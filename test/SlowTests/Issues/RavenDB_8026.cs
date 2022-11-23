﻿using System.Linq;
using Raven.Client.Documents;
using Raven.Client.Documents.Queries.Facets;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RavenDB_8026 : FacetTestBase
    {
        public RavenDB_8026(ITestOutputHelper output) : base(output)
        {
        }

        [RavenTheory(RavenTestCategory.Facets)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void OptionsShouldWork(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                CreateCameraCostIndex(store);

                InsertCameraData(store, GetCameras(100));

                using (var session = store.OpenSession())
                {
                    var result = session.Query<Camera, CameraCostIndex>()
                        .AggregateBy(x => x.ByField(y => y.Manufacturer).WithOptions(new FacetOptions
                        {
                            TermSortMode = FacetTermSortMode.CountDesc
                        }))
                        .Execute();

                    var counts = result["Manufacturer"].Values.Select(x => x.Count).ToList();

                    var orderedCounts = result["Manufacturer"].Values
                        .Select(x => x.Count)
                        .OrderByDescending(x => x)
                        .ToList();

                    Assert.Equal(counts, orderedCounts);
                }
            }
        }
    }
}
