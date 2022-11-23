﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Corax.Queries;
using Corax.Utils;

namespace Corax;

public partial class IndexSearcher
{
    public MultiTermMatch InQuery(string field, List<string> inTerms, int fieldId = Constants.IndexSearcher.NonAnalyzer)
    {
        var terms = _fieldsTree?.CompactTreeFor(field);
        if (terms == null)
        {
            // If either the term or the field does not exist the request will be empty. 
            return MultiTermMatch.CreateEmpty(_transaction.Allocator);
        }

        if (inTerms.Count is > 1 and <= 16)
        {
            var stack = new BinaryMatch[inTerms.Count / 2];
            for (int i = 0; i < inTerms.Count / 2; i++)
                stack[i] = Or(TermQuery(terms, inTerms[i * 2], fieldId), TermQuery(terms, inTerms[i * 2 + 1], fieldId));

            if (inTerms.Count % 2 == 1)
            {
                // We need even values to make the last work. 
                stack[^1] = Or(stack[^1], TermQuery(terms, inTerms[^1], fieldId));
            }

            int currentTerms = stack.Length;
            while (currentTerms > 1)
            {
                int termsToProcess = currentTerms / 2;
                int excessTerms = currentTerms % 2;

                for (int i = 0; i < termsToProcess; i++)
                    stack[i] = Or(stack[i * 2], stack[i * 2 + 1]);

                if (excessTerms != 0)
                    stack[termsToProcess - 1] = Or(stack[termsToProcess - 1], stack[currentTerms - 1]);

                currentTerms = termsToProcess;
            }

            return MultiTermMatch.Create(stack[0]);
        }

        return MultiTermMatch.Create(new MultiTermMatch<InTermProvider>(_transaction.Allocator, new InTermProvider(this, field, inTerms, fieldId)));
    }

    //Unlike the In operation, this one requires us to check all entries in a given entry.
    //However, building a query with And can quickly lead to a Stackoverflow Exception.
    //In this case, when we get more conditions, we have to quit building the tree and manually check the entries with UnaryMatch.
    public IQueryMatch AllInQuery(string field, HashSet<string> allInTerms, int fieldId = Constants.IndexSearcher.NonAnalyzer)
    {
        var terms = _fieldsTree?.CompactTreeFor(field);

        if (terms == null)
        {
            // If either the term or the field does not exist the request will be empty. 
            return MultiTermMatch.CreateEmpty(_transaction.Allocator);
        }

        //TODO PERF
        //Since comparing lists is expensive, we will try to reduce the set of likely candidates as much as possible.
        //Therefore, we check the density of elements present in the tree.
        //In the future, this can be optimized by adding some values at which it makes sense to skip And and go directly into checking.
        TermQueryItem[] list = new TermQueryItem[allInTerms.Count];
        var it = 0;
        foreach (var item in allInTerms)
        {
            var itemSlice = EncodeAndApplyAnalyzer(item, fieldId);
            var amount = TermAmount(terms, itemSlice);
            if (amount == 0)
            {
                return MultiTermMatch.CreateEmpty(_transaction.Allocator);
            }

            list[it++] = new TermQueryItem(itemSlice, amount);
        }


        //Sort by density.
        Array.Sort(list, (tuple, valueTuple) => tuple.Density.CompareTo(valueTuple.Density));

        var allInTermsCount = (allInTerms.Count % 16);
        var stack = new BinaryMatch[allInTermsCount / 2];
        for (int i = 0; i < allInTermsCount / 2; i++)
        {
            var term1 = TermQuery(terms, list[i * 2].Item.Span, fieldId);
            var term2 = TermQuery(terms, list[i * 2 + 1].Item.Span, fieldId);
            stack[i] = And(term1, term2);
        }

        if (allInTermsCount % 2 == 1)
        {
            // We need even values to make the last work. 
            var term = TermQuery(terms, list[^1].Item.Span, fieldId);
            stack[^1] = And(stack[^1], term);
        }

        int currentTerms = stack.Length;
        while (currentTerms > 1)
        {
            int termsToProcess = currentTerms / 2;
            int excessTerms = currentTerms % 2;

            for (int i = 0; i < termsToProcess; i++)
                stack[i] = And(stack[i * 2], stack[i * 2 + 1]);

            if (excessTerms != 0)
                stack[termsToProcess - 1] = And(stack[termsToProcess - 1], stack[currentTerms - 1]);

            currentTerms = termsToProcess;
        }


        //Just perform normal And.
        if (allInTerms.Count is > 1 and <= 16)
            return MultiTermMatch.Create(stack[0]);


        //We don't have to check previous items. We have to check if those entries contain the rest of them.
        list = list[16..];

        //BinarySearch requires sorted array.
        Array.Sort(list, ((item, inItem) => item.Item.Span.SequenceCompareTo(inItem.Item.Span)));
        return UnaryQuery(stack[0], fieldId, list, UnaryMatchOperation.AllIn, -1);
    }

    public MultiTermMatch InQuery<TScoreFunction>(string field, List<string> inTerms, TScoreFunction scoreFunction, int fieldId = Constants.IndexSearcher.NonAnalyzer)
        where TScoreFunction : IQueryScoreFunction
    {
        var terms = _fieldsTree?.CompactTreeFor(field);
        if (terms == null)
        {
            // If either the term or the field does not exist the request will be empty. 
            return MultiTermMatch.CreateEmpty(_transaction.Allocator);
        }

        if (inTerms.Count is > 1 and <= 16)
        {
            var stack = new BinaryMatch[inTerms.Count / 2];
            for (int i = 0; i < inTerms.Count / 2; i++)
            {
                var term1 = Boost(TermQuery(terms, inTerms[i * 2], fieldId), scoreFunction);
                var term2 = Boost(TermQuery(terms, inTerms[i * 2 + 1], fieldId), scoreFunction);
                stack[i] = Or(term1, term2);
            }

            if (inTerms.Count % 2 == 1)
            {
                // We need even values to make the last work. 
                var term = Boost(TermQuery(terms, inTerms[^1], fieldId), scoreFunction);
                stack[^1] = Or(stack[^1], term);
            }

            int currentTerms = stack.Length;
            while (currentTerms > 1)
            {
                int termsToProcess = currentTerms / 2;
                int excessTerms = currentTerms % 2;

                for (int i = 0; i < termsToProcess; i++)
                    stack[i] = Or(stack[i * 2], stack[i * 2 + 1]);

                if (excessTerms != 0)
                    stack[termsToProcess - 1] = Or(stack[termsToProcess - 1], stack[currentTerms - 1]);

                currentTerms = termsToProcess;
            }

            return MultiTermMatch.Create(stack[0]);
        }

        return MultiTermMatch.Create(
            MultiTermBoostingMatch<InTermProvider>.Create(
                this, new InTermProvider(this, field, inTerms, fieldId), scoreFunction));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AndNotMatch NotInQuery<TInner>(string field, TInner inner, List<string> notInTerms, int fieldId)
        where TInner : IQueryMatch
    {
        return AndNot(inner, MultiTermMatch.Create(new MultiTermMatch<InTermProvider>(_transaction.Allocator, new InTermProvider(this, field, notInTerms, fieldId))));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AndNotMatch NotInQuery<TScoreFunction, TInner>(string field, TInner inner, List<string> notInTerms, int fieldId, TScoreFunction scoreFunction)
        where TScoreFunction : IQueryScoreFunction
        where TInner : IQueryMatch
    {
        return AndNot(inner, MultiTermMatch.Create(
            MultiTermBoostingMatch<InTermProvider>.Create(
                this, new InTermProvider(this, field, notInTerms, fieldId), scoreFunction)));
    }
}
