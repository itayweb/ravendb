﻿using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Documents.Indexes;
using Raven.Server.Documents.Handlers.Processors.Indexes;
using Raven.Server.ServerWide.Context;
using Raven.Server.Web.Http;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.Indexes
{
    internal class ShardedIndexHandlerProcessorForGetIndexesStatus : AbstractIndexHandlerProcessorForGetIndexesStatus<ShardedDatabaseRequestHandler, TransactionOperationContext>
    {
        public ShardedIndexHandlerProcessorForGetIndexesStatus([NotNull] ShardedDatabaseRequestHandler requestHandler) : base(requestHandler, requestHandler.ContextPool)
        {
        }
        protected override bool SupportsCurrentNode => false;

        protected override ValueTask HandleCurrentNodeAsync() => throw new NotSupportedException();

        protected override Task HandleRemoteNodeAsync(ProxyCommand<IndexingStatus> command)
        {
            var shardNumber = GetShardNumber();

            return RequestHandler.ShardExecutor.ExecuteSingleShardAsync(command, shardNumber);
        }
    }
}