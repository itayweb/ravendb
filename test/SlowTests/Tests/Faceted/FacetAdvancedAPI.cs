﻿using System;
using System.Collections.Generic;
using System.Linq;
using FastTests;
using Raven.Client.Documents.Queries.Facets;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Tests.Faceted
{
    public class FacetAdvancedAPI : NoDisposalNeeded
    {
        public FacetAdvancedAPI(ITestOutputHelper output) : base(output)
        {
        }

        private class Test
        {
            public String Id { get; set; }
            public String Manufacturer { get; set; }
            public DateTime Date { get; set; }
            public Decimal Cost { get; set; }
            public int Quantity { get; set; }
            public Double Price { get; set; }
        }

        [RavenFact(RavenTestCategory.Facets)]
        public void CanUseNewAPIToDoMultipleQueries()
        {
            var oldFacets = new List<FacetBase>
            {
                new Facet {FieldName = "Manufacturer"},
                new RangeFacet
                {
                    Ranges =
                    {
                        "Cost < 200",
                        "Cost > 200 and Cost < 400",
                        "Cost > 400 and Cost < 600",
                        "Cost > 600 and Cost < 800",
                        "Cost > 800",
                    }
                },
                new RangeFacet
                {
                    Ranges =
                    {
                        "Price < 9.99",
                        "Price > 9.99 and Price < 49.99",
                        "Price > 49.99 and Price < 99.99",
                        "Price > 99.99",
                    }
                }
            };

            var newFacets = new List<FacetBase>
            {
                new Facet<Test> {FieldName = x => x.Manufacturer},
                new RangeFacet<Test>
                {
                    Ranges =
                        {
                            x => x.Cost < 200m,
                            x => x.Cost > 200m && x.Cost < 400m,
                            x => x.Cost > 400m && x.Cost < 600m,
                            x => x.Cost > 600m && x.Cost < 800m,
                            x => x.Cost > 800m
                        }
                },
                new RangeFacet<Test>
                {
                    Ranges =
                    {
                        x => x.Price < 9.99,
                        x => x.Price > 9.99 && x.Price < 49.99,
                        x => x.Price > 49.99 && x.Price < 99.99,
                        x => x.Price > 99.99
                    }
                }
            };

            Assert.Equal(true, AreFacetsEqual<Test>(oldFacets[0], newFacets[0]));
            Assert.Equal(true, AreFacetsEqual<Test>(oldFacets[1], newFacets[1]));
            Assert.Equal(true, AreFacetsEqual<Test>(oldFacets[2], newFacets[2]));
        }

        [RavenFact(RavenTestCategory.Facets)]
        public void NewAPIThrowsExceptionsForInvalidExpressions()
        {
            //Create an invalid lambda and check it throws an exception!!
            Assert.Throws<InvalidOperationException>(() =>
                TriggerConversion(new RangeFacet<Test>
                {
                    //Ranges can be a single item or && only
                    Ranges = { x => x.Cost > 200m || x.Cost < 400m }
                }));

            Assert.Throws<InvalidOperationException>(() =>
                TriggerConversion(new RangeFacet<Test>
                {
                    //Ranges can be > or < only
                    Ranges = { x => x.Cost == 200m }
                }));

            Assert.Throws<InvalidOperationException>(() =>
                TriggerConversion(new RangeFacet<Test>
                {
                    //Ranges must be on the same field!!!
                    Ranges = { x => x.Price > 9.99 && x.Cost < 49.99m }
                }));
        }

        [RavenFact(RavenTestCategory.Facets)]
        public void AdvancedAPIAdvancedEdgeCases()
        {
            var now = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
            var testDateTime = new DateTime(2001, 12, 5);
            var edgeCaseFacet = new RangeFacet<Test>
            {
                Ranges =
                {
                    x => x.Date < now,
                    x => x.Date < new DateTime(2010, 12, 5) && x.Date > testDateTime
                }
            };

            var facet = TriggerConversion(edgeCaseFacet);
            Assert.Equal(2, facet.Ranges.Count);
            Assert.False(string.IsNullOrWhiteSpace(facet.Ranges[0]));
            Assert.Equal(@"Date > '2001-12-05T00:00:00.0000000' and Date < '2010-12-05T00:00:00.0000000'", facet.Ranges[1]);

        }

        private bool AreFacetsEqual<T>(FacetBase left, FacetBase right)
        {
            if (left is Facet leftFacet)
            {
                var rightFacet = (Facet)(Facet<T>)right;

                return leftFacet.FieldName == rightFacet.FieldName;
            }

            var leftRangeFacet = (RangeFacet)left;
            var rightRangeFacet = (RangeFacet)(RangeFacet<T>)right;

            return leftRangeFacet.Ranges.Count == rightRangeFacet.Ranges.Count &&
                leftRangeFacet.Ranges.All(x => rightRangeFacet.Ranges.Contains(x));
        }

        private RangeFacet TriggerConversion(RangeFacet<Test> facet)
        {
            //The conversion is done with an implicit cast, 
            //so we remain compatible with the original facet API
            return (RangeFacet)facet;
        }
    }
}
