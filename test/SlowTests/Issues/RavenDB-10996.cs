﻿using System.IO;
using System.Text;
using FastTests;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Operations.Indexes;
using Sparrow.Server.Json.Sync;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RavenDB_10996 : RavenTestBase
    {
        public RavenDB_10996(ITestOutputHelper output) : base(output)
        {
        }

        private const string Doc = @"{
  ""Name"": ""0e5d5967-28e6-467d-a3f7-85488cea8a83"",
  ""Properties"": [
    {
      ""Key"": ""a1"",
      ""Value"": ""b1""
    },
    {
      ""Key"": ""a2"",
      ""Value"": ""b2""
    },
    {
      ""Key"": ""a3"",
      ""Value"": ""b3""
    },
    {
      ""Key"": ""a4"",
      ""Value"": ""b4""
    }
  ],
  ""@metadata"": {
    ""@collection"": ""Documents""
  }
}";

        [Theory]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void CanGetCorrectOutputWhenUsingDuplicateArray(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.Maintenance.Send(new PutIndexesOperation(new Raven.Client.Documents.Indexes.IndexDefinition
                {
                    Maps =
                    {
                        @"docs.Documents.Select(doc => new {
    Name = doc.Name,
    Properties = doc.Properties,
    col = new[]{
        new {
            Name = doc.Name,
            Properties = doc.Properties
        }
    }})
"
                    },
                    Reduce = @"results.GroupBy(doc=> new {
        Name = doc.Name
    }).Select(g=> new {
        Name = g.Key.Name,
        Properties = g.SelectMany(d => d.Properties),
        col = g.SelectMany(d => d.col)
    })",
                    Name = "test"
                }));

                var requestExecuter = store.GetRequestExecutor();

                using (requestExecuter.ContextPool.AllocateOperationContext(out var context))
                {
                    var json = context.Sync.ReadForDisk(new MemoryStream(Encoding.UTF8.GetBytes(Doc)), "users/1");

                    var putCommand = new PutDocumentCommand("users/1", null, json);

                    requestExecuter.Execute(putCommand, context);
                }

                using (var session = store.OpenSession())
                {
                    var r = session.Advanced.RawQuery<dynamic>(@"from index test")
                        .WaitForNonStaleResults()
                        .Single();

                    Assert.Equal("a1", r.col[0].Properties[0].Key.ToString());
                }
            }
        }
    }
}
