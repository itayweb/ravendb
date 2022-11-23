﻿using FastTests;
using Orders;
using Raven.Client.Documents.Queries.Facets;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RavenDB_10746 : RavenTestBase
    {
        public RavenDB_10746(ITestOutputHelper output) : base(output)
        {
        }

        [RavenFact(RavenTestCategory.Facets)]
        public void ShouldBeAbleToParseRangeFacet()
        {
            RangeFacet facet = new RangeFacet<Product>
            {
                Ranges =
                {
                    product => product.UnitsInStock < 3.0,
                    product => product.UnitsInStock >= 3.0 && product.UnitsInStock < 7.0,
                    product => product.UnitsInStock >= 7.0 && product.UnitsInStock < 10.0,
                    product => product.UnitsInStock >= 10.0
                }
            };

            Assert.Equal("UnitsInStock < 3", facet.Ranges[0]);
            Assert.Equal("UnitsInStock >= 3 and UnitsInStock < 7", facet.Ranges[1]);
            Assert.Equal("UnitsInStock >= 7 and UnitsInStock < 10", facet.Ranges[2]);
            Assert.Equal("UnitsInStock >= 10", facet.Ranges[3]);
        }
    }
}
