using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="LayerExtentCulling"/>, the per-layer viewport-extent
/// cull that lets the TiledScene ("B") custom layer renderers skip cells whose
/// data lies entirely outside the viewport. Mapsui invokes a custom layer
/// renderer for every enabled, in-resolution layer each frame without
/// extent-culling, so without this an exchange set of many S-101 cells runs the
/// full per-frame path (and worker churn) once per off-view cell.
/// </summary>
public sealed class LayerExtentCullingTests
{
    [Fact]
    public void Intersects_TrueWhenBoxesOverlap()
    {
        Assert.True(LayerExtentCulling.Intersects(
            layerMinX: 0, layerMinY: 0, layerMaxX: 100, layerMaxY: 100,
            vpMinX: 50, vpMinY: 50, vpMaxX: 150, vpMaxY: 150,
            marginWorld: 0));
    }

    [Fact]
    public void Intersects_FalseWhenLayerFarOutsideViewport()
    {
        // Layer sits well to the west of the viewport, no margin.
        Assert.False(LayerExtentCulling.Intersects(
            layerMinX: 0, layerMinY: 0, layerMaxX: 100, layerMaxY: 100,
            vpMinX: 1000, vpMinY: 0, vpMaxX: 1100, vpMaxY: 100,
            marginWorld: 0));
    }

    [Fact]
    public void Intersects_MarginPullsNearbyLayerIntoView()
    {
        // Layer ends at x=100, viewport starts at x=150: a 50-unit gap.
        // No margin -> culled; a 60-unit halo -> kept.
        Assert.False(LayerExtentCulling.Intersects(
            0, 0, 100, 100, 150, 0, 250, 100, marginWorld: 0));
        Assert.True(LayerExtentCulling.Intersects(
            0, 0, 100, 100, 150, 0, 250, 100, marginWorld: 60));
    }

    [Fact]
    public void Intersects_TrueWhenTouchingAtEdge()
    {
        // Shared edge at x=100 counts as intersecting (inclusive bounds).
        Assert.True(LayerExtentCulling.Intersects(
            0, 0, 100, 100, 100, 0, 200, 100, marginWorld: 0));
    }

    [Fact]
    public void ShouldRender_TrueForLayerWithNoExtent()
    {
        // A geometry-less layer reports a null Extent and must never be culled.
        var layer = new MemoryLayer { Features = System.Array.Empty<IFeature>() };
        Assert.Null(layer.Extent);

        Assert.True(LayerExtentCulling.ShouldRender(layer, MapViewport(), resolution: 10, marginPx: 256));
    }

    [Fact]
    public void ShouldRender_FalseForLayerOutsideViewport()
    {
        // Layer ~100 km east of a viewport centred on the origin (EPSG:3857 m).
        var layer = LayerAt(minX: 100_000, minY: 0, maxX: 101_000, maxY: 1_000);

        Assert.False(LayerExtentCulling.ShouldRender(
            layer, MapViewport(centerX: 0, centerY: 0, widthDip: 800, heightDip: 600),
            resolution: 10, marginPx: 256));
    }

    [Fact]
    public void ShouldRender_TrueForLayerInsideViewport()
    {
        var layer = LayerAt(minX: -500, minY: -500, maxX: 500, maxY: 500);

        Assert.True(LayerExtentCulling.ShouldRender(
            layer, MapViewport(centerX: 0, centerY: 0, widthDip: 800, heightDip: 600),
            resolution: 10, marginPx: 256));
    }

    private static MemoryLayer LayerAt(double minX, double minY, double maxX, double maxY)
    {
        var ring = new LinearRing(new[]
        {
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        });
        var feature = new GeometryFeature(new Polygon(ring));
        return new MemoryLayer { Features = new[] { feature } };
    }

    private static Mapsui.Viewport MapViewport(
        double centerX = 0, double centerY = 0,
        double widthDip = 800, double heightDip = 600, double resolution = 10) =>
        new(centerX, centerY, resolution, rotation: 0, width: widthDip, height: heightDip);
}
