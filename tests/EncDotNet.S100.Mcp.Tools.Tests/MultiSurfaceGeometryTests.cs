using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// A feature with several surfaces is measured and intersected surface by
/// surface, not as the single joined ring its ExteriorRing holds, which runs
/// phantom edges across the gap between the parts (issue #643).
/// </summary>
public class MultiSurfaceGeometryTests
{
    private static IS100Feature TwoSquares() => new S101VectorSource(
            S101Synth.DatasetWithMultiSurfaceFeature(
                "DepthArea",
                IdentifyFeaturesToolTests.SquareA,
                IdentifyFeaturesToolTests.SquareB))
        .GetFeatures()
        .Single();

    [Fact]
    public void Surfaces_ListsEachPart()
    {
        Assert.Equal(2, TwoSquares().Surfaces.Count);
    }

    [Fact]
    public void Distance_BetweenTheParts_IsToTheNearestRealEdge()
    {
        // (0.3, 2) is 1° of longitude from both A's east edge and B's west
        // edge; the joined ring's phantom edges pass within 0.3°.
        var distance = GeometryDistance.Measure(TwoSquares(), new GeoPoint(0.3, 2.0))!.Value;

        Assert.False(distance.Inside);
        Assert.InRange(distance.DistanceMeters, 110_000, 112_000);
    }

    [Fact]
    public void Distance_InsideAPart_IsZero()
    {
        var distance = GeometryDistance.Measure(TwoSquares(), new GeoPoint(0.5, 3.5))!.Value;

        Assert.True(distance.Inside);
        Assert.Equal(0.0, distance.DistanceMeters);
    }

    [Fact]
    public void Intersects_ALegBetweenTheParts_DoesNotMatch()
    {
        var leg = new GeoQuery.Polyline(new GeoPolyline([new GeoPoint(0.5, 1.5), new GeoPoint(0.5, 2.5)]));

        Assert.False(GeometryIntersection.Intersects(TwoSquares(), leg));
    }

    [Fact]
    public void Intersects_ALegIntoAPart_Matches()
    {
        var leg = new GeoQuery.Polyline(new GeoPolyline([new GeoPoint(0.5, 2.5), new GeoPoint(0.5, 3.5)]));

        Assert.True(GeometryIntersection.Intersects(TwoSquares(), leg));
    }
}
