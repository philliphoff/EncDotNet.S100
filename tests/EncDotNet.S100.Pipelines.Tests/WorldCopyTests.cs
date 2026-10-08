using System.Diagnostics;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Rendering;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins that chart data draws at every world copy in view (issue #773): a
/// dataset kept in a 0…360 frame (NIC Arctic S-411) or past the antimeridian
/// (NWS Alaska S-411, ~175°E → ~225°E) shows on both sides of 0° and ±180°,
/// as the basemap does, and overlays tied to the data follow it.
/// </summary>
/// <remarks>
/// Serial with the other tile-renderer tests: tile workers come from a
/// process-wide pool, so a sibling class's dense layer could otherwise hold
/// every worker while these frames wait for their tiles.
/// </remarks>
[Collection(RenderingOptimizationsCollection.Name)]
public sealed class WorldCopyTests
{
    private const double C = WorldCopies.Circumference;
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void Visible_ZeroTo360Data_AtPrimeMeridian_DrawsOwnFrameAndWestCopy()
    {
        // 0…360° data viewed across 0°: the part west of 0° is the west copy.
        var offsets = WorldCopies.Visible(X(0), X(360), X(-5), X(5), marginWorld: 0);

        Assert.Equal([-C, 0.0], offsets);
    }

    [Fact]
    public void Visible_StandardFrameData_AwayFromSeam_DrawsOwnFrameOnly()
    {
        var offsets = WorldCopies.Visible(X(-10), X(10), X(-5), X(5), marginWorld: 0);

        Assert.Equal([0.0], offsets);
    }

    [Fact]
    public void Visible_DataEastOfAntimeridian_ViewedAtItsStandardLongitude_DrawsWestCopyOnly()
    {
        // NWS Alaska (~175°E → ~225°E) viewed at 160°W: only the west copy.
        var offsets = WorldCopies.Visible(X(175), X(225), X(-165), X(-155), marginWorld: 0);

        Assert.Equal([-C], offsets);
    }

    [Fact]
    public void Visible_MarginKeepsCopyJustOffView()
    {
        Assert.Empty(WorldCopies.Visible(X(10), X(20), X(-5), X(5), marginWorld: 0));
        Assert.Equal([0.0], WorldCopies.Visible(X(10), X(20), X(-5), X(5), marginWorld: X(6)));
    }

    [Fact]
    public void Nearest_PicksCopyClosestToCentre()
    {
        IReadOnlyList<double> offsets = [-C, 0.0];

        Assert.Equal(-C, WorldCopies.Nearest(offsets, X(175), X(225), X(-170)));
        Assert.Equal(0.0, WorldCopies.Nearest(offsets, X(175), X(225), X(180)));
    }

    [Fact]
    public void CandidateLongitudes_AddsCopiesOnlyWithinTheDataSpan()
    {
        Assert.Equal([-170.0, 190.0], WorldCopies.CandidateLongitudes(-170, 175, 225));
        Assert.Equal([5.0], WorldCopies.CandidateLongitudes(5, 0, 360));
        Assert.Equal([-10.0, 350.0], WorldCopies.CandidateLongitudes(-10, 0, 360));
        Assert.Equal([-170.0], WorldCopies.CandidateLongitudes(-170, -10, 10));
        Assert.Equal([-170.0], WorldCopies.CandidateLongitudes(-170, null, null));
    }

    [Fact]
    public void VisibleWorldCopies_UsesLayerExtent()
    {
        var layer = ExtentLayer(175, 225);
        var viewport = new Mapsui.Viewport(X(-170), 0, Resolution, 0, 256, 256);

        Assert.Equal([-C], LayerExtentCulling.VisibleWorldCopies(layer, viewport, Resolution, marginPx: 0));
    }

    [Fact]
    public void VisibleWorldCopies_LayerWithoutExtent_DrawsOwnFrame()
    {
        var layer = new MemoryLayer();
        var viewport = new Mapsui.Viewport(0, 0, Resolution, 0, 256, 256);

        Assert.Equal([0.0], LayerExtentCulling.VisibleWorldCopies(layer, viewport, Resolution, marginPx: 0));
    }

    [Fact]
    public void VisibleWorldCopies_OutOfViewNorthOrSouth_DrawsNothing()
    {
        var layer = ExtentLayer(175, 225);
        var viewport = new Mapsui.Viewport(X(-170), Y(-60), Resolution, 0, 256, 256);

        Assert.Empty(LayerExtentCulling.VisibleWorldCopies(layer, viewport, Resolution, marginPx: 0));
    }

    [Fact]
    public void TileRender_DataEastOfAntimeridian_DrawsAtWestCopy()
    {
        // NWS-style data at 185°–195°E, viewed at its standard longitude
        // 170°W: before #773 the view was empty there.
        var layer = BindArea(185, 195);
        var viewport = new Mapsui.Viewport(X(-170), 0, Resolution, 0, 256, 256);

        using var surface = RenderSettled(layer, viewport);

        Assert.False(IsClear(Pixel(surface, 128, 128)));
    }

    [Fact]
    public void TileRender_ZeroTo360Data_IsContinuousAcrossPrimeMeridian()
    {
        // NIC-style 0…360° data viewed on 0°: the west half comes from the
        // west copy (data at 350°–360°), the east half from the own frame.
        var layer = BindArea(0, 360);
        var viewport = new Mapsui.Viewport(0, 0, Resolution, 0, 256, 256);

        using var surface = RenderSettled(layer, viewport);

        Assert.False(IsClear(Pixel(surface, 32, 128)), "west of 0° is empty");
        Assert.False(IsClear(Pixel(surface, 224, 128)), "east of 0° is empty");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(1)]
    [InlineData(0)]
    public void TileRender_ZeroTo360TranslucentData_HasNoSeamLineBetweenBands(int band)
    {
        // Between bands the tiles are scaled with bilinear sampling. The seam
        // at 0° must look like any other tile edge: no column lighter (an empty
        // gutter blended in) or darker (two copies' tiles both drawn), also
        // zoomed out to where a tile straddles the seam and the view spans
        // more than one world.
        var layer = BindArea(0, 360, alpha: 128);
        var resolution = TileGrid.ResolutionForBand(band) * 1.37;
        var viewport = new Mapsui.Viewport(0.31 * resolution, 0, resolution, 0, 256, 256);
        var row = 128;

        // Settled: every visible tile rasterised, so no backdrop band remains.
        using var surface = RenderSettled(layer, viewport);

        var alphas = Enumerable.Range(0, 256).Select(x => (int)Pixel(surface, x, row).Alpha).ToList();
        Assert.InRange(alphas[64], 120, 136);
        if (band == 0)
        {
            // At band 0 one tile spans the seam, so the two copies' fills meet
            // inside tile cores; their anti-aliased edges blend there like
            // any two translucent shapes that abut. Only no column may be
            // drawn twice.
            Assert.All(alphas, a => Assert.True(a <= alphas[64] + 2, string.Join(",", alphas)));
            return;
        }

        Assert.True(alphas.All(a => Math.Abs(a - alphas[64]) <= 2), string.Join(",", alphas.Select((a, x) => (a, x)).Where(p => Math.Abs(p.a - alphas[64]) > 2)));
    }

    [Fact]
    public void TileRender_StandardFrameData_AwayFromCopies_StaysEmpty()
    {
        // A copy is drawn only where the data's copy is: 185°–195°E data does
        // not appear at 10°E.
        var layer = BindArea(185, 195);
        var viewport = new Mapsui.Viewport(X(10), 0, Resolution, 0, 256, 256);

        using var surface = RenderFor(layer, viewport, TimeSpan.FromMilliseconds(500));

        Assert.True(IsClear(Pixel(surface, 128, 128)));
    }

    [Fact]
    public void RasterizeTile_AtTheFrameEdge_FillsItsGutterFromTheAdjacentCopy()
    {
        // The last tile of a 0…360 dataset ends where the data is clipped at
        // 360°; its east gutter is filled from the data at 0° (one world east),
        // so a scaled blit does not blend an empty gutter into a seam line.
        var scene = Scene(0, 360);
        const int band = 10;
        var key = new TileKey(band, (int)(1.5 * TileGrid.TilesPerAxis(band)) - 1, TileGrid.TilesPerAxis(band) / 2);
        Assert.Equal(C, TileGrid.TileWorldBounds(key).MaxX, 3);

        using var bitmap = S100VectorTileRenderer.RasterizeTile(scene, new BaseSpatialIndex(scene), key, deviceScale: 1f);

        Assert.Equal(255, bitmap.GetPixel(bitmap.Width - 1, bitmap.Height / 2).Alpha);
        Assert.Equal(255, bitmap.GetPixel(0, bitmap.Height / 2).Alpha);
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(0, 1)]
    public void RasterizeTile_LeavesItsCoreToTheCopiesOwnTiles(int band, int columnPastEdge)
    {
        // The tile just east of 360° (band 4), or the band-0 tile straddling
        // it (180°…540°), shows no copy of the data in its core: the west
        // copy's own tiles draw that place, so the compositor would draw a
        // translucent fill twice.
        var scene = Scene(0, 360, alpha: 128);
        var column = band == 0 ? columnPastEdge : (int)(1.5 * TileGrid.TilesPerAxis(band)) + columnPastEdge;
        var key = new TileKey(band, column, TileGrid.TilesPerAxis(band) / 2);
        var (minX, _, maxX, _) = TileGrid.TileWorldBounds(key);
        Assert.InRange(C, minX - 1, maxX);

        using var bitmap = S100VectorTileRenderer.RasterizeTile(scene, new BaseSpatialIndex(scene), key, deviceScale: 1f);

        // A core pixel just east of 360°.
        var gutter = (bitmap.Width - TileGrid.TileSizeDip) / 2;
        var x = gutter + (int)Math.Ceiling((C - minX) / (maxX - minX) * TileGrid.TileSizeDip) + 2;
        Assert.Equal(0, bitmap.GetPixel(x, bitmap.Height / 4).Alpha);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void RasterizeMetatile_AtTheFrameEdge_MatchesIndependentTiles(float deviceScale)
    {
        // A block straddling 360° gets the same adjacent-copy gutter as the
        // tiles rasterised one by one.
        var scene = Scene(0, 360, alpha: 128);
        var index = new BaseSpatialIndex(scene);
        const int band = 4;
        var edge = (int)(1.5 * TileGrid.TilesPerAxis(band));
        var row = TileGrid.TilesPerAxis(band) / 2;
        TileKey[] keys = [new(band, edge - 2, row), new(band, edge - 1, row), new(band, edge, row), new(band, edge + 1, row)];

        var actual = S100VectorTileRenderer.RasterizeMetatile(scene, index, keys, deviceScale);
        try
        {
            foreach (var key in keys)
            {
                using var expected = S100VectorTileRenderer.RasterizeTile(scene, index, key, deviceScale);
                using var slice = SKBitmap.FromImage(actual[key]);
                for (var x = 0; x < expected.Width; x += 3)
                {
                    Assert.Equal(expected.GetPixel(x, expected.Height / 4), slice.GetPixel(x, slice.Height / 4));
                }
            }
        }
        finally
        {
            foreach (var image in actual.Values)
            {
                image.Dispose();
            }
        }
    }

    [Theory]
    [InlineData(1.5, -1)]
    [InlineData(1.0, 0)]
    public void RasterizeTile_DrawsEachOpOnce(double columnFraction, int columnDelta)
    {
        var scene = Scene(0, 360, alpha: 128);
        const int band = 4;
        var key = new TileKey(band, (int)(columnFraction * TileGrid.TilesPerAxis(band)) + columnDelta, TileGrid.TilesPerAxis(band) / 2);

        using var bitmap = S100VectorTileRenderer.RasterizeTile(scene, new BaseSpatialIndex(scene), key, deviceScale: 1f);

        Assert.InRange(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 4).Alpha, 120, 136);
        Assert.InRange(bitmap.GetPixel(bitmap.Width - 1, bitmap.Height / 4).Alpha, 120, 136);
        Assert.InRange(bitmap.GetPixel(0, bitmap.Height / 4).Alpha, 120, 136);
    }

    [Fact]
    public void WorldCopyLayer_ReturnsShiftedCopyForViewOnAdjacentWorld()
    {
        var layer = new WorldCopyMemoryLayer { Features = [Box(185, 195)] };
        var original = (GeometryFeature)Assert.Single(layer.Features);
        var west = new MRect(X(-176), Y(-1), X(-164), Y(1));

        var copy = (GeometryFeature)Assert.Single(layer.GetFeatures(west, Resolution));

        Assert.NotSame(original, copy);
        Assert.Equal(X(185) - C, copy.Geometry!.EnvelopeInternal.MinX, 3);
        // The original stays in its raw frame, as does the layer extent.
        Assert.Equal(X(185), original.Geometry!.EnvelopeInternal.MinX, 3);
        Assert.Equal(X(185), layer.Extent!.MinX, 3);
        // The copy is reused across frames, so Mapsui's per-feature caches hold.
        Assert.Same(copy, Assert.Single(layer.GetFeatures(west, Resolution)));
    }

    [Fact]
    public void WorldCopyLayer_CopyKeepsFieldsAndStyles()
    {
        // Picks resolve a copy by its feature-reference field, and the copy
        // draws with the original's styles.
        var feature = Box(185, 195);
        feature[MapsuiDisplayListRenderer.FeatureRefKey] = "ice-1";
        feature.Styles.Add(new Mapsui.Styles.VectorStyle());

        var copy = WorldCopyFeatures.Shift(feature, -C);

        Assert.Equal("ice-1", copy[MapsuiDisplayListRenderer.FeatureRefKey]);
        Assert.Single(copy.Styles);
        Assert.Equal(X(185), feature.Geometry!.EnvelopeInternal.MinX, 3);
    }

    [Fact]
    public void WorldCopyLayer_ReturnsOriginalForViewOnOwnFrame()
    {
        var layer = new WorldCopyMemoryLayer { Features = [Box(185, 195)] };
        var own = new MRect(X(186), Y(-1), X(194), Y(1));

        Assert.Same(Assert.Single(layer.Features), Assert.Single(layer.GetFeatures(own, Resolution)));
    }

    [Fact]
    public void TiledDatasetLayer_ReturnsPickFeaturesOnAdjacentWorld()
    {
        // A click on the west copy of 185°–195°E data (at 170°W) hits its
        // pick feature there, so the Object Information panel resolves it.
        var feature = Box(185, 195);
        feature[MapsuiDisplayListRenderer.FeatureRefKey] = "ice-1";
        var layer = new InstrumentedMemoryLayer { Features = [feature], RepeatsAcrossWorldCopies = true };
        var click = new MRect(X(-170), Y(0), X(-170), Y(0));

        var hit = Assert.Single(layer.GetFeatures(click, Resolution));

        Assert.Equal("ice-1", hit[MapsuiDisplayListRenderer.FeatureRefKey]);
        Assert.Equal(X(185) - C, hit.Extent!.MinX, 3);
    }

    [Fact]
    public void UntiledDatasetLayer_ReturnsOwnFrameOnly()
    {
        var layer = new InstrumentedMemoryLayer { Features = [Box(185, 195)] };
        var click = new MRect(X(-170), Y(0), X(-170), Y(0));

        Assert.Empty(layer.GetFeatures(click, Resolution));
    }

    private static double Resolution => TileGrid.ResolutionForBand(10);

    private static double X(double longitude) => longitude * C / 360.0;

    private static double Y(double latitude) => WebMercator.FromLonLat(0, latitude).Y;

    private static GeometryFeature Box(double west, double east) =>
        new(new Polygon(new LinearRing(
        [
            new Coordinate(X(west), Y(-10)),
            new Coordinate(X(east), Y(-10)),
            new Coordinate(X(east), Y(10)),
            new Coordinate(X(west), Y(10)),
            new Coordinate(X(west), Y(-10)),
        ])));

    private static MemoryLayer ExtentLayer(double west, double east) =>
        new() { Features = [Box(west, east)] };

    /// <summary>
    /// A tiled layer bound to one opaque area from <paramref name="west"/> to
    /// <paramref name="east"/> (raw longitudes, ±10° latitude) with a matching
    /// layer extent and no disk namespace.
    /// </summary>
    private static MemoryLayer BindArea(double west, double east, byte alpha = 255)
    {
        var layer = ExtentLayer(west, east);
        S100VectorTileRenderer.BindScene(layer, Scene(west, east, alpha), productLayerSet: null, styleStateHash: null);
        return layer;
    }

    private static VectorScene Scene(double west, double east, byte alpha = 255) =>
        new(new List<PaintOp>
        {
            new AreaPaintOp
            {
                FeatureReference = "ice",
                WorldShell =
                [
                    (X(west), Y(-10)), (X(east), Y(-10)),
                    (X(east), Y(10)), (X(west), Y(10)), (X(west), Y(-10)),
                ],
                Fill = new RgbaColor(120, 160, 220, alpha),
            },
        });

    /// <summary>
    /// Renders live frames until every visible tile is rasterised and resident
    /// (the background workers have no visible work left), then returns one
    /// more frame composited from that settled cache. Waiting on the renderer's
    /// state rather than on pixels keeps a slow or contended host from passing
    /// a half-drawn frame on to the assertions; a host that never settles fails
    /// with the scheduler state rather than with a blank pixel.
    /// </summary>
    private static SKSurface RenderSettled(ILayer layer, Mapsui.Viewport viewport)
    {
        var surface = SKSurface.Create(new SKImageInfo((int)viewport.Width, (int)viewport.Height));
        var renderService = new RenderService();
        var tilePx = S100VectorTileRenderer.TilePixelSize(1f);
        var sw = Stopwatch.StartNew();
        while (true)
        {
            surface.Canvas.Clear(SKColors.Transparent);
            S100VectorTileRenderer.Render(surface.Canvas, viewport, layer, renderService);
            if (S100VectorTileRenderer.IsVisibleSettledForTest(layer, tilePx))
            {
                break;
            }

            if (sw.Elapsed > SettleTimeout)
            {
                surface.Dispose();
                Assert.Fail(
                    $"Tiles did not settle within {SettleTimeout.TotalSeconds:F0} s: "
                    + S100VectorTileRenderer.DescribeTileWorkForTest(layer));
            }

            Thread.Sleep(20);
        }

        surface.Canvas.Clear(SKColors.Transparent);
        S100VectorTileRenderer.Render(surface.Canvas, viewport, layer, renderService);
        return surface;
    }

    /// <summary>
    /// Renders live frames for <paramref name="duration"/>, for a view that
    /// must stay empty (no visible tiles, so nothing ever settles).
    /// </summary>
    private static SKSurface RenderFor(ILayer layer, Mapsui.Viewport viewport, TimeSpan duration)
    {
        var surface = SKSurface.Create(new SKImageInfo((int)viewport.Width, (int)viewport.Height));
        var renderService = new RenderService();
        var sw = Stopwatch.StartNew();
        do
        {
            surface.Canvas.Clear(SKColors.Transparent);
            S100VectorTileRenderer.Render(surface.Canvas, viewport, layer, renderService);
            Thread.Sleep(20);
        }
        while (sw.Elapsed < duration);

        return surface;
    }

    private static SKColor Pixel(SKSurface surface, int x, int y)
    {
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    private static bool IsClear(SKColor color) => color.Alpha == 0;
}
