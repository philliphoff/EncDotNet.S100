using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools.Tests.Geometry;

public class SpatialPredicatesTests
{
    [Fact]
    public void Intersects_box_query_returns_true_when_overlapping()
    {
        var bounds = new BoundingBox(0, 0, 10, 10);
        var query = new GeoQuery.Box(new GeoBoundingBox(5, 5, 15, 15));

        Assert.True(SpatialPredicates.Intersects(bounds, query));
    }

    [Fact]
    public void Intersects_box_query_returns_false_when_disjoint()
    {
        var bounds = new BoundingBox(0, 0, 10, 10);
        var query = new GeoQuery.Box(new GeoBoundingBox(20, 20, 30, 30));

        Assert.False(SpatialPredicates.Intersects(bounds, query));
    }

    [Fact]
    public void Intersects_touching_edges_returns_true()
    {
        var bounds = new BoundingBox(0, 0, 10, 10);
        var query = new GeoQuery.Box(new GeoBoundingBox(10, 10, 20, 20));

        Assert.True(SpatialPredicates.Intersects(bounds, query));
    }

    // An L-shaped route: east along the equator, then north along 1°E.
    private static GeoPolyline LRoute(double? corridorWidthMeters) => new(
        [new GeoPoint(0, 0), new GeoPoint(0, 1), new GeoPoint(1, 1)],
        corridorWidthMeters);

    [Theory]
    [InlineData(null)]
    [InlineData(1_000.0)]
    public void Intersects_polyline_ignores_the_empty_corner_of_a_bending_route(double? corridor)
    {
        // Inside the route's overall envelope, but ~55 km from either leg.
        var offTrack = new BoundingBox(0.5, 0.2, 0.6, 0.3);

        Assert.False(SpatialPredicates.Intersects(offTrack, new GeoQuery.Polyline(LRoute(corridor))));
    }

    [Fact]
    public void Intersects_polyline_matches_a_box_within_the_corridor_of_a_later_leg()
    {
        // ~1.1 km west of the northbound leg; a 2 km corridor reaches it.
        var nearLeg = new BoundingBox(0.5, 0.989, 0.51, 0.99);

        Assert.True(SpatialPredicates.Intersects(nearLeg, new GeoQuery.Polyline(LRoute(2_000.0))));
        Assert.False(SpatialPredicates.Intersects(nearLeg, new GeoQuery.Polyline(LRoute(500.0))));
    }

    [Fact]
    public void Intersects_polyline_result_depends_on_corridor_width()
    {
        // ~5.5 km north of the eastbound leg, clear of the northbound one.
        var box = new BoundingBox(0.05, 0.4, 0.06, 0.5);

        Assert.False(SpatialPredicates.Intersects(box, new GeoQuery.Polyline(LRoute(1_000.0))));
        Assert.True(SpatialPredicates.Intersects(box, new GeoQuery.Polyline(LRoute(10_000.0))));
    }

    [Fact]
    public void Contains_point_returns_true_on_boundary()
    {
        var bounds = new BoundingBox(0, 0, 10, 10);
        Assert.True(SpatialPredicates.Contains(bounds, new GeoPoint(0, 0)));
        Assert.True(SpatialPredicates.Contains(bounds, new GeoPoint(10, 10)));
    }

    [Fact]
    public void ContainsPoint_simple_quad_ray_cast()
    {
        IReadOnlyList<GeoPoint> ring = [
            new GeoPoint(0, 0),
            new GeoPoint(0, 10),
            new GeoPoint(10, 10),
            new GeoPoint(10, 0),
            new GeoPoint(0, 0)];

        Assert.True(SpatialPredicates.ContainsPoint(ring, new GeoPoint(5, 5)));
        Assert.False(SpatialPredicates.ContainsPoint(ring, new GeoPoint(15, 5)));
    }

    [Fact]
    public void ContainsPoint_empty_ring_returns_false()
    {
        Assert.False(SpatialPredicates.ContainsPoint(
            [],
            new GeoPoint(0, 0)));
    }
}
