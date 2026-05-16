using System.Collections.Immutable;
using EncDotNet.S100.Mcp.Tools.Geometry;
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
        var ring = ImmutableArray.Create(
            new GeoPoint(0, 0),
            new GeoPoint(0, 10),
            new GeoPoint(10, 10),
            new GeoPoint(10, 0),
            new GeoPoint(0, 0));

        Assert.True(SpatialPredicates.ContainsPoint(ring, new GeoPoint(5, 5)));
        Assert.False(SpatialPredicates.ContainsPoint(ring, new GeoPoint(15, 5)));
    }

    [Fact]
    public void ContainsPoint_empty_ring_returns_false()
    {
        Assert.False(SpatialPredicates.ContainsPoint(
            ImmutableArray<GeoPoint>.Empty,
            new GeoPoint(0, 0)));
    }
}
