using System.Diagnostics;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Chart tiles (map rendering slice 8): one raster per slot over every layer
/// of a <see cref="ChartStack"/>, keyed by the slot's stack slice, with the
/// finer-coverage clip, opacity and S-98 suppression applied while
/// rasterising, and the old tile kept on screen while a changed slot
/// re-rasters.
/// </summary>
[Collection(ChartSessionOptionsCollection.Name)]
public sealed class ChartTileTests
{
    private const int Band = 10;
    private const int ViewportDip = 256;
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);
    private static readonly RgbaColor Blue = new(40, 120, 220, 255);
    private static readonly RgbaColor Green = new(30, 200, 60, 255);
    private static readonly RgbaColor Red = new(230, 30, 30, 255);

    [Fact]
    public void OneLayerStack_DrawsTheSamePixelsAsThePerLayerTile()
    {
        var view = MakeView();
        var cell = BindSource([Square(Blue, 0.25, "a")]);
        using var legacy = RenderSettled(cell, view);

        var chart = new object();
        Publish(chart, 1, new ChartStackEntry(cell));
        using var composite = RenderSettled(chart, view);

        Assert.Equal(Pixels(legacy), Pixels(composite));
    }

    [Fact]
    public void FinerCoverage_ClipsTheCoarserLayerInsideTheRaster()
    {
        var view = MakeView();
        var coarse = BindSource([Square(Blue, 0.5, "coarse")]);
        var fine = BindSource([Square(Green, 0.2, "fine")]);
        var fineCoverage = Box(view, 0.2);

        var chart = new object();
        Publish(
            chart,
            1,
            new ChartStackEntry(coarse) { Finer = [new FinerCoverage(fineCoverage, double.MaxValue)] },
            new ChartStackEntry(fine));
        using var frame = RenderSettled(chart, view);

        // The centre shows the finer cell; the coarser one only outside it.
        Assert.Equal(Green, PixelAt(frame, ViewportDip / 2, ViewportDip / 2));
        Assert.Equal(Blue, PixelAt(frame, ViewportDip / 2, ViewportDip / 2 + (int)(ViewportDip * 0.35)));
    }

    [Fact]
    public void Opacity_IsAStackSetting()
    {
        var view = MakeView();
        var cell = BindSource([Square(Blue, 0.5, "a")]);
        var chart = new object();
        Publish(chart, 1, new ChartStackEntry(cell) { Opacity = 0.5 });

        using var frame = RenderSettled(chart, view);

        var pixel = PixelAt(frame, ViewportDip / 2, ViewportDip / 2);
        Assert.InRange(pixel.A, 120, 135);
    }

    [Fact]
    public void SuppressedFeatures_AreLeftOutOfTheRaster()
    {
        var view = MakeView();
        var cell = BindSource([Square(Blue, 0.5, "keep"), Square(Red, 0.2, "drop")]);
        var chart = new object();
        Publish(chart, 1, new ChartStackEntry(cell) { SuppressedFeatures = new HashSet<string> { "drop" } });

        using var frame = RenderSettled(chart, view);

        Assert.Equal(Blue, PixelAt(frame, ViewportDip / 2, ViewportDip / 2));
    }

    [Fact]
    public void ChangedSlot_KeepsItsOldTileUntilTheNewOneLands()
    {
        var view = MakeView();
        var first = BindSource([Square(Blue, 0.5, "first")]);
        var chart = new object();
        Publish(chart, 1, new ChartStackEntry(first));
        using (RenderSettled(chart, view))
        {
        }

        var before = S100VectorTileRenderer.CurrentVisibleForTest(chart);

        var second = BindSource([Square(Red, 0.1, "second")]);
        Publish(chart, 2, new ChartStackEntry(first), new ChartStackEntry(second));

        // The new stack changes the slot's slice; its first frame has no tile
        // of its own yet, so it shows the old one rather than nothing.
        using (var frame = RenderFrame(chart, view))
        {
            Assert.Equal(Blue, PixelAt(frame, ViewportDip / 2, ViewportDip / 2 + (int)(ViewportDip * 0.2)));
        }

        var after = S100VectorTileRenderer.CurrentVisibleForTest(chart);
        Assert.NotEqual(before.Select(k => k.Slice).Order(), after.Select(k => k.Slice).Order());

        using var settled = RenderSettled(chart, view);
        Assert.Equal(Red, PixelAt(settled, ViewportDip / 2, ViewportDip / 2));
    }

    [Fact]
    public void UntouchedSlot_KeepsItsKeyWhenAnotherCellLands()
    {
        var view = MakeView();
        var first = BindSource([Square(Blue, 0.5, "first")]);
        var chart = new object();
        Publish(chart, 1, new ChartStackEntry(first));
        using (RenderSettled(chart, view))
        {
        }

        var before = S100VectorTileRenderer.CurrentVisibleForTest(chart).ToHashSet();

        // A cell far away from the view touches none of its slots.
        var far = BindSource([SquareAt(view.CenterX + 50 * view.Resolution * ViewportDip, view.CenterY, view.Resolution * 10, Red, "far")]);
        Publish(chart, 2, new ChartStackEntry(first), new ChartStackEntry(far));
        using (RenderFrame(chart, view))
        {
        }

        Assert.Equal(before, S100VectorTileRenderer.CurrentVisibleForTest(chart).ToHashSet());
    }

    [Fact]
    public void LandingLayersSymbols_WaitForItsBase()
    {
        var view = MakeView();
        var first = BindSource([Square(Blue, 0.5, "first")]);
        var chart = new object();
        Publish(chart, 1, new ChartStackEntry(first));
        using (RenderSettled(chart, view))
        {
        }

        var second = BindSource([Square(Green, 0.1, "second"), Point(Red)]);
        Publish(chart, 2, new ChartStackEntry(first), new ChartStackEntry(second));

        // The slot shows its old tile, which doesn't hold the new cell yet,
        // so the new cell's symbol waits with its fills.
        using (var frame = RenderFrame(chart, view))
        {
            Assert.Equal(0, Count(frame, Red));
        }

        using var settled = RenderSettled(chart, view);
        Assert.True(Count(settled, Red) > 0);
        Assert.True(Count(settled, Green) > 0);
    }

    [Fact]
    public void SameStackInAnotherSession_HasTheSameSlices()
    {
        var view = MakeView();
        var cell = new object();
        S100VectorTileRenderer.BindScene(cell, new VectorScene([Square(Blue, 0.5, "a")]), "chart-tile-tests", "style");

        var one = new object();
        var two = new object();
        new ChartSession(new ChartSessionOptions { TileDiskCacheEnabled = false }).Publish(one, new ChartStack(1, [new ChartStackEntry(cell)]));
        new ChartSession(new ChartSessionOptions { TileDiskCacheEnabled = false }).Publish(two, new ChartStack(7, [new ChartStackEntry(cell)]));
        using (RenderFrame(one, view))
        using (RenderFrame(two, view))
        {
        }

        var a = S100VectorTileRenderer.CurrentVisibleForTest(one);
        Assert.NotEmpty(a);
        Assert.Equal(a.ToHashSet(), S100VectorTileRenderer.CurrentVisibleForTest(two).ToHashSet());
        Assert.All(a, key => Assert.NotEqual(UInt128.Zero, key.Slice));
    }

    // -----------------------------------------------------------------

    private static void Publish(object chart, long version, params ChartStackEntry[] entries) =>
        new ChartSession(new ChartSessionOptions { TileDiskCacheEnabled = false })
            .Publish(chart, new ChartStack(version, entries));

    /// <summary>A source with no disk namespace, so nothing touches the shared warm cache.</summary>
    private static object BindSource(List<PaintOp> ops)
    {
        var source = new object();
        S100VectorTileRenderer.BindScene(source, new VectorScene(ops), productLayerSet: null, styleStateHash: null);
        return source;
    }

    private static SKSurface RenderFrame(object owner, ViewState view)
    {
        var surface = SKSurface.Create(new SKImageInfo(ViewportDip, ViewportDip));
        surface.Canvas.Clear(SKColors.Transparent);
        S100VectorTileRenderer.Render(surface.Canvas, view, new LayerFrame(owner, "chart", 1.0, null));
        return surface;
    }

    private static SKSurface RenderSettled(object owner, ViewState view)
    {
        var px = S100VectorTileRenderer.TilePixelSize(1f);
        var sw = Stopwatch.StartNew();
        while (true)
        {
            using (RenderFrame(owner, view))
            {
            }

            if (S100VectorTileRenderer.IsVisibleSettledForTest(owner, px))
            {
                return RenderFrame(owner, view);
            }

            if (sw.Elapsed > SettleTimeout)
            {
                Assert.Fail("Tiles did not settle: " + S100VectorTileRenderer.DescribeTileWorkForTest(owner));
            }

            Thread.Sleep(20);
        }
    }

    private static ViewState MakeView()
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(new TileKey(Band, 512, 512));
        return new ViewState(
            (minX + maxX) * 0.5,
            (minY + maxY) * 0.5,
            TileGrid.ResolutionForBand(Band),
            0,
            ViewportDip,
            ViewportDip);
    }

    private static Polygon Box(ViewState view, double fraction)
    {
        var half = view.Resolution * ViewportDip * fraction;
        return new GeometryFactory().CreatePolygon(
        [
            new Coordinate(view.CenterX - half, view.CenterY - half),
            new Coordinate(view.CenterX + half, view.CenterY - half),
            new Coordinate(view.CenterX + half, view.CenterY + half),
            new Coordinate(view.CenterX - half, view.CenterY + half),
            new Coordinate(view.CenterX - half, view.CenterY - half),
        ]);
    }

    /// <summary>A filled square of half-size <paramref name="fraction"/> of the view, centred.</summary>
    private static AreaPaintOp Square(RgbaColor fill, double fraction, string reference)
    {
        var view = MakeView();
        return SquareAt(view.CenterX, view.CenterY, view.Resolution * ViewportDip * fraction, fill, reference);
    }

    private static AreaPaintOp SquareAt(double cx, double cy, double half, RgbaColor fill, string reference) => new()
    {
        FeatureReference = reference,
        WorldShell =
        [
            (cx - half, cy - half),
            (cx + half, cy - half),
            (cx + half, cy + half),
            (cx - half, cy + half),
            (cx - half, cy - half),
        ],
        Fill = fill,
    };

    private static PointPaintOp Point(RgbaColor color)
    {
        var view = MakeView();
        var offset = view.Resolution * ViewportDip * 0.4;
        return new PointPaintOp
        {
            FeatureReference = "point",
            World = (view.CenterX - offset, view.CenterY + offset),
            FallbackColor = color,
            FallbackScale = 2,
        };
    }

    private static SKColor[] Pixels(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Pixels;
    }

    private static RgbaColor PixelAt(SKSurface surface, int x, int y)
    {
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        var p = bitmap.GetPixel(x, y);
        return new RgbaColor(p.Red, p.Green, p.Blue, p.Alpha);
    }

    private static long Count(SKSurface surface, RgbaColor color)
    {
        long count = 0;
        foreach (var pixel in Pixels(surface))
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
}
