﻿using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Raven.Client.Http;
using Raven.Server.Documents.Sharding.Handlers;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.Sharding.Operations
{
    public readonly struct SingleNodeShardedBatchOperation : IShardedOperation<BlittableJsonReaderObject, DynamicJsonArray>
    {
        private readonly HttpContext _httpContext;
        private readonly JsonOperationContext _resultContext;
        private readonly Dictionary<int, SingleNodeShardedBatchCommand> _commands;
        private readonly int _totalCommands;

        public SingleNodeShardedBatchOperation(HttpContext httpContext, JsonOperationContext resultContext,
            Dictionary<int, SingleNodeShardedBatchCommand> commands, int totalCommands)
        {
            _httpContext = httpContext;
            _resultContext = resultContext;
            _commands = commands;
            _totalCommands = totalCommands;
        }

        public HttpRequest HttpRequest => _httpContext.Request;

        public DynamicJsonArray Combine(Memory<BlittableJsonReaderObject> results)
        {
            var reply = new object[_totalCommands];
            foreach (var c in _commands.Values)
                c.AssembleShardedReply(_resultContext, reply);

            return new DynamicJsonArray(reply);
        }

        public RavenCommand<BlittableJsonReaderObject> CreateCommandForShard(int shard) => _commands[shard];
    }
}