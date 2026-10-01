using System.Diagnostics;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
using Mapsui.Layers;
using Mapsui.Rendering;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins that the tiled base plane's hot cache is device-scale aware: a frame at
/// one scale (for example an off-screen capture at 1x through the same live
/// layer) must never leave tiles behind that a later frame at another scale
/// (the 2x retina window) keeps blitting.
/// </summary>
public sealed class TileDeviceScaleTests
{
    private const int Band = 10;
    private static readonly TimeSpan LandTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public void LiveFrame_AfterColdFrameAtOtherScale_ReplacesTilesAtLiveScale()
    {
        var layer = BindLayer();
        var viewport = MakeViewport();

        // A cold frame at 1x (e.g. a window on a non-retina display) fills the
        // cache with 1x tiles.
        RenderUntilSettled(layer, viewport, deviceScale: 1f);
        Assert.All(Sizes(layer).Values, px => Assert.Equal(TilePx(1f), px));

        // Back at 2x the 1x tiles are a miss, not a hit: every visible tile is
        // re-rasterised at 2x.
        RenderUntilSettled(layer, viewport, deviceScale: 2f);
        AssertVisibleTilesAt(layer, viewport, TilePx(2f));
    }

    [Fact]
    public void OffscreenFrame_AtOtherScale_DoesNotScheduleOrReplaceLiveTiles()
    {
        var layer = BindLayer();
        var viewport = MakeViewport();
        RenderUntilSettled(layer, viewport, deviceScale: 2f);
        Assert.NotEmpty(Sizes(layer));

        // An off-screen capture at 1x composites the cached 2x tiles and
        // schedules nothing: neither visible nor speculative (predicted /
        // cross-band) work is enqueued at its scale, and in-flight live work
        // keeps the live 2x scale.
        using (var surface = SKSurface.Create(new SKImageInfo(256, 256)))
        using (S100VectorTileRenderer.BeginOffscreenRender())
        {
            S100VectorTileRenderer.Render(surface.Canvas, viewport, layer, new RenderService());
            Assert.NotEqual(SKColors.Transparent, Pixel(surface, 128, 128));
        }

        Thread.Sleep(500);
        Assert.DoesNotContain(TilePx(1f), Sizes(layer).Values);

        // A live 2x frame after the capture still finds every tile warm at 2x.
        RenderUntilSettled(layer, viewport, deviceScale: 2f);
        AssertVisibleTilesAt(layer, viewport, TilePx(2f));
    }

    [Fact]
    public void OffscreenScope_IsThreadLocalAndNests()
    {
        Assert.False(S100VectorTileRenderer.IsOffscreenRender);
        using (S100VectorTileRenderer.BeginOffscreenRender())
        {
            using (S100VectorTileRenderer.BeginOffscreenRender())
            {
                Assert.True(S100VectorTileRenderer.IsOffscreenRender);
            }

            Assert.True(S100VectorTileRenderer.IsOffscreenRender);
            var other = false;
            var thread = new Thread(() => other = S100VectorTileRenderer.IsOffscreenRender);
            thread.Start();
            thread.Join();
            Assert.False(other);
        }

        Assert.False(S100VectorTileRenderer.IsOffscreenRender);
    }

    private static SKColor Pixel(SKSurface surface, int x, int y)
    {
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    private static int TilePx(float scale) => S100VectorTileRenderer.TilePixelSize(scale);

    private static IReadOnlyDictionary<TileKey, int> Sizes(ILayer layer) =>
        S100VectorTileRenderer.CachedTilePixelSizesForTest(layer);

    private static void AssertVisibleTilesAt(ILayer layer, Mapsui.Viewport viewport, int px)
    {
        var sizes = Sizes(layer);
        var visible = VisibleKeys(viewport);
        Assert.NotEmpty(visible);
        foreach (var key in visible)
        {
            Assert.True(sizes.TryGetValue(key, out var actual), $"{key} not cached");
            Assert.Equal(px, actual);
        }
    }

    private static IReadOnlyList<TileKey> VisibleKeys(Mapsui.Viewport viewport) =>
        TileGrid.VisibleTiles(
            viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height,
            viewport.Resolution, Band);

    /// <summary>
    /// Renders frames at <paramref name="deviceScale"/> until every visible tile
    /// is resident at that scale's pixel size (or the timeout lapses).
    /// </summary>
    private static void RenderUntilSettled(ILayer layer, Mapsui.Viewport viewport, float deviceScale)
    {
        var px = TilePx(deviceScale);
        var info = new SKImageInfo(
            (int)Math.Ceiling(viewport.Width * deviceScale),
            (int)Math.Ceiling(viewport.Height * deviceScale));
        using var surface = SKSurface.Create(info);
        var renderService = new RenderService();
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < LandTimeout)
        {
            surface.Canvas.ResetMatrix();
            surface.Canvas.Scale(deviceScale);
            S100VectorTileRenderer.Render(surface.Canvas, viewport, layer, renderService);
            var sizes = Sizes(layer);
            if (VisibleKeys(viewport).All(k => sizes.TryGetValue(k, out var s) && s == px))
            {
                return;
            }

            Thread.Sleep(20);
        }
    }

    private static Mapsui.Viewport MakeViewport()
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(new TileKey(Band, 512, 512));
        return new Mapsui.Viewport(
            (minX + maxX) * 0.5,
            (minY + maxY) * 0.5,
            TileGrid.ResolutionForBand(Band),
            rotation: 0,
            width: 256,
            height: 256);
    }

    /// <summary>
    /// A layer bound to a single opaque area op that covers the whole viewport
    /// (and its neighbours), with no disk namespace so the test never touches
    /// the shared warm cache.
    /// </summary>
    private static ILayer BindLayer()
    {
        var viewport = MakeViewport();
        var half = viewport.Resolution * 1024;
        var (cx, cy) = (viewport.CenterX, viewport.CenterY);
        var area = new AreaPaintOp
        {
            FeatureReference = "sea",
            WorldShell = new[]
            {
                (cx - half, cy - half), (cx + half, cy - half),
                (cx + half, cy + half), (cx - half, cy + half), (cx - half, cy - half),
            },
            Fill = new RgbaColor(120, 160, 220, 255),
            OutlineColor = new RgbaColor(0, 0, 0, 255),
            OutlineWidthPx = 1.0,
        };

        var layer = new MemoryLayer { Name = "scale-test" };
        S100VectorTileRenderer.BindScene(
            layer, new VectorScene(new List<PaintOp> { area }), productLayerSet: null, styleStateHash: null);
        return layer;
    }
}
