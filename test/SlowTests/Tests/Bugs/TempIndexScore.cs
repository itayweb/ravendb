﻿using System.Linq;
using FastTests;
using Raven.Client;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Tests.Bugs
{
    public class TempIndexScore : RavenTestBase
    {
        public TempIndexScore(ITestOutputHelper output) : base(output)
        {
        }

        private class Blog
        {
            public string Title { get; set; }
            public string Category { get; set; }
            public BlogTag[] Tags { get; set; }
        }

        private class BlogTag
        {
            public string Name { get; set; }
        }

        private IDocumentStore SetupSampleData(Options options)
        {
            var blogOne = new Blog
            {
                Title = "one",
                Category = "Ravens",
                Tags = new[]
                {
                    new BlogTag {Name = "Birds"}
                }
            };
            var blogTwo = new Blog
            {
                Title = "two",
                Category = "Rhinos",
                Tags = new[]
                {
                    new BlogTag {Name = "Mammals"}
                }
            };
            var blogThree = new Blog
            {
                Title = "three",
                Category = "Rhinos",
                Tags = new[]
                {
                    new BlogTag {Name = "Mammals"}
                }
            };

            var store = GetDocumentStore(options);
            using (var s = store.OpenSession())
            {
                s.Store(blogOne);
                s.Store(blogTwo);
                s.Store(blogThree);
                s.SaveChanges();
            }

            return store;
        }

        [RavenTheory(RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void ScoreShouldBeAValidDoubleValue(Options options)
        {
            using (var store = SetupSampleData(options))
            {
                QueryStatistics stats;
                using (var session = store.OpenSession())
                {
                    var result = session.Query<Blog>()
                            .Statistics(out stats)
                            .Customize(x => x.WaitForNonStaleResults())
                            .FirstOrDefault(x => x.Title == "one"); // NonSortingCollector

                    var metadata = session.Advanced.GetMetadataFor(result);
                    var score = metadata.GetDouble(Constants.Documents.Metadata.IndexScore);
                    Assert.True(score > 0d);
                }

                using (var session = store.OpenSession())
                {
                    var results = session.Query<Blog>(stats.IndexName)
                        .Customize(x => x.WaitForNonStaleResults())
                        .Take(int.MaxValue)
                        .ToList(); // GathetrAllCollector

                    var result = results[0];

                    var metadata = session.Advanced.GetMetadataFor(result);
                    var score = metadata.GetDouble(Constants.Documents.Metadata.IndexScore);
                    Assert.True(score > 0d);
                }

                using (var session = store.OpenSession())
                {
                    var result = session.Query<Blog>()
                        .Customize(x => x.WaitForNonStaleResults())
                        .OrderBy(x => x.Category)
                        .FirstOrDefault(x => x.Title == "one");

                    var metadata = session.Advanced.GetMetadataFor(result);
                    var score = metadata.GetDouble(Constants.Documents.Metadata.IndexScore);
                    Assert.True(score > 0d);
                }
            }
        }
    }
}
