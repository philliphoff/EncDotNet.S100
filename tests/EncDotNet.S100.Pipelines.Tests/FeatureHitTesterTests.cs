using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="FeatureHitTester"/> — the shared geographic
/// hit-test that dataset processors delegate <c>HitTestFeatures</c> to. Uses a
/// minimal in-memory <see cref="IS100Feature"/> so the geometry, radius, and
/// ordinal-alignment behaviour is exercised without opening a real dataset.
/// </summary>
public class FeatureHitTesterTests
{
    // ~111.32 m per 0.001° of latitude at the equator; longitude scales by
    // cos(lat) ≈ 1 near the equator, so both axes are ~111 320 m / degree.
    private const double MetersPerMilliDegree = 111.32;

    [Fact]
    public void HitTest_PointWithinRadius_MatchesNearWithDistance()
    {
        // A point ~111 m east of the origin.
        var features = new[] { Point("pt", 0.0, 0.001) };

        var farEnough = FeatureHitTester.HitTest(features, 0.0, 0.0, radiusMeters: 200.0);
        var tooTight = FeatureHitTester.HitTest(features, 0.0, 0.0, radiusMeters: 50.0);

        var hit = Assert.Single(farEnough);
        Assert.Equal("pt", hit.FeatureRef);
        Assert.Equal(0, hit.Ordinal);
        Assert.Equal(S100GeometryType.Point, hit.Primitive);
        Assert.False(hit.Inside);
        Assert.InRange(hit.DistanceMeters, MetersPerMilliDegree - 1.0, MetersPerMilliDegree + 1.0);

        Assert.Empty(tooTight);
    }

    [Fact]
    public void HitTest_InsideArea_MatchesWithInsideTrueAndZeroDistance()
    {
        var features = new[] { Square("area", half: 0.1) };

        // Even a zero radius matches an area by containment.
        var hits = FeatureHitTester.HitTest(features, 0.0, 0.0, radiusMeters: 0.0);

        var hit = Assert.Single(hits);
        Assert.Equal("area", hit.FeatureRef);
        Assert.Equal(S100GeometryType.Surface, hit.Primitive);
        Assert.True(hit.Inside);
        Assert.Equal(0.0, hit.DistanceMeters);
    }

    [Fact]
    public void HitTest_OutsideAreaEdge_MatchesNearOnlyWithinRadius()
    {
        // Square edge is at 0.1°; pick ~55 m east of the edge (0.1005°).
        var features = new[] { Square("area", half: 0.1) };

        var near = FeatureHitTester.HitTest(features, 0.0, 0.1005, radiusMeters: 200.0);
        var tooTight = FeatureHitTester.HitTest(features, 0.0, 0.1005, radiusMeters: 10.0);

        var hit = Assert.Single(near);
        Assert.False(hit.Inside);
        Assert.True(hit.DistanceMeters > 0.0);
        Assert.Empty(tooTight);
    }

    [Fact]
    public void HitTest_InsideHole_IsNotInside()
    {
        // Exterior half 0.5° with a concentric hole of half 0.1°. The origin
        // sits inside the hole, so it is not "inside" the area; the nearest
        // ring edge (the hole) is ~11 km away.
        var hole = Ring(0.1);
        var features = new[] { Square("donut", half: 0.5, hole) };

        var noMatch = FeatureHitTester.HitTest(features, 0.0, 0.0, radiusMeters: 100.0);
        var edgeMatch = FeatureHitTester.HitTest(features, 0.0, 0.0, radiusMeters: 20_000.0);

        Assert.Empty(noMatch);
        var hit = Assert.Single(edgeMatch);
        Assert.False(hit.Inside);
        Assert.True(hit.DistanceMeters > 0.0);
    }

    [Fact]
    public void HitTest_OrdinalsMatchEnumerationIndex_AcrossNonMatchingGaps()
    {
        var features = new IS100Feature[]
        {
            Point("far-a", 10.0, 10.0),   // 0: miss
            Point("near", 0.0, 0.0),      // 1: hit
            Point("far-b", 20.0, 20.0),   // 2: miss
            Square("area", half: 0.05),   // 3: hit (contains origin)
        };

        var hits = FeatureHitTester.HitTest(features, 0.0, 0.0, radiusMeters: 50.0);

        Assert.Equal(new[] { 1, 3 }, hits.Select(h => h.Ordinal).ToArray());
        Assert.Equal(new[] { "near", "area" }, hits.Select(h => h.FeatureRef).ToArray());
    }

    [Fact]
    public void HitTest_NullFeatureInSequence_IsSkippedButOrdinalStillAdvances()
    {
        var features = new IS100Feature[] { null!, Point("pt", 0.0, 0.0) };

        var hits = FeatureHitTester.HitTest(features, 0.0, 0.0, radiusMeters: 50.0);

        var hit = Assert.Single(hits);
        Assert.Equal(1, hit.Ordinal);
        Assert.Equal("pt", hit.FeatureRef);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-100.0)]
    public void HitTest_NonPositiveOrNonFiniteRadius_TreatedAsZero(double radius)
    {
        // Point ~111 m away: a zero-equivalent radius must not match it, but an
        // area containing the point still does (containment ignores radius).
        var point = new[] { Point("pt", 0.0, 0.001) };
        var area = new[] { Square("area", half: 0.05) };

        Assert.Empty(FeatureHitTester.HitTest(point, 0.0, 0.0, radius));
        Assert.Single(FeatureHitTester.HitTest(area, 0.0, 0.0, radius));
    }

    [Fact]
    public void HitTest_EmptySequence_ReturnsEmpty()
    {
        var hits = FeatureHitTester.HitTest(System.Array.Empty<IS100Feature>(), 0.0, 0.0, 50.0);

        Assert.Empty(hits);
    }

    private static FakeFeature Point(string id, double lat, double lon) => new()
    {
        Id = id,
        FeatureType = "TestPoint",
        GeometryType = S100GeometryType.Point,
        Points = new[] { new GeoPosition(lat, lon) },
    };

    private static FakeFeature Square(string id, double half, params IReadOnlyList<GeoPosition>[] holes) => new()
    {
        Id = id,
        FeatureType = "TestArea",
        GeometryType = S100GeometryType.Surface,
        ExteriorRing = Ring(half),
        InteriorRings = holes,
    };

    private static IReadOnlyList<GeoPosition> Ring(double half) => new[]
    {
        new GeoPosition(-half, -half),
        new GeoPosition(-half, half),
        new GeoPosition(half, half),
        new GeoPosition(half, -half),
        new GeoPosition(-half, -half),
    };

    private sealed class FakeFeature : IS100Feature
    {
        public required string Id { get; init; }
        public required string FeatureType { get; init; }
        public required S100GeometryType GeometryType { get; init; }
        public IReadOnlyList<GeoPosition> Points { get; init; } = System.Array.Empty<GeoPosition>();
        public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves { get; init; } = System.Array.Empty<IReadOnlyList<GeoPosition>>();
        public IReadOnlyList<GeoPosition> ExteriorRing { get; init; } = System.Array.Empty<GeoPosition>();
        public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings { get; init; } = System.Array.Empty<IReadOnlyList<GeoPosition>>();
        public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
        public IReadOnlyList<IS100ComplexAttribute> ComplexAttributes { get; init; } = System.Array.Empty<IS100ComplexAttribute>();
    }
}
