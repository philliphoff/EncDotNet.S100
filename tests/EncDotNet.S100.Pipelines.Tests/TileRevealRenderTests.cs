using System.Diagnostics;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
using Mapsui.Layers;
using Mapsui.Rendering;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins how a newly drawn cell appears in the tiled renderer: its live symbol
/// overlay waits for its base plane, and a layer that replaces another on the
/// map (an S-98 filtered copy whose dropped features changed) shows the
/// replaced layer's tiles until its own land, rather than blanking.
/// </summary>
[Collection(RenderingOptimizationsCollection.Name)]
public sealed class TileRevealRenderTests
{
    private const int Band = 10;
    private const int ViewportDip = 256;
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);
    private static readonly RgbaColor Fill = new(40, 120, 220, 255);
    private static readonly RgbaColor Symbol = new(230, 30, 30, 255);

    [Fact]
    public void Render_ColdCell_HoldsOverlayUntilBaseAppears()
    {
        var viewport = MakeViewport();
        var layer = BindLayer(new List<PaintOp> { Square(), Point() });

        // First frame: no tile has been rasterised yet, so nothing of the cell
        // is drawn, its symbols included.
        using (var first = RenderFrame(layer, viewport))
        {
            Assert.Equal(0, Count(first, Symbol));
            Assert.Equal(0, Count(first, Fill));
        }

        using var settled = RenderSettled(layer, viewport);
        Assert.True(Count(settled, Fill) > 0);
        Assert.True(Count(settled, Symbol) > 0);
    }

    [Fact]
    public void Render_OverlayOnlyInView_DrawsOverlayAtOnce()
    {
        // No base content in view: there is no fill to wait for.
        var viewport = MakeViewport();
        var layer = BindLayer(new List<PaintOp> { Point() });

        using var first = RenderFrame(layer, viewport);

        Assert.True(Count(first, Symbol) > 0);
    }

    [Fact]
    public void Render_ReplacementLayer_ShowsReplacedLayersTilesUntilItsOwnLand()
    {
        var viewport = MakeViewport();
        var source = BindLayer(new List<PaintOp> { Square() });
        using (RenderSettled(source, viewport))
        {
        }

        var replacement = new MemoryLayer { Name = "filtered" };
        Assert.True(S100VectorTileRenderer.TryBindFilteredScene(source, replacement, _ => true, replaces: source));

        // The replacement's first frame has no tile of its own yet (its workers
        // start after the composite), so the fill comes from the replaced layer.
        using var first = RenderFrame(replacement, viewport);

        Assert.True(Count(first, Fill) > 0);
    }

    [Fact]
    public void Render_FilteredLayerWithoutPredecessor_StartsBlank()
    {
        // The control for the test above: a filtered copy that replaces nothing
        // has nothing to show on its first frame.
        var viewport = MakeViewport();
        var source = BindLayer(new List<PaintOp> { Square() });
        using (RenderSettled(source, viewport))
        {
        }

        var filtered = new MemoryLayer { Name = "filtered" };
        Assert.True(S100VectorTileRenderer.TryBindFilteredScene(source, filtered, _ => true));

        using var first = RenderFrame(filtered, viewport);

        Assert.Equal(0, Count(first, Fill));
    }

    private static long Count(SKSurface surface, RgbaColor color)
    {
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        long count = 0;
        foreach (var pixel in bitmap.Pixels)
        {
            if (pixel.Alpha > 200
                && Math.Abs(pixel.Red - color.R) < 24
                && Math.Abs(pixel.Green - color.G) < 24
                && Math.Abs(pixel.Blue - color.B) < 24)
            {
                count++;
            }
        }

        return count;
    }

    private static SKSurface RenderFrame(ILayer layer, Mapsui.Viewport viewport)
    {
        var surface = CreateSurface();
        S100VectorTileRenderer.Render(surface.Canvas, viewport, layer, new RenderService());
        return surface;
    }

    /// <summary>Renders live frames until every visible tile is resident and shown, then one more.</summary>
    private static SKSurface RenderSettled(ILayer layer, Mapsui.Viewport viewport)
    {
        var px = S100VectorTileRenderer.TilePixelSize(1f);
        var sw = Stopwatch.StartNew();
        while (true)
        {
            using (RenderFrame(layer, viewport))
            {
            }

            if (S100VectorTileRenderer.IsVisibleSettledForTest(layer, px))
            {
                return RenderFrame(layer, viewport);
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

    private static SKSurface CreateSurface()
    {
        var surface = SKSurface.Create(new SKImageInfo(ViewportDip, ViewportDip));
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

    /// <summary>A layer with no disk namespace, so the test never touches the shared warm cache.</summary>
    private static ILayer BindLayer(List<PaintOp> ops)
    {
        var layer = new MemoryLayer { Name = "cell" };
        S100VectorTileRenderer.BindScene(layer, new VectorScene(ops), productLayerSet: null, styleStateHash: null);
        return layer;
    }

    /// <summary>A filled square over the middle half of the viewport.</summary>
    private static AreaPaintOp Square()
    {
        var viewport = MakeViewport();
        var half = viewport.Resolution * ViewportDip * 0.25;
        return new AreaPaintOp
        {
            FeatureReference = "area",
            WorldShell =
            [
                (viewport.CenterX - half, viewport.CenterY - half),
                (viewport.CenterX + half, viewport.CenterY - half),
                (viewport.CenterX + half, viewport.CenterY + half),
                (viewport.CenterX - half, viewport.CenterY + half),
                (viewport.CenterX - half, viewport.CenterY - half),
            ],
            Fill = Fill,
        };
    }

    /// <summary>A symbol-less point drawn as its fallback marker, outside the square.</summary>
    private static PointPaintOp Point()
    {
        var viewport = MakeViewport();
        var offset = viewport.Resolution * ViewportDip * 0.4;
        return new PointPaintOp
        {
            FeatureReference = "point",
            World = (viewport.CenterX - offset, viewport.CenterY + offset),
            FallbackColor = Symbol,
            FallbackScale = 2,
        };
    }
}
