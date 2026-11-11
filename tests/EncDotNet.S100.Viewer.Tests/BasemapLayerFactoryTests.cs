using EncDotNet.S100.Rendering.Scene;
using Mapsui;
using Mapsui.Nts;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Issues #295, #731: the basemap factory builds the bundled offline
/// Natural Earth land layer (tiled, with a level of detail per
/// resolution), returns nothing for <see cref="BasemapMode.None"/>, and
/// an online tile layer for <see cref="BasemapMode.Online"/>. The
/// offline layer additionally repeats its land across adjacent world
/// copies so datasets kept in a continuous longitude frame across the
/// ±180° antimeridian (e.g. the US NWS S-411 sea-ice product) have land
/// beneath them.
/// </summary>
public class BasemapLayerFactoryTests
{
    private const double Extent = WebMercator.Circumference / 2.0;

    private const double WorldResolution = WebMercator.Circumference / 1024;

    private static readonly MRect WorldBox = new(-Extent, -Extent, Extent, Extent);

    private static IEnumerable<Envelope> Envelopes(IEnumerable<IFeature> features)
        => features.OfType<GeometryFeature>()
            .Where(f => f.Geometry is not null)
            .Select(f => f.Geometry!.EnvelopeInternal);

    private static int Points(IEnumerable<IFeature> features)
        => features.OfType<GeometryFeature>().Sum(f => f.Geometry?.NumPoints ?? 0);

    private static int PointsIn(IEnumerable<IFeature> features, MRect box)
        => features.OfType<GeometryFeature>()
            .SelectMany(f => f.Geometry?.Coordinates ?? [])
            .Count(c => box.Contains(new MPoint(c.X, c.Y)));

    [Fact]
    public void None_ReturnsNull()
    {
        Assert.Null(BasemapLayerFactory.TryCreate(BasemapMode.None));
    }

    [Fact]
    public void Offline_BuildsLayerWithLandFeatures()
    {
        var layer = BasemapLayerFactory.TryCreate(BasemapMode.Offline);

        Assert.NotNull(layer);
        Assert.NotEmpty(layer!.GetFeatures(WorldBox, WorldResolution));
    }

    [Fact]
    public void Offline_RepeatsLandAcrossAdjacentWorldCopies()
    {
        var layer = BasemapLayerFactory.TryCreate(BasemapMode.Offline)!;

        // Query the neighbouring worlds; their land must sit beyond ±180° so
        // continuous-frame S-411 ice east of the antimeridian has land under
        // it (and likewise west of -180°).
        var east = new MRect(Extent, -Extent, 3 * Extent, Extent);
        var west = new MRect(-3 * Extent, -Extent, -Extent, Extent);
        double maxX = Envelopes(layer.GetFeatures(east, WorldResolution)).Max(e => e.MaxX);
        double minX = Envelopes(layer.GetFeatures(west, WorldResolution)).Min(e => e.MinX);

        Assert.True(maxX > Extent, $"expected land east of +Extent, got maxX={maxX}");
        Assert.True(minX < -Extent, $"expected land west of -Extent, got minX={minX}");
    }

    [Fact]
    public void Offline_ReturnsFinerLandOnlyNearTheView_WhenZoomedIn()
    {
        var layer = BasemapLayerFactory.TryCreate(BasemapMode.Offline)!;

        // Elliott Bay at ~1:50k (issue #731): only tiles near the view, at
        // full resolution, so the coastline has more points than the world
        // view's simplified one while the whole frame stays small.
        var (minX, minY) = WebMercator.FromLonLat(-122.56, 47.52);
        var (maxX, maxY) = WebMercator.FromLonLat(-122.33, 47.66);
        var box = new MRect(minX, minY, maxX, maxY);

        var near = layer.GetFeatures(box, resolution: 15).ToList();
        var world = layer.GetFeatures(WorldBox, WorldResolution).ToList();

        Assert.NotEmpty(near);
        Assert.All(Envelopes(near), e => Assert.True(e.Intersects(new Envelope(minX, maxX, minY, maxY))));
        Assert.True(Points(near) < Points(world) / 10, $"near={Points(near)} world={Points(world)}");
        int fine = PointsIn(near, box);
        int coarse = PointsIn(layer.GetFeatures(box, resolution: 5_000), box);
        Assert.True(fine > 2 * coarse, $"the zoomed-in coastline should be finer: {fine} vs {coarse} points");
    }

    [Fact]
    public void Offline_ReportsBoundedSingleWorldExtent()
    {
        var extent = BasemapLayerFactory.TryCreate(BasemapMode.Offline)!.Extent;

        // Even though the geometry spans world copies, the layer must report
        // only the canonical single world so the copies never inflate
        // Map.Extent (which drives "zoom to extent").
        Assert.NotNull(extent);
        Assert.Equal(-Extent, extent!.MinX, 3);
        Assert.Equal(-Extent, extent.MinY, 3);
        Assert.Equal(Extent, extent.MaxX, 3);
        Assert.Equal(Extent, extent.MaxY, 3);
    }

    [Fact]
    public void Online_BuildsTileLayer()
    {
        var layer = BasemapLayerFactory.TryCreate(BasemapMode.Online);

        Assert.NotNull(layer);
        Assert.IsType<Mapsui.Tiling.Layers.TileLayer>(layer);
    }
}
