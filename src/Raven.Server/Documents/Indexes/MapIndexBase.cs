﻿using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Nest;
using Raven.Client.Documents.Indexes;
using Raven.Server.Documents.Includes;
using Raven.Server.Documents.Indexes.Persistence;
using Raven.Server.Documents.Indexes.Persistence.Lucene;
using Raven.Server.Documents.Indexes.Workers;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Queries.Results;
using Raven.Server.Documents.Queries.Timings;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Indexes
{
    public abstract class MapIndexBase<T, TField> : Index<T, TField> where T : IndexDefinitionBaseServerSide<TField> where TField : IndexFieldBase
    {
        private CollectionOfBloomFilters _filters;
        private IndexingStatsScope _statsInstance;
        private readonly MapStats _stats = new MapStats();

        protected MapIndexBase(IndexType type, IndexSourceType sourceType, T definition) : base(type, sourceType, definition)
        {
        }

        protected override IIndexingWork[] CreateIndexWorkExecutors()
        {
            return new IIndexingWork[]
            {
                new CleanupDocuments(this, DocumentDatabase.DocumentsStorage, _indexStorage, Configuration, null),
                new MapDocuments(this, DocumentDatabase.DocumentsStorage, _indexStorage, null, Configuration)
            };
        }

        public override IDisposable InitializeIndexingWork(TransactionOperationContext indexContext)
        {
            var mode = DocumentDatabase.Is32Bits
                ? CollectionOfBloomFilters.Mode.X86
                : CollectionOfBloomFilters.Mode.X64;

            if (_filters == null || _filters.Consumed == false)
                _filters = CollectionOfBloomFilters.Load(mode, indexContext);

            return _filters;
        }

        public override void HandleDelete(Tombstone tombstone, string collection, Lazy<IndexWriteOperationBase> writer, TransactionOperationContext indexContext, IndexingStatsScope stats)
        {
            writer.Value.Delete(tombstone.LowerId, stats);
        }

        public override int HandleMap(IndexItem indexItem, IEnumerable mapResults, Lazy<IndexWriteOperationBase> writer, TransactionOperationContext indexContext, IndexingStatsScope stats)
        {
            EnsureValidStats(stats);

            int numberOfOutputs;
            if (SearchEngineType == SearchEngineType.Lucene)
            {
                numberOfOutputs = UpdateIndexEntriesLucene(indexItem, mapResults, writer, indexContext, stats);
            }
            else if(SearchEngineType == SearchEngineType.Corax)
            {
                numberOfOutputs = UpdateIndexEntriesCorax(indexItem, mapResults, writer, indexContext, stats);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(SearchEngineType), SearchEngineType + " is not a supported value");
            }


            HandleIndexOutputsPerDocument(indexItem.Id ?? indexItem.LowerId, numberOfOutputs, stats);
            HandleSourceDocumentIncludedInMapOutput();
            
            DocumentDatabase.Metrics.MapIndexes.IndexedPerSec.Mark(numberOfOutputs);

            return numberOfOutputs;
        }

        private int UpdateIndexEntriesCorax(IndexItem indexItem, IEnumerable mapResults, Lazy<IndexWriteOperationBase> writer, TransactionOperationContext indexContext, IndexingStatsScope stats)
        {
            var it = mapResults.GetEnumerator();
            try
            {
                if (it.MoveNext() == false)
                {
                    writer.Value.Delete(indexItem.LowerId, stats);
                    return 0; // no results at all
                }

                var first = it.Current;

                if (it.MoveNext() == false)
                {
                    // we have just _one_ entry for the map, can try to optimize
                    writer.Value.UpdateDocument(Raven.Client.Constants.Documents.Indexing.Fields.DocumentIdFieldName,
                        indexItem.LowerId, indexItem.LowerSourceDocumentId, first, stats, indexContext);
                    return 1;
                }
                else
                {
                    writer.Value.Delete(indexItem.LowerId, stats);
                    writer.Value.IndexDocument(indexItem.LowerId, indexItem.LowerSourceDocumentId, first, stats, indexContext);
                    var numberOfOutputs = 1; // the first
                    do
                    {
                        numberOfOutputs++;
                        writer.Value.IndexDocument(indexItem.LowerId, indexItem.LowerSourceDocumentId, it.Current, stats, indexContext);
                    } while (it.MoveNext());

                    return numberOfOutputs;
                }

            }
            finally
            {
                if(it is IDisposable d)
                    d.Dispose();
            }
        }

        private int UpdateIndexEntriesLucene(IndexItem indexItem, IEnumerable mapResults, Lazy<IndexWriteOperationBase> writer, TransactionOperationContext indexContext, IndexingStatsScope stats)
        {
            bool mustDelete;
            using (_stats.BloomStats.Start())
            {
                mustDelete = _filters.Add(indexItem.LowerId) == false;
            }

            if (indexItem.SkipLuceneDelete == false && mustDelete)
                writer.Value.Delete(indexItem.LowerId, stats);

            var numberOfOutputs = 0;
            foreach (var mapResult in mapResults)
            {
                writer.Value.IndexDocument(indexItem.LowerId, indexItem.LowerSourceDocumentId, mapResult, stats, indexContext);

                numberOfOutputs++;
            }

            return numberOfOutputs;
        }

        public override IQueryResultRetriever GetQueryResultRetriever(IndexQueryServerSide query, QueryTimingsScope queryTimings, DocumentsOperationContext documentsContext, SearchEngineType searchEngineType, FieldsToFetch fieldsToFetch, IncludeDocumentsCommand includeDocumentsCommand, IncludeCompareExchangeValuesCommand includeCompareExchangeValuesCommand, IncludeRevisionsCommand includeRevisionsCommand)
        {
            return new MapQueryResultRetriever(DocumentDatabase, query, queryTimings, DocumentDatabase.DocumentsStorage, documentsContext, searchEngineType, fieldsToFetch, includeDocumentsCommand, includeCompareExchangeValuesCommand,includeRevisionsCommand: includeRevisionsCommand);
        }

        public override void SaveLastState()
        {
            _filters?.Flush();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureValidStats(IndexingStatsScope stats)
        {
            if (_statsInstance == stats)
                return;

            _statsInstance = stats;
            _stats.BloomStats = stats.For(IndexingOperation.Map.Bloom, start: false);
        }

        private class MapStats
        {
            public IndexingStatsScope BloomStats;
        }
    }
}
