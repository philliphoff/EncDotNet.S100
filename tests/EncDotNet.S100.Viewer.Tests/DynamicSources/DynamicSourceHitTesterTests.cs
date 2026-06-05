using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using Mapsui;
using Mapsui.Projections;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

public class DynamicSourceHitTesterTests
{
    private const double SeattleLat = 47.6;
    private const double SeattleLon = -122.3;

    private static DynamicFeature PointFeature(string id, double lat, double lon) => new()
    {
        Id = id,
        GeometryType = GeometryType.Point,
        Coordinates = new[] { (lat, lon) },
        LastUpdated = DateTimeOffset.UtcNow,
    };

    private static MPoint ToMercator(double lat, double lon)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        return new MPoint(x, y);
    }

    [Fact]
    public void HitTest_ReturnsFeature_WhenClickInsideTolerance()
    {
        // Resolution ~ 9.55 m/px at zoom 14. 12px tolerance → ~115m at this zoom.
        const double resolution = 9.55;
        var click = ToMercator(SeattleLat, SeattleLon);
        var source = new FakeDynamicFeatureSource("test", new DynamicSourceMetadata { DisplayName = "Test" });
        source.SetFeatures(new[] { PointFeature("f1", SeattleLat, SeattleLon) });

        var hits = DynamicSourceHitTester.HitTest(click, resolution, new[] { source });

        var hit = Assert.Single(hits);
        Assert.Equal("f1", hit.Feature.Id);
        Assert.Equal(0.0, hit.DistanceMapUnits, precision: 1);
    }

    [Fact]
    public void HitTest_ReturnsEmpty_WhenClickOutsideTolerance()
    {
        const double resolution = 1.0; // 12-metre tolerance
        var click = ToMercator(SeattleLat, SeattleLon);
        var source = new FakeDynamicFeatureSource("test", new DynamicSourceMetadata { DisplayName = "Test" });
        // 0.001° lat ≈ 111 m — outside 12-m tolerance.
        source.SetFeatures(new[] { PointFeature("far", SeattleLat + 0.001, SeattleLon) });

        var hits = DynamicSourceHitTester.HitTest(click, resolution, new[] { source });

        Assert.Empty(hits);
    }

    [Fact]
    public void HitTest_OrdersHitsByAscendingDistance()
    {
        const double resolution = 50.0;
        var click = ToMercator(SeattleLat, SeattleLon);
        var source = new FakeDynamicFeatureSource("test", new DynamicSourceMetadata { DisplayName = "Test" });
        source.SetFeatures(new[]
        {
            PointFeature("far",   SeattleLat + 0.003, SeattleLon),
            PointFeature("close", SeattleLat,         SeattleLon),
            PointFeature("mid",   SeattleLat + 0.001, SeattleLon),
        });

        var hits = DynamicSourceHitTester.HitTest(click, resolution, new[] { source });

        Assert.Equal(3, hits.Count);
        Assert.Equal("close", hits[0].Feature.Id);
        Assert.Equal("mid",   hits[1].Feature.Id);
        Assert.Equal("far",   hits[2].Feature.Id);
    }

    [Fact]
    public void HitTest_WalksAllProvidedSources()
    {
        const double resolution = 100.0;
        var click = ToMercator(SeattleLat, SeattleLon);
        var s1 = new FakeDynamicFeatureSource("s1", new DynamicSourceMetadata { DisplayName = "S1" });
        var s2 = new FakeDynamicFeatureSource("s2", new DynamicSourceMetadata { DisplayName = "S2" });
        s1.SetFeatures(new[] { PointFeature("a", SeattleLat, SeattleLon) });
        s2.SetFeatures(new[] { PointFeature("b", SeattleLat, SeattleLon) });

        var hits = DynamicSourceHitTester.HitTest(click, resolution, new[] { s1, s2 });

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Source.Id == "s1");
        Assert.Contains(hits, h => h.Source.Id == "s2");
    }

    [Fact]
    public void HitTest_SkipsFeaturesWithEmptyCoordinates()
    {
        const double resolution = 1.0;
        var click = ToMercator(SeattleLat, SeattleLon);
        var source = new FakeDynamicFeatureSource("test", new DynamicSourceMetadata { DisplayName = "Test" });
        source.SetFeatures(new[]
        {
            new DynamicFeature
            {
                Id = "empty",
                GeometryType = GeometryType.Point,
                Coordinates = Array.Empty<(double, double)>(),
                LastUpdated = DateTimeOffset.UtcNow,
            },
        });

        var hits = DynamicSourceHitTester.HitTest(click, resolution, new[] { source });

        Assert.Empty(hits);
    }

    [Fact]
    public void HitTest_ReturnsEmpty_WhenResolutionIsInvalid()
    {
        var click = ToMercator(SeattleLat, SeattleLon);
        var source = new FakeDynamicFeatureSource("test", new DynamicSourceMetadata { DisplayName = "Test" });
        source.SetFeatures(new[] { PointFeature("f1", SeattleLat, SeattleLon) });

        Assert.Empty(DynamicSourceHitTester.HitTest(click, 0, new[] { source }));
        Assert.Empty(DynamicSourceHitTester.HitTest(click, -1, new[] { source }));
        Assert.Empty(DynamicSourceHitTester.HitTest(click, double.NaN, new[] { source }));
    }

    [Fact]
    public void HitTest_ReturnsHullHit_WhenClickInsidePolygon_EvenAtTinyResolution()
    {
        // 200 m × 30 m vessel, antenna at the bow (BowOffset=0, PortOffset=15).
        var feature = new DynamicFeature
        {
            Id = "ais1",
            GeometryType = GeometryType.Point,
            Coordinates = new[] { (SeattleLat, SeattleLon) },
            VesselGeometry = new DynamicVesselGeometry
            {
                LengthMetres = 200,
                BeamMetres = 30,
                BowOffsetMetres = 0,
                PortOffsetMetres = 15,
            },
            Motion = new DynamicMotion { HeadingDeg = 0 }, // bow north
            LastUpdated = DateTimeOffset.UtcNow,
        };
        // 100 m south of antenna → mid-hull, well inside polygon, far
        // outside the 12-px tolerance even at tiny resolution.
        var midHullLat = SeattleLat - 100.0 / 111_320.0;
        var click = ToMercator(midHullLat, SeattleLon);
        var source = new FakeDynamicFeatureSource("ais", new DynamicSourceMetadata { DisplayName = "AIS" });
        source.SetFeatures(new[] { feature });

        var hits = DynamicSourceHitTester.HitTest(click, resolution: 0.5, new[] { source });

        var hit = Assert.Single(hits);
        Assert.Equal(0.0, hit.DistanceMapUnits); // inside-polygon distance is 0.
        Assert.Equal("ais1", hit.Feature.Id);
    }

    [Fact]
    public void HitTest_HullMiss_FallsBackToPointTolerance()
    {
        var feature = new DynamicFeature
        {
            Id = "ais1",
            GeometryType = GeometryType.Point,
            Coordinates = new[] { (SeattleLat, SeattleLon) },
            VesselGeometry = new DynamicVesselGeometry
            {
                LengthMetres = 200,
                BeamMetres = 30,
                BowOffsetMetres = 0,
                PortOffsetMetres = 15,
            },
            LastUpdated = DateTimeOffset.UtcNow,
        };
        // Click 1 km away — well outside hull AND outside any reasonable tolerance.
        var farLat = SeattleLat + 0.01;
        var click = ToMercator(farLat, SeattleLon);
        var source = new FakeDynamicFeatureSource("ais", new DynamicSourceMetadata { DisplayName = "AIS" });
        source.SetFeatures(new[] { feature });

        var hits = DynamicSourceHitTester.HitTest(click, resolution: 1.0, new[] { source });

        Assert.Empty(hits);
    }
}
