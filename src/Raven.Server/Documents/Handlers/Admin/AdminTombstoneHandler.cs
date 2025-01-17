﻿using System.Threading.Tasks;
using Raven.Server.Routing;
using Raven.Server.Utils;
using Sparrow.Json;

namespace Raven.Server.Documents.Handlers.Admin
{
    public class AdminTombstoneHandler : DatabaseRequestHandler
    {
        [RavenAction("/databases/*/admin/tombstones/cleanup", "POST", AuthorizationStatus.DatabaseAdmin)]
        public async Task Cleanup()
        {
            var count = await Database.TombstoneCleaner.ExecuteCleanup();

            using (Database.DocumentsStorage.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
                {
                    writer.WriteStartObject();

                    writer.WritePropertyName("Value");
                    writer.WriteInteger(count);

                    writer.WriteEndObject();
                }
            }
        }

        [RavenAction("/databases/*/admin/tombstones/state", "GET", AuthorizationStatus.DatabaseAdmin, IsDebugInformationEndpoint = true)]
        public async Task State()
        {
            var state = Database.TombstoneCleaner.GetState(addInfoForDebug: true);

            using (Database.DocumentsStorage.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                await using (var writer = new AsyncBlittableJsonTextWriterForDebug(context, ServerStore, ResponseBodyStream()))
                {
                    writer.WriteStartObject();

                    writer.WritePropertyName(nameof(TombstoneCleaner.TombstonesState.MinAllDocsEtag));
                    writer.WriteInteger(state.MinAllDocsEtag);
                    writer.WriteComma();

                    writer.WritePropertyName(nameof(TombstoneCleaner.TombstonesState.MinAllTimeSeriesEtag));
                    writer.WriteInteger(state.MinAllTimeSeriesEtag);
                    writer.WriteComma();

                    writer.WritePropertyName(nameof(TombstoneCleaner.TombstonesState.MinAllCountersEtag));
                    writer.WriteInteger(state.MinAllCountersEtag);
                    writer.WriteComma();

                    writer.WriteArray(context, "Results", state.Tombstones, (w, c, v) =>
                    {
                        w.WriteStartObject();

                        w.WritePropertyName("Collection");
                        w.WriteString(v.Key);
                        w.WriteComma();

                        w.WritePropertyName(nameof(v.Value.Documents));
                        w.WriteStartObject();
                        w.WritePropertyName(nameof(v.Value.Documents.Component));
                        w.WriteString(v.Value.Documents.Component);
                        w.WriteComma();
                        w.WritePropertyName(nameof(v.Value.Documents.Etag));
                        w.WriteInteger(v.Value.Documents.Etag);
                        w.WriteEndObject();
                        w.WriteComma();

                        w.WritePropertyName(nameof(v.Value.TimeSeries));
                        w.WriteStartObject();
                        w.WritePropertyName(nameof(v.Value.TimeSeries.Component));
                        w.WriteString(v.Value.TimeSeries.Component);
                        w.WriteComma();
                        w.WritePropertyName(nameof(v.Value.TimeSeries.Etag));
                        w.WriteInteger(v.Value.TimeSeries.Etag);
                        w.WriteEndObject();

                        w.WriteEndObject();
                    });

                    writer.WriteComma();

                    writer.WritePropertyName(nameof(TombstoneCleaner.TombstonesState.PerSubscriptionInfo));
                    writer.WriteStartArray();
                    if (state.PerSubscriptionInfo != null)
                    {
                        var first = true;

                        foreach (var info in state.PerSubscriptionInfo)
                        {
                            if (first == false)
                                writer.WriteComma();

                            first = false;

                            writer.WriteStartObject();

                            writer.WritePropertyName(nameof(TombstoneCleaner.TombstonesState.SubscriptionInfo.Identifier));
                            writer.WriteString(info.Identifier);
                            writer.WriteComma();
                            writer.WritePropertyName(nameof(TombstoneCleaner.TombstonesState.SubscriptionInfo.Type));
                            writer.WriteString(info.Type.ToString());
                            writer.WriteComma();
                            writer.WritePropertyName(nameof(TombstoneCleaner.TombstonesState.SubscriptionInfo.Collection));
                            writer.WriteString(info.Collection);
                            writer.WriteComma();
                            writer.WritePropertyName(nameof(TombstoneCleaner.TombstonesState.SubscriptionInfo.Etag));
                            writer.WriteInteger(info.Etag);

                            writer.WriteEndObject();
                        }
                    }

                    writer.WriteEndArray();

                    writer.WriteEndObject();
                }
            }
        }
    }
}
