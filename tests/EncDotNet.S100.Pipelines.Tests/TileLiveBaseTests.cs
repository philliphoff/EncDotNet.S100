using System.Diagnostics;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
using Mapsui.Layers;
using Mapsui.Rendering;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins the tiled renderer's live base plane: a sparse scene whose product
/// hints live drawing (<see cref="VectorScene.PreferLiveDrawing"/>, an S-421
/// route) is drawn straight onto the canvas every frame, scheduling no tiles,
/// and looks the same as when it is tiled. A collection of many such datasets
/// otherwise composited a stack of mostly transparent tiles per dataset every
/// frame.
/// </summary>
[Collection(RenderingOptimizationsCollection.Name)]
public sealed class TileLiveBaseTests
{
    private const int Band = 10;
    private const int ViewportDip = 256;
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void IsLiveBaseScene_HintedFewLines_IsLive()
    {
        Assert.True(S100VectorTileRenderer.IsLiveBaseScene(Hinted(Route(0))));
    }

    [Fact]
    public void IsLiveBaseScene_HintedEmptyScene_IsLive()
    {
        Assert.True(S100VectorTileRenderer.IsLiveBaseScene(Hinted()));
    }

    [Fact]
    public void IsLiveBaseScene_WithoutHint_IsTiled()
    {
        // However sparse, a scene is tiled unless its product asks otherwise:
        // a small ENC cell must look like the tiled cells beside it.
        Assert.False(S100VectorTileRenderer.IsLiveBaseScene(new VectorScene(new List<PaintOp> { Route(0) })));
    }

    [Fact]
    public void IsLiveBaseScene_PatternFill_IsTiled()
    {
        var pattern = new PatternAreaPaintOp
        {
            FeatureReference = "pattern",
            PatternReference = "DIAMOND1",
            WorldShell = [(0, 0), (1, 0), (1, 1), (0, 0)],
            TilePng = [],
        };

        Assert.False(S100VectorTileRenderer.IsLiveBaseScene(Hinted(Route(0), pattern)));
    }

    [Fact]
    public void IsLiveBaseScene_OverVertexBudget_IsTiled()
    {
        var vertices = Enumerable.Range(0, S100VectorTileRenderer.LiveBaseMaxVertices + 1)
            .Select(i => ((double)i, 0d))
            .ToList();
        var dense = new LinePaintOp { FeatureReference = "dense", World = vertices, WidthPx = 1 };

        Assert.False(S100VectorTileRenderer.IsLiveBaseScene(Hinted(dense)));
    }

    [Fact]
    public void Render_LiveScene_DrawsWithoutSchedulingTiles()
    {
        var viewport = MakeViewport();
        var layer = BindLayer(preferLiveDrawing: true);

        using var surface = RenderFrame(layer, viewport, deviceScale: 2f);

        Assert.True(Ink(surface) > 0);
        Assert.Empty(S100VectorTileRenderer.CachedTilePixelSizesForTest(layer));
        Assert.False(S100VectorTileRenderer.HasVisibleTileWorkForTest(layer));
    }

    [Fact]
    public void Render_LiveScene_MatchesTiledScene()
    {
        // Tiles are rasterised in device pixels, so a live layer must stroke its
        // widths in device pixels too; drawn in DIP its lines came out twice as
        // heavy on a 2x display.
        var viewport = MakeViewport();
        var tiled = BindLayer(preferLiveDrawing: false);
        var live = BindLayer(preferLiveDrawing: true);

        using var tiledSurface = RenderSettled(tiled, viewport, deviceScale: 2f);
        using var liveSurface = RenderFrame(live, viewport, deviceScale: 2f);

        var tiledInk = Ink(tiledSurface);
        var liveInk = Ink(liveSurface);
        Assert.True(tiledInk > 0);
        Assert.InRange(liveInk, tiledInk * 0.8, tiledInk * 1.25);
    }

    private static long Ink(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        long ink = 0;
        foreach (var pixel in bitmap.Pixels)
        {
            if (pixel.Alpha > 128)
            {
                ink++;
            }
        }

        return ink;
    }

    private static SKSurface RenderFrame(ILayer layer, Mapsui.Viewport viewport, float deviceScale)
    {
        var surface = CreateSurface(deviceScale);
        surface.Canvas.Scale(deviceScale);
        S100VectorTileRenderer.Render(surface.Canvas, viewport, layer, new RenderService());
        return surface;
    }

    /// <summary>Renders live frames until every visible tile is resident, then one more onto a clean surface.</summary>
    private static SKSurface RenderSettled(ILayer layer, Mapsui.Viewport viewport, float deviceScale)
    {
        var px = S100VectorTileRenderer.TilePixelSize(deviceScale);
        var renderService = new RenderService();
        var sw = Stopwatch.StartNew();
        while (true)
        {
            using (var scratch = CreateSurface(deviceScale))
            {
                scratch.Canvas.Scale(deviceScale);
                S100VectorTileRenderer.Render(scratch.Canvas, viewport, layer, renderService);
            }

            if (S100VectorTileRenderer.IsVisibleSettledForTest(layer, px))
            {
                return RenderFrame(layer, viewport, deviceScale);
            }

            if (sw.Elapsed > SettleTimeout)
            {
                Assert.Fail(
                    $"Tiles did not settle within {SettleTimeout.TotalSeconds:F0} s: "
                    + S100VectorTileRenderer.DescribeTileWorkForTest(layer));
            }

            Thread.Sleep(20);
        }
    }

    private static SKSurface CreateSurface(float deviceScale)
    {
        var edge = (int)Math.Ceiling(ViewportDip * deviceScale);
        var surface = SKSurface.Create(new SKImageInfo(edge, edge));
        surface.Canvas.Clear(SKColors.Transparent);
        return surface;
    }

    private static Mapsui.Viewport MakeViewport()
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(new TileKey(Band, 512, 512));
        return new Mapsui.Viewport(
            (minX + maxX) * 0.5,
            (minY + maxY) * 0.5,
            TileGrid.ResolutionForBand(Band),
            rotation: 0,
            width: ViewportDip,
            height: ViewportDip);
    }

    private static VectorScene Hinted(params PaintOp[] ops) => new(ops) { PreferLiveDrawing = true };

    /// <summary>
    /// A layer bound to a few dashed route legs crossing the viewport, with no
    /// disk namespace so the test never touches the shared warm cache.
    /// </summary>
    private static ILayer BindLayer(bool preferLiveDrawing)
    {
        var layer = new MemoryLayer { Name = preferLiveDrawing ? "live" : "tiled" };
        var scene = new VectorScene(new List<PaintOp> { Route(-60), Route(0), Route(60) })
        {
            PreferLiveDrawing = preferLiveDrawing,
        };
        S100VectorTileRenderer.BindScene(layer, scene, productLayerSet: null, styleStateHash: null);
        return layer;
    }

    /// <summary>A dashed leg from the viewport's west edge to its east edge, offset north by <paramref name="offsetDip"/>.</summary>
    private static LinePaintOp Route(double offsetDip)
    {
        var viewport = MakeViewport();
        var half = viewport.Resolution * ViewportDip;
        var dy = viewport.Resolution * offsetDip;
        return new LinePaintOp
        {
            FeatureReference = $"leg{offsetDip}",
            World =
            [
                (viewport.CenterX - half, viewport.CenterY + dy - half * 0.2),
                (viewport.CenterX + half, viewport.CenterY + dy + half * 0.2),
            ],
            Color = new RgbaColor(220, 60, 40, 255),
            WidthPx = 2.0,
            DashArrayPx = [6f, 4f],
        };
    }
}
