using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Sparrow;
using Sparrow.Server;
using Voron.Debugging;
using Voron.Global;
using Voron.Impl;

namespace Voron.Data.Sets
{
    public unsafe class Set : IDisposable
    {
        public Slice Name;
        private readonly LowLevelTransaction _llt;
        private SetState _state;
        private UnmanagedSpan<SetCursorState> _stk;
        private int _pos = -1, _len;
        private readonly ByteStringContext<ByteStringMemoryCache>.InternalScope _scope;

        public SetState State => _state;
        internal LowLevelTransaction Llt => _llt;

        public Set(LowLevelTransaction llt, Slice name, in SetState state)
        {
            if (state.RootObjectType != RootObjectType.Set)
                throw new InvalidOperationException($"Tried to open {name} as a set, but it is actually a " +
                                                    state.RootObjectType);
            Name = name;
            _llt = llt;
            _state = state;

            // PERF: We dont have the ability to dispose Set (because of how it is used) therefore,
            // we will just discard the memory as reclaiming it may be even more costly.  
            _scope = llt.Allocator.AllocateDirect(8 * sizeof(SetCursorState), out ByteString buffer);
            _stk = new UnmanagedSpan<SetCursorState>(buffer.Ptr, buffer.Size);
        }

        public static void Create(LowLevelTransaction tx, ref SetState state)
        {
            var newPage = tx.AllocatePage(1);
            new SetLeafPage(newPage).Init(0);
            state.RootObjectType = RootObjectType.Set;
            state.Depth = 1;
            state.BranchPages = 0;
            state.LeafPages = 1;
            state.RootPage = newPage.PageNumber;
        }

        public void Remove(long value)
        {
            // caller ensures that the value *already exists* in the set
            FindPageFor(value);
            ref var state = ref _stk[_pos];
            state.Page = _llt.ModifyPage(state.Page.PageNumber);
            var leaf = new SetLeafPage(state.Page);
            if (leaf.IsValidValue(value) == false)
                return; // value does not exists in tree

            if (leaf.Remove(_llt, value)) // removed value properly
            {
                _state.NumberOfEntries = Math.Max(0, _state.NumberOfEntries - 1);
                if (_pos == 0)
                    return;  // this is the root page

                if (leaf.SpaceUsed > Constants.Storage.PageSize / 4)
                    return; // don't merge too eagerly

                MaybeMergeLeafPage(in leaf);
                return;
            }
            // could not store the new value (rare, but can happen)
            // need to split on remove :-(
            _state.LeafPages++;
            // we need to always split by half here, so we'll have enough space to
            // write the new removed entry
            var (separator, newPage) = SplitLeafPageInHalf(value, leaf, state);
            AddToParentPage(separator, newPage);
            Remove(value); // now we can properly store the new value
        }

        private void MaybeMergeLeafPage(in SetLeafPage leaf)
        {
            if (_pos == 0)
                return; // no parent branch to go to...
            
            PopPage();
            
            ref var parent = ref _stk[_pos];
            
            var branch = new SetBranchPage(parent.Page);
            Debug.Assert(branch.Header->NumberOfEntries >= 2);
            var siblingIdx = parent.LastSearchPosition == 0 ? 1 : parent.LastSearchPosition - 1;
            var (_, siblingPageNum) = branch.GetByIndex(siblingIdx);

            var siblingPage = _llt.GetPage(siblingPageNum);
            var siblingHeader = (SetLeafPageHeader*)siblingPage.Pointer;
            if (siblingHeader->SetFlags != ExtendedPageType.SetLeaf)
                return;

            if (siblingHeader->Baseline != leaf.Header->Baseline)
                return; // we cannot merge pages from different leafs

            var sibling = new SetLeafPage(siblingPage);
            if (sibling.SpaceUsed + leaf.SpaceUsed > Constants.Storage.PageSize / 2 + Constants.Storage.PageSize / 4)
                return; // if the two pages together will be bigger than 75%, can skip merging

            var it = sibling.GetIterator(_llt);
            while (it.MoveNext(out long v))
            {
                if (leaf.Add(_llt, v) == false)
                    throw new InvalidOperationException("Even though we have 25% spare capacity, we run out?! Should not happen ever");
            }

            MergeSiblingsAtParent();
        }

        private void MergeSiblingsAtParent()
        {
            ref var state = ref _stk[_pos];
            state.Page = _llt.ModifyPage(state.Page.PageNumber);
            
            var current = new SetBranchPage(state.Page);
            Debug.Assert(current.Header->SetFlags == ExtendedPageType.SetBranch);
            var (siblingKey, siblingPageNum) = current.GetByIndex(GetSiblingIndex(in state));
            var (leafKey, leafPageNum) = current.GetByIndex(state.LastSearchPosition);

            var siblingPageHeader = (SetLeafPageHeader*)_llt.GetPage(siblingPageNum).Pointer;
            if (siblingPageHeader->SetFlags == ExtendedPageType.SetBranch)
                _state.BranchPages--;
            else
                _state.LeafPages--;
            
            _llt.FreePage(siblingPageNum);
            current.Remove(siblingKey);
            current.Remove(leafKey);

            // if it is empty, can just replace with the child
            if (current.Header->NumberOfEntries == 0)
            {
                var leafPage = _llt.GetPage(leafPageNum);
                
                long cpy = state.Page.PageNumber;
                leafPage.CopyTo(state.Page);
                state.Page.PageNumber = cpy;

                if (_pos == 0)
                    _state.Depth--; // replaced the root page

                _state.BranchPages--;
                _llt.FreePage(leafPageNum);
                return;
            }

            var newKey = Math.Min(siblingKey, leafKey);
            if (current.TryAdd(_llt, newKey, leafPageNum) == false)
                throw new InvalidOperationException("We just removed two values to add one, should have enough space. This error should never happen");

            if (_pos == 0)
                return; // root has no siblings

            if (current.Header->NumberOfEntries > SetBranchPage.MinNumberOfValuesBeforeMerge)
                return;

            PopPage();
            ref var parent = ref _stk[_pos];
            
            var gp = new SetBranchPage(parent.Page);
            var siblingIdx = GetSiblingIndex(parent);
            (_, siblingPageNum) = gp.GetByIndex(siblingIdx);
            var siblingPage = _llt.GetPage(siblingPageNum);
            var siblingHeader = (SetLeafPageHeader*)siblingPage.Pointer;
            if (siblingHeader->SetFlags != ExtendedPageType.SetBranch)
                return;// cannot merge leaf & branch
            
            var sibling = new SetBranchPage(siblingPage);
            if (sibling.Header->NumberOfEntries + current.Header->NumberOfEntries > SetBranchPage.MinNumberOfValuesBeforeMerge * 2)
                return; // not enough space to _ensure_ that we can merge

            for (int i = 0; i < sibling.Header->NumberOfEntries; i++)
            {
                (long key, long page) = sibling.GetByIndex(i);
                if(current.TryAdd(_llt, key, page) == false)
                    throw new InvalidOperationException("Even though we have checked for spare capacity, we run out?! Should not hapen ever");
            }

            MergeSiblingsAtParent();
        }

        private static int GetSiblingIndex(in SetCursorState parent)
        {
            return parent.LastSearchPosition == 0 ? 1 : parent.LastSearchPosition - 1;
        }

        public List<long> DumpAllValues()
        {
            var iterator = Iterate();
            Span<long> buffer = stackalloc long[1024];
            var results = new List<long>();
            while (iterator.Fill(buffer, out var read) && read != 0)
            {
                results.AddRange(buffer[0..read].ToArray());
            }

            return results;
        }

        /// <summary>
        /// We do a bulk removal of the values in the tree. The values are *assumed to already exists* in the tree.
        /// </summary>
        public void Remove(ReadOnlySpan<long> values)
        {
            int index = 0;
            while (index < values.Length)
            {
                FindPageFor(values[index]);
                ref var state = ref _stk[_pos];
                state.Page = _llt.ModifyPage(state.Page.PageNumber);
                var leafPage = new SetLeafPage(state.Page);
                if (leafPage.Header->Baseline != (values[index] & ~int.MaxValue))
                {
                    throw new InvalidOperationException($"Attempted to remove a value {values[index]} that is no located in page {state.Page.PageNumber} (baseline: {leafPage.Header->Baseline})");
                }

                long limit = Math.Min(NextParentLimit(), leafPage.Header->Baseline + int.MaxValue + 1);

                for (; index < values.Length && values[index] < limit; index++)
                {
                    if (leafPage.Remove(_llt, values[index]) == false)
                    {
                        // shouldn't really happen, but may because removing a value may change
                        // the compression rate. if we can't add the removal, we'll just remove
                        // using single value method, and resume the bulk mode on the next one 
                        Remove(values[index++]);
                        break; 
                    }
                    _state.NumberOfEntries--;
                }

                // because we are in bulk mode, rather than try to be eager about this
                // we only want to try to do a merge if the  page is 75% empty or the
                // space used is less than 25% of the total size 
                if (leafPage.SpaceUsed < Constants.Storage.PageSize / 4) 
                    MaybeMergeLeafPage(in leafPage);
            }
        }

        public void Add(ReadOnlySpan<long> values)
        {
            // NOTE: We assume that values is sorted
            
            int index = 0;
#if DEBUG
            var prev = long.MinValue;
#endif
            while (index < values.Length)
            {               
                FindPageFor(values[index]);
                ref var state = ref _stk[_pos];

                state.Page = _llt.ModifyPage(state.Page.PageNumber);

                var leafPage = new SetLeafPage(state.Page);

                // Two different conditions may force us to move into single value insertions.
                // Either the value is outside range on the upside, OR it is within range
                // but because we are not going to be adding elements below the Baseline we 
                // need to create another leaf page to deal with that one. This is the case
                // when values are separated by more than int.MaxValue. 

                long last = NextParentLimit();
                if (leafPage.IsValidValue(last) == false)
                {
                    // must still fit in the page
                    last = leafPage.Header->Baseline + int.MaxValue;
                    if (values[index] > last)
                    {
                        // add a single item, forcing new page creation
                        Add(values[index++]);
                        continue;
                    }
                }

                if (values[index] < leafPage.Header->Baseline)
                {
                    // Since FindPageFor will return the minimal leaf page that holds this range,
                    // we need to create a new leaf page to hold this value as the current baseline
                    // is incompatible. 
                    Add(values[index++]);
                    continue;
                }

                for (; index < values.Length && values[index] < last; index++)
                {
#if DEBUG
                    if(prev > values[index])
                        throw new InvalidOperationException("Values not sorted");
                    prev = values[index];
#endif
                    if (leafPage.Add(_llt, values[index]))
                    {
                        _state.NumberOfEntries++;
                        continue; // successfully added
                    }
                    // we couldn't add to the page (but it fits, need to split)
                    var (separator, newPage) = SplitLeafPage(values[index]);
                    AddToParentPage(separator, newPage);
#if DEBUG
                    prev = values[index];
#endif
                    break; 
                }
            }
        }

        private long NextParentLimit()
        {
            var cur = _pos;
            while (cur > 0)
            {
                ref var state = ref _stk[cur - 1];
                if (state.LastSearchPosition + 1 < state.BranchHeader->NumberOfEntries)
                {
                    var (key, _) = new SetBranchPage(state.Page).GetByIndex(state.LastSearchPosition + 1);
                    return key;
                }
                cur--;
            }
            return long.MaxValue;
        }

        public void Add(long value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Only positive values are allowed");

            FindPageFor(value);
            AddToPage(value);
        }

        private void AddToPage(long value)
        {
            ref var state = ref _stk[_pos];

            state.Page = _llt.ModifyPage(state.Page.PageNumber);

            var leafPage = new SetLeafPage(state.Page);
            if (leafPage.IsValidValue(value) && // may have enough space, but too far out to fit 
                leafPage.Add(_llt, value))
            {
                _state.NumberOfEntries++;
                return; // successfully added
            }
        

            if (leafPage.IsValidValue(value) == false) 
            {
                if (leafPage.Header->NumberOfCompressedPositions == 0 &&
                    leafPage.Header->NumberOfRawValues == 0)
                {
                    // never had a write, the baseline is wrong, can update 
                    // this and move on
                    leafPage.Header->Baseline = value & ~int.MaxValue;
                    if(leafPage.Add(_llt, value) == false)
                        throw new InvalidOperationException("Adding value to empty page failed?!");
                    return;
                }
            }

            var (separator, newPage) = SplitLeafPage(value);
            AddToParentPage(separator, newPage);
            Add(value); // now add the value after the split
        }

        private void AddToParentPage(long separator, long newPage)
        {
            if (_pos == 0) // need to create a root page
            {
                CreateRootPage();
            }

            PopPage();
            ref var state = ref _stk[_pos];
            state.Page = _llt.ModifyPage(state.Page.PageNumber);
            var parent = new SetBranchPage(state.Page);
            if (parent.TryAdd(_llt, separator, newPage))
                return;

            SplitBranchPage(separator, newPage);
        }

        private void SplitBranchPage(long key, long value)
        {
            ref var state = ref _stk[_pos];

            var pageToSplit = new SetBranchPage(state.Page);
            var page = _llt.AllocatePage(1);
            var branch = new SetBranchPage(page);
            branch.Init();
            _state.BranchPages++;
            
            // grow rightward
            if (key > pageToSplit.Last)
            {
                if (branch.TryAdd(_llt, key, value) == false)
                    throw new InvalidOperationException("Failed to add to a newly created page? Should never happen");
                AddToParentPage(key, page.PageNumber);
                return;
            }

            // grow leftward
            if (key < pageToSplit.First)
            {
                long oldFirst = pageToSplit.First;
                var cpy = page.PageNumber;
                state.Page.AsSpan().CopyTo(page.AsSpan());
                page.PageNumber = cpy;

                cpy = state.Page.PageNumber;
                state.Page.AsSpan().Clear();
                state.Page.PageNumber = cpy;

                var curPage = new SetBranchPage(state.Page);
                curPage.Init();
                if(curPage.TryAdd(_llt, key, value) == false)
                    throw new InvalidOperationException("Failed to add to a newly initialized page? Should never happen");
                AddToParentPage(oldFirst, page.PageNumber);
                return;
            }

            // split in half
            for (int i = pageToSplit.Header->NumberOfEntries / 2; i < pageToSplit.Header->NumberOfEntries; i++)
            {
                var (k, v) = pageToSplit.GetByIndex(i);
                if(branch.TryAdd(_llt, k, v) == false)
                    throw new InvalidOperationException("Failed to add half our capacity to a newly created page? Should never happen");
            }

            pageToSplit.Header->NumberOfEntries /= 2;// truncate entries
            var success = pageToSplit.Last > key ?
                branch.TryAdd(_llt, key, value) :
                pageToSplit.TryAdd(_llt, key, value);
            if(success == false)
                throw new InvalidOperationException("Failed to add final to a newly created page after adding half the capacit? Should never happen");

            AddToParentPage(branch.First, page.PageNumber);
        }

        private (long Separator, long NewPage) SplitLeafPage(long value)
        {
            ref var state = ref _stk[_pos];
            var curPage = new SetLeafPage(state.Page);
            var (first, last) = curPage.GetRange();
            _state.LeafPages++;

            if (value >= first && value <= last)
            {
                return SplitLeafPageInHalf(value, curPage, state);
            }

            Page page;
            if (value > last)
            {
                // optimize sequential writes, can create a new page directly
                page = _llt.AllocatePage(1);
                var newPage = new SetLeafPage(page);
                newPage.Init(value);
                return (value, page.PageNumber);
            }
            Debug.Assert(first > value);
            // smaller than current, we'll move the higher values to the new location
            // instead of update the entry position
            page = _llt.AllocatePage(1);
            var cpy = page.PageNumber;
            curPage.Span.CopyTo(page.AsSpan());
            page.PageNumber = cpy;

            cpy = state.Page.PageNumber;
            curPage.Span.Clear();
            state.Page.PageNumber = cpy;

            curPage.Init(value);
            return (first, page.PageNumber);
        }

        private (long Separator, long NewPage) SplitLeafPageInHalf(long value, SetLeafPage curPage, in SetCursorState state)
        {
            // we have to split this in the middle page
            var page = _llt.AllocatePage(1);
            var newPage = new SetLeafPage(page);

            curPage.SplitHalfInto(ref newPage);
            Debug.Assert(curPage.Header->SetFlags == ExtendedPageType.SetLeaf);
            Debug.Assert(newPage.Header->SetFlags == ExtendedPageType.SetLeaf);

            var (start, _) = newPage.GetRange();
            return (start, page.PageNumber);
        }



        [Conditional("DEBUG")]
        public void Render()
        {
            DebugStuff.RenderAndShow(this);
        }

        private void CreateRootPage()
        {
            _state.Depth++;
            _state.BranchPages++;
            // we'll copy the current page and reuse it, to avoid changing the root page number
            var page = _llt.AllocatePage(1);
            long cpy = page.PageNumber;
            ref var state = ref _stk[_pos];
            Memory.Copy(page.Pointer, state.Page.Pointer, Constants.Storage.PageSize);
            page.PageNumber = cpy;
            Memory.Set(state.Page.DataPointer, 0, Constants.Storage.PageSize - PageHeader.SizeOf);
            var rootPage = new SetBranchPage(state.Page);
            rootPage.Init();
            rootPage.TryAdd(_llt, long.MinValue, cpy);

            InsertToStack(new SetCursorState
            {
                Page = page,
                LastMatch = state.LastMatch,
                LastSearchPosition = state.LastSearchPosition
            });
            state.LastMatch = -1;
            state.LastSearchPosition = 0;
        }

        private void InsertToStack(SetCursorState newPageState)
        {
            // insert entry and shift other elements
            if (_len + 1 >= _stk.Length) // should never happen
                ResizeCursorState();

            var src = _stk.ToReadOnlySpan().Slice(_pos + 1, _len - (_pos + 1));
            var dest = _stk.ToSpan().Slice(_pos + 2);
            src.CopyTo(dest);

            _len++;
            _stk[_pos + 1] = newPageState;
            _pos++;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ResizeCursorState()
        {
            _llt.Allocator.Allocate(_stk.Length * 2 * sizeof(SetCursorState), out ByteString buffer);
            var newStk = new UnmanagedSpan<SetCursorState>(buffer.Ptr, buffer.Size);
            _stk.ToReadOnlySpan().CopyTo(newStk.ToSpan());
            _stk = newStk;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SetCursorState* FindSmallestValue()
        {
            _pos = -1;
            _len = 0;
            PushPage(_state.RootPage);

            var state = _stk.GetAsPtr(_pos);
            while (state->IsLeaf == false)
            {
                var branch = new SetBranchPage(state->Page);

                // Until we hit a leaf, just take the left-most key and move on. 
                long nextPage = branch.GetPageByIndex(0);
                PushPage(nextPage);

                state = _stk.GetAsPtr(_pos);
            }

            return state;
        }

        private void FindPageFor(long value)
        {
            _pos = -1;
            _len = 0;
            PushPage(_state.RootPage);
            ref var state = ref _stk[_pos];

            while (state.IsLeaf == false)
            {
                var branch = new SetBranchPage(state.Page);
                (long nextPage, state.LastSearchPosition, state.LastMatch) = branch.SearchPage(value);

                PushPage(nextPage);

                state = ref _stk[_pos];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PopPage()
        {
            _stk[_pos--] = default;
            _len--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PushPage(long nextPage)
        {
            if (_pos + 1 >= _stk.Length) //  should never actually happen
                ResizeCursorState();

            Page page = _llt.GetPage(nextPage);
            _pos++; 
            
            var state = _stk.GetAsPtr(_pos);
            state->Page = page;
            state->LastMatch = 0;
            state->LastSearchPosition = 0;

            _len++;
        }

        public Iterator Iterate()
        {
            return new Iterator(this);
        }

        public struct Iterator 
        {
            private readonly Set _parent;
            private SetLeafPage.Iterator _it;

            public long Current;

            public Iterator(Set parent)
            {
                _parent = parent;
                Current = default;

                // We need to find the long.MinValue therefore the fastest way is to always
                // take the left-most pointer on any branch node.
                var state = _parent.FindSmallestValue();

                var leafPage = new SetLeafPage(state->Page);
                _it = leafPage.GetIterator(_parent._llt);
            }

            public bool? MaybeSeek(long from)
            {                
                // TODO: In case that we are in the right location but we have passed over,
                //       we may be able to use a optimized Seek method instead which would
                //       avoid getting the page and just start over in the current segment.
                if (Current < from && _it.IsInRange(from)) 
                    return null;
                return Seek(from);
            }

            public bool Seek(long from = long.MinValue)
            {
                _parent.FindPageFor(from);
                ref var state = ref _parent._stk[_parent._pos];
                var leafPage = new SetLeafPage(state.Page);

                _it = leafPage.GetIterator(_parent._llt);
                return _it.Skip(from);
            }

            public bool Fill(Span<long> matches, out int total, long pruneGreaterThanOptimization = long.MaxValue)
            {
                // We will try to fill.
                total = _it.TryFill(matches, pruneGreaterThanOptimization);
                          
                while(true)
                {
                    var tmp = matches.Slice(total);
                    _it.Fill(tmp, out var read, out bool hasPrunedResults,  pruneGreaterThanOptimization);                                                                                      

                    // We haven't read anything, but we are not getting a pruned result.
                    if (read == 0 && hasPrunedResults == false)
                    {
                        var parent = _parent;
                        if (parent._pos == 0)
                            break;

                        parent.PopPage();

                        var llt = parent._llt;

                        while (true)
                        {
                            ref var state = ref parent._stk[_parent._pos];
                            state.LastSearchPosition++;
                            Debug.Assert(state.IsLeaf == false);
                            if (state.LastSearchPosition >= state.BranchHeader->NumberOfEntries)
                            {
                                if (parent._pos == 0)
                                    break;

                                parent.PopPage();
                                continue;
                            }

                            var branch = new SetBranchPage(state.Page);
                            (_, long pageNum) = branch.GetByIndex(state.LastSearchPosition);
                            var page = llt.GetPage(pageNum);
                            var header = (SetLeafPageHeader*)page.Pointer;

                            parent.PushPage(pageNum);

                            if (header->SetFlags == ExtendedPageType.SetBranch)
                            {
                                // we'll increment on the next
                                parent._stk[parent._pos].LastSearchPosition = -1;
                                continue;
                            }
                            _it = new SetLeafPage(page).GetIterator(llt);
                            break;
                        }
                    }                        

                    total += read;
                    if (total == matches.Length)
                        break; // We are done.  

                    // We have reached the end by prunning.
                    if (hasPrunedResults)
                        break; // We are done.
                }

                if (total != 0)
                    Current = matches[total - 1];

                return total != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_it.MoveNext(out Current))
                    return true;

                var parent = _parent;
                if (parent._pos == 0)
                    return false;

                parent.PopPage();
                
                var llt = parent._llt;

                var it = _it;
                bool result = false;
                while (true)
                {
                    ref var state = ref parent._stk[_parent._pos];
                    state.LastSearchPosition++;
                    Debug.Assert(state.IsLeaf == false);
                    if (state.LastSearchPosition >= state.BranchHeader->NumberOfEntries)
                    {
                        if (parent._pos == 0)
                            break;

                        parent.PopPage();
                        continue;
                    }

                    var branch = new SetBranchPage(state.Page);
                    (_, long pageNum) = branch.GetByIndex(state.LastSearchPosition);
                    var page = llt.GetPage(pageNum);
                    var header = (SetLeafPageHeader*)page.Pointer;

                    parent.PushPage(pageNum);

                    if (header->SetFlags == ExtendedPageType.SetBranch)
                    {
                        // we'll increment on the next
                        parent._stk[parent._pos].LastSearchPosition = -1;
                        continue;
                    }
                    it = new SetLeafPage(page).GetIterator(llt);
                    if (it.MoveNext(out Current))
                    {
                        result = true;
                        break;
                    }
                }

                _it = it;
                return result;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }

        public List<long> AllPages()
        {
            var result = new List<long>();
            Add(_llt.GetPage(_state.RootPage));
            return result;

            void Add(Page p)
            {
                result.Add(p.PageNumber);
                var state = new SetCursorState { Page = p, };
                if (state.BranchHeader->SetFlags != ExtendedPageType.SetBranch)
                    return;
                
                var branch = new SetBranchPage(state.Page);
                foreach (var child in branch.GetAllChildPages())
                {
                    var childPage = _llt.GetPage(child);
                    Add(childPage);
                }
            }
        }

        public void Dispose()
        {
            _scope.Dispose();
        }
    }
}
