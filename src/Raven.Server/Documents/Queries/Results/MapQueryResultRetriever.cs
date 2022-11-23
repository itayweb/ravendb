﻿using System;
using System.Collections.Generic;
using System.Threading;
using Lucene.Net.Store;
using Raven.Client;
using Raven.Client.Documents.Indexes;
using Raven.Server.Documents.Includes;
using Raven.Server.Documents.Queries.Timings;
using Raven.Server.ServerWide.Context;
using Sparrow.Json.Parsing;
using Voron;

namespace Raven.Server.Documents.Queries.Results
{
    public class MapQueryResultRetriever : QueryResultRetrieverBase
    {
        private readonly DocumentsOperationContext _context;
        private QueryTimingsScope _storageScope;

        public MapQueryResultRetriever(DocumentDatabase database, IndexQueryServerSide query, QueryTimingsScope queryTimings, DocumentsStorage documentsStorage, DocumentsOperationContext context, SearchEngineType searchEngineType, FieldsToFetch fieldsToFetch, IncludeDocumentsCommand includeDocumentsCommand, IncludeCompareExchangeValuesCommand includeCompareExchangeValuesCommand, IncludeRevisionsCommand includeRevisionsCommand)
            : base(database, query, queryTimings, searchEngineType, fieldsToFetch, documentsStorage, context, false, includeDocumentsCommand, includeCompareExchangeValuesCommand, includeRevisionsCommand: includeRevisionsCommand)
        {
            _context = context;
        }

        public override (Document Document, List<Document> List) Get(ref RetrieverInput retrieverInput, CancellationToken token)
        {

            using (RetrieverScope?.Start())
            {
                string id = string.Empty;
                switch (SearchEngineType)
                {
                    case SearchEngineType.Corax:
                        id = retrieverInput.DocumentId;
                        break;
                    case SearchEngineType.None:
                    case SearchEngineType.Lucene:
                        if (TryGetKey(ref retrieverInput, out id) == false)
                            throw new InvalidOperationException($"Could not extract '{Constants.Documents.Indexing.Fields.DocumentIdFieldName}' from index.");
                        break;
                }

                if (FieldsToFetch.IsProjection)
                    return GetProjection(ref retrieverInput, id, token);

                using (_storageScope = _storageScope?.Start() ?? RetrieverScope?.For(nameof(QueryTimingsScope.Names.Storage)))
                {
                    var doc = DirectGet(ref retrieverInput, id, DocumentFields);

                    FinishDocumentSetup(doc, retrieverInput.Score);
                    return (doc, null);
                }
            }
        }

        public override bool TryGetKey(ref RetrieverInput retrieverInput, out string key)
        {
            //Lucene method
            key = retrieverInput.LuceneDocument.Get(Constants.Documents.Indexing.Fields.DocumentIdFieldName, retrieverInput.State);
            return string.IsNullOrEmpty(key) == false;
        }

        public override Document DirectGet(ref RetrieverInput retrieverInput, string id, DocumentFields fields)
        {
            return DocumentsStorage.Get(_context, id, fields);
        }

        protected override Document LoadDocument(string id)
        {
            return DocumentsStorage.Get(_context, id);
        }

        protected override long? GetCounter(string docId, string name)
        {
            var value = DocumentsStorage.CountersStorage.GetCounterValue(_context, docId, name);
            return value?.Value;
        }

        protected override DynamicJsonValue GetCounterRaw(string docId, string name)
        {
            var djv = new DynamicJsonValue();

            foreach (var partialValue in DocumentsStorage.CountersStorage.GetCounterPartialValues(_context, docId, name))
            {
                djv[partialValue.ChangeVector] = partialValue.PartialValue;
            }

            return djv;
        }
    }
}
