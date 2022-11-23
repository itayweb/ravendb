﻿using System;
using FastTests;
using Raven.Client.Documents.Indexes.Spatial;
using Raven.Client.Documents.Queries;
using Raven.Server.Documents.Indexes.Spatial;
using Raven.Server.Documents.Queries;
using Sparrow.Json;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Tests.Spatial
{
    public class DynamicSpatialQueries : RavenTestBase
    {
        public DynamicSpatialQueries(ITestOutputHelper output) : base(output)
        {
        }

        [RavenTheory(RavenTestCategory.Spatial)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void VerifySingleSpatialPropertyInResults(Options options)
        {
            var house1 = new GeoDoc(44.75, -93.35);
            var house2 = new GeoDoc(44.751, -93.351);
            var house3 = new GeoDoc(44.752, -93.352);
            var house4 = new GeoDoc(45.75, -94.35);

            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                using (var session = store.OpenSession())
                {
                    session.Store(house1);
                    session.Store(house2);
                    session.Store(house3);
                    session.Store(house4);
                    session.SaveChanges();
                }

                using (var commands = store.Commands())
                {
                    var json = commands.RawGetJson<BlittableJsonReaderObject>("/queries?query=from GeoDocs where spatial.within(spatial.point(Location1.Latitude, Location1.Longitude), spatial.circle(50, 44.75, -93.35))" +
                                                                              "&addSpatialProperties=true");

                    Assert.True(json.TryGet(nameof(QueryResult.TotalResults), out int results));
                    Assert.Equal(3, results);

                    Assert.True(json.TryGet(nameof(DocumentQueryResult.SpatialProperties), out BlittableJsonReaderArray spatialProperties));
                    Assert.Equal(1, spatialProperties.Length);

                    (spatialProperties[0] as BlittableJsonReaderObject).TryGet(nameof(SpatialProperty.LatitudeProperty), out string lat);
                    Assert.Equal(lat, "Location1.Latitude");
                    (spatialProperties[0] as BlittableJsonReaderObject).TryGet(nameof(SpatialProperty.LongitudeProperty), out string lng);
                    Assert.Equal(lng, "Location1.Longitude");
                }
            }
        }

        [RavenTheory(RavenTestCategory.Spatial)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void VerifyMultipleSpatialPropertiesInResults(Options options)
        {
            var house1 = new GeoDoc(44.75, -93.35);
            var house2 = new GeoDoc(44.751, -93.351);
            var house3 = new GeoDoc(44.752, -93.352);
            var house4 = new GeoDoc(45.75, -94.35);

            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                using (var session = store.OpenSession())
                {
                    session.Store(house1);
                    session.Store(house2);
                    session.Store(house3);
                    session.Store(house4);
                    session.SaveChanges();
                }

                var boundingPolygon =
                    "POLYGON((12.556675672531128 55.675285554217,12.56213665008545 55.675285554217,12.56213665008545 55.67261750095371,12.556675672531128 55.67261750095371,12.556675672531128 55.675285554217))";

                using (var commands = store.Commands())
                {
                    var json = commands.RawGetJson<BlittableJsonReaderObject>("/queries?query=from GeoDocs " +
                                                                              "where spatial.within(spatial.point(Location1.Latitude, Location1.Longitude), spatial.circle(50, 44.75, -93.35)) " +
                                                                              "or spatial.within(spatial.point(Location2.Latitude, Location2.Longitude), spatial.circle(50, 45.75, -94.35)) " +
                                                                              $"or spatial.within(spatial.point(Location3.Latitude, Location3.Longitude), spatial.wkt('{boundingPolygon}'))" +
                                                                              "&addSpatialProperties=true");

                    Assert.True(json.TryGet(nameof(QueryResult.TotalResults), out int results));
                    Assert.Equal(4, results);

                    Assert.True(json.TryGet(nameof(DocumentQueryResult.SpatialProperties), out BlittableJsonReaderArray spatialProperties));
                    Assert.Equal(3, spatialProperties.Length);

                    for (var i = 0; i < spatialProperties.Length; i++)
                    {
                        Assert.True((spatialProperties[i] as BlittableJsonReaderObject).TryGet(nameof(SpatialProperty.LatitudeProperty), out string lat));
                        Assert.Equal(lat, $"Location{i + 1}.Latitude");
                        Assert.True((spatialProperties[i] as BlittableJsonReaderObject).TryGet(nameof(SpatialProperty.LongitudeProperty), out string lng));
                        Assert.Equal(lng, $"Location{i + 1}.Longitude");
                    }
                }
            }
        }

        [RavenTheory(RavenTestCategory.Spatial)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void VerifySelectedSpatialPropertiesWithAliasInResults(Options options)
        {
            var house1 = new GeoDoc(44.75, -93.35);
            var house2 = new GeoDoc(44.751, -93.351);
            var house3 = new GeoDoc(44.752, -93.352);
            var house4 = new GeoDoc(45.75, -94.35);

            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                using (var session = store.OpenSession())
                {
                    session.Store(house1);
                    session.Store(house2);
                    session.Store(house3);
                    session.Store(house4);
                    session.SaveChanges();
                }

                using (var commands = store.Commands())
                {
                    var json = commands.RawGetJson<BlittableJsonReaderObject>("/queries?query=from GeoDocs " +
                                                                              "where spatial.within(spatial.point(Location1.Latitude, Location1.Longitude), spatial.circle(50, 44.75, -93.35)) " +
                                                                              "select Location1 as SomeAlias" +
                                                                              "&addSpatialProperties=true");

                    Assert.True(json.TryGet(nameof(QueryResult.TotalResults), out int results));
                    Assert.Equal(3, results);

                    Assert.True(json.TryGet(nameof(DocumentQueryResult.SpatialProperties), out BlittableJsonReaderArray spatialProperties));
                    Assert.Equal(1, spatialProperties.Length);

                    (spatialProperties[0] as BlittableJsonReaderObject).TryGet(nameof(SpatialProperty.LatitudeProperty), out string lat);
                    Assert.Equal(lat, "SomeAlias.Latitude");
                    (spatialProperties[0] as BlittableJsonReaderObject).TryGet(nameof(SpatialProperty.LongitudeProperty), out string lng);
                    Assert.Equal(lng, "SomeAlias.Longitude");
                }
            }
        }

        [RavenTheory(RavenTestCategory.Spatial)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void VerifySelectedSpatialPropertiesWithoutAliasInResults(Options options)
        {
            var house1 = new GeoDoc(44.75, -93.35);
            var house2 = new GeoDoc(44.751, -93.351);
            var house3 = new GeoDoc(44.752, -93.352);
            var house4 = new GeoDoc(45.75, -94.35);

            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                using (var session = store.OpenSession())
                {
                    session.Store(house1);
                    session.Store(house2);
                    session.Store(house3);
                    session.Store(house4);
                    session.SaveChanges();
                }

                using (var commands = store.Commands())
                {
                    var json = commands.RawGetJson<BlittableJsonReaderObject>("/queries?query=from GeoDocs " +
                                                                              "where spatial.within(spatial.point(Location1.Latitude, Location1.Longitude), spatial.circle(50, 44.75, -93.35)) " +
                                                                              "select Location1" +
                                                                              "&addSpatialProperties=true");

                    Assert.True(json.TryGet(nameof(QueryResult.TotalResults), out int results));
                    Assert.Equal(3, results);

                    Assert.True(json.TryGet(nameof(DocumentQueryResult.SpatialProperties), out BlittableJsonReaderArray spatialProperties));
                    Assert.Equal(1, spatialProperties.Length);

                    (spatialProperties[0] as BlittableJsonReaderObject).TryGet(nameof(SpatialProperty.LatitudeProperty), out string lat);
                    Assert.Equal(lat, "Location1.Latitude");
                    (spatialProperties[0] as BlittableJsonReaderObject).TryGet(nameof(SpatialProperty.LongitudeProperty), out string lng);
                    Assert.Equal(lng, "Location1.Longitude");
                }
            }
        }

        [RavenTheory(RavenTestCategory.Spatial)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void VerifyNoSpatialPropertiesInResultsWhenSelectingOnlyLatitudeProperty(Options options)
        {
            var house1 = new GeoDoc(44.75, -93.35);
            var house2 = new GeoDoc(44.751, -93.351);
            var house3 = new GeoDoc(44.752, -93.352);
            var house4 = new GeoDoc(45.75, -94.35);

            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                using (var session = store.OpenSession())
                {
                    session.Store(house1);
                    session.Store(house2);
                    session.Store(house3);
                    session.Store(house4);
                    session.SaveChanges();
                }

                using (var commands = store.Commands())
                {
                    var json = commands.RawGetJson<BlittableJsonReaderObject>("/queries?query=from GeoDocs " +
                                                                              "where spatial.within(spatial.point(Location1.Latitude, Location1.Longitude), spatial.circle(50, 44.75, -93.35)) " +
                                                                              "select Location1.latitude" +
                                                                              "&addSpatialProperties=true");

                    Assert.True(json.TryGet(nameof(QueryResult.TotalResults), out int results));
                    Assert.Equal(3, results);

                    Assert.False(json.TryGet(nameof(DocumentQueryResult.SpatialProperties), out BlittableJsonReaderArray spatialProperties));
                }
            }
        }

        [RavenTheory(RavenTestCategory.Spatial)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void VerifyNoSpatialPropertiesInResultsWhenSelectingNonSpatialField(Options options)
        {
            var house1 = new GeoDoc(44.75, -93.35);
            var house2 = new GeoDoc(44.751, -93.351);
            var house3 = new GeoDoc(44.752, -93.352);
            var house4 = new GeoDoc(45.75, -94.35);

            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                using (var session = store.OpenSession())
                {
                    session.Store(house1);
                    session.Store(house2);
                    session.Store(house3);
                    session.Store(house4);
                    session.SaveChanges();
                }

                using (var commands = store.Commands())
                {
                    var json = commands.RawGetJson<BlittableJsonReaderObject>("/queries?query=from GeoDocs " +
                                                                              "where spatial.within(spatial.point(Location1.Latitude, Location1.Longitude), spatial.circle(50, 44.75, -93.35)) " +
                                                                              "select Id" +
                                                                              "&addSpatialProperties=true");

                    Assert.True(json.TryGet(nameof(QueryResult.TotalResults), out int results));
                    Assert.Equal(3, results);

                    Assert.False(json.TryGet(nameof(DocumentQueryResult.SpatialProperties), out BlittableJsonReaderArray spatialProperties));
                }
            }
        }

        [RavenTheory(RavenTestCategory.Spatial)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void VerifyPolygonInResults(Options options)
        {
            var house1 = new GeoDoc(44.75, -93.35);
            var house2 = new GeoDoc(44.751, -93.351);
            var house3 = new GeoDoc(44.752, -93.352);
            var house4 = new GeoDoc(45.75, -94.35);

            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                using (var session = store.OpenSession())
                {
                    session.Store(house1);
                    session.Store(house2);
                    session.Store(house3);
                    session.Store(house4);
                    session.SaveChanges();
                }

                using (var commands = store.Commands())
                {
                    var json = commands.RawGetJson<BlittableJsonReaderObject>("/queries?query=from GeoDocs " +
                                                                              "where spatial.within(spatial.point(Location1.Latitude, Location1.Longitude), " +
                                                                              "spatial.wkt('POLYGON ((-90.0 50.0, -95.0 50.0, -95.0 40.0, -90.0 40.0, -90.0 50.0))'))" +
                                                                              "&addSpatialProperties=true");

                    Assert.True(json.TryGet(nameof(QueryResult.TotalResults), out int results));
                    Assert.Equal(4, results);

                    Assert.True(json.TryGet(nameof(DocumentQueryResult.SpatialShapes), out BlittableJsonReaderArray spatialShapes));
                    Assert.Equal(1, spatialShapes.Length);

                    (spatialShapes[0] as BlittableJsonReaderObject).TryGet(nameof(SpatialShapeBase.Type), out string shape);
                    Assert.Equal(shape, "Polygon");

                    Assert.True((spatialShapes[0] as BlittableJsonReaderObject).TryGet(nameof(Polygon.Vertices), out BlittableJsonReaderArray vertices));
                    Assert.Equal(4, vertices.Length);

                    (vertices[0] as BlittableJsonReaderObject).TryGet(nameof(Coordinates.Latitude), out double latitude);
                    Assert.Equal(50, latitude);
                    (vertices[0] as BlittableJsonReaderObject).TryGet(nameof(Coordinates.Longitude), out double longitude);
                    Assert.Equal(-90, longitude);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Spatial)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void VerifyCirclesInResults(Options options)
        {
            var house1 = new GeoDoc(44.75, -93.35);
            var house2 = new GeoDoc(44.751, -93.351);
            var house3 = new GeoDoc(44.752, -93.352);
            var house4 = new GeoDoc(45.75, -94.35);

            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                using (var session = store.OpenSession())
                {
                    session.Store(house1);
                    session.Store(house2);
                    session.Store(house3);
                    session.Store(house4);
                    session.SaveChanges();
                }

                using (var commands = store.Commands())
                {
                    var json = commands.RawGetJson<BlittableJsonReaderObject>("/queries?query=from GeoDocs " +
                                                                              "where spatial.within(spatial.point(Location1.Latitude, Location1.Longitude), " +
                                                                              "spatial.circle(50, 44.75, -93.35, 'miles')) " +
                                                                              "or spatial.within(spatial.point(Location2.Latitude, Location2.Longitude), " +
                                                                              "spatial.wkt('CIRCLE(-90 40 d=20)'))" +
                                                                              "&addSpatialProperties=true");

                    Assert.True(json.TryGet(nameof(QueryResult.TotalResults), out int results));
                    Assert.Equal(3, results);

                    Assert.True(json.TryGet(nameof(DocumentQueryResult.SpatialShapes), out BlittableJsonReaderArray spatialShapes));
                    Assert.Equal(2, spatialShapes.Length);

                    var firstShape = spatialShapes[0] as BlittableJsonReaderObject;
                    firstShape.TryGet(nameof(SpatialShapeBase.Type), out string shape);
                    Assert.Equal(shape, "Circle");

                    Assert.True(firstShape.TryGet(nameof(Circle.Center), out BlittableJsonReaderObject center));
                    center.TryGet(nameof(Coordinates.Latitude), out double latitude);
                    Assert.Equal(44.75, latitude);
                    center.TryGet(nameof(Coordinates.Longitude), out double longitude);
                    Assert.Equal(-93.35, longitude);

                    firstShape.TryGet(nameof(Circle.Radius), out double radius);
                    Assert.True(Math.Abs(50 - radius) < 0.1);

                    firstShape.TryGet(nameof(Circle.Units), out SpatialUnits units);
                    Assert.Equal(SpatialUnits.Miles, units);

                    var secondShape = spatialShapes[1] as BlittableJsonReaderObject;
                    secondShape.TryGet(nameof(SpatialShapeBase.Type), out shape);
                    Assert.Equal(shape, "Circle");

                    Assert.True(secondShape.TryGet(nameof(Circle.Center), out center));
                    center.TryGet(nameof(Coordinates.Latitude), out latitude);
                    Assert.Equal(40, latitude);
                    center.TryGet(nameof(Coordinates.Longitude), out longitude);
                    Assert.Equal(-90, longitude);

                    secondShape.TryGet(nameof(Circle.Radius), out radius);
                    Assert.True(Math.Abs(20 - radius) < 0.1);

                    secondShape.TryGet(nameof(Circle.Units), out units);
                    Assert.Equal(SpatialUnits.Kilometers, units);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Spatial)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void VerifyWKTCircleHasDistance(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                using (var commands = store.Commands())
                {
                    var e = Assert.Throws<Raven.Client.Exceptions.InvalidQueryException>(() => commands.RawGetJson<BlittableJsonReaderObject>("/queries?query=from GeoDocs " +
                                                                                                 "where spatial.within(spatial.point(Location1.Latitude, Location1.Longitude), " +
                                                                                                 "spatial.wkt('CIRCLE(-90 40)'))" +
                                                                                                 "&addSpatialProperties=true"));

                    Assert.Contains("Value 'CIRCLE(-90 40)' is not a valid WKT value", e.Message);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Spatial)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void VerifyWKTCircleDistanceFormat(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.Initialize();

                using (var commands = store.Commands())
                {
                    var e = Assert.Throws<Raven.Client.Exceptions.InvalidQueryException>(() => commands.RawGetJson<BlittableJsonReaderObject>("/queries?query=from GeoDocs " +
                                                                                                                         "where spatial.within(spatial.point(Location1.Latitude, Location1.Longitude), " +
                                                                                                                         "spatial.wkt('CIRCLE(-90 40 d2)'))" +
                                                                                                                         "&addSpatialProperties=true"));

                    Assert.Contains("Value 'CIRCLE(-90 40 d2)' is not a valid WKT value", e.Message);
                }
            }
        }

        private class GeoPoint
        {
            public double Latitude { get; set; }
            public double Longitude { get; set; }

            public GeoPoint(double lat, double lng)
            {
                Latitude = lat;
                Longitude = lng;
            }
        }

        private class GeoDoc
        {
            public string Id { get; set; }
            public GeoPoint Location1 { get; set; }
            public GeoPoint Location2 { get; set; }
            public GeoPoint Location3 { get; set; }

            public GeoDoc(double lat, double lng)
            {
                Location1 = new GeoPoint(lat, lng);
                Location2 = new GeoPoint(lat + 0.0001, lng + 0.0001);
                Location3 = new GeoPoint(lat + 0.0002, lng + 0.0002);
            }
        }
    }
}
