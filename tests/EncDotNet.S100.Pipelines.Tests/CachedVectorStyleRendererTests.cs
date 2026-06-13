using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Validates the translation-invariant vector path cache and resolution-aware
/// line simplification in <see cref="CachedVectorStyleRenderer"/>: it must
/// build a geometry's <see cref="SKPath"/> once per (feature, resolution) and
/// re-use it across pans, render byte-for-byte like Mapsui's
/// <see cref="VectorStyleRenderer"/> when simplification is disabled, drop only
/// sub-pixel vertices when it is enabled (preserving endpoints), and delegate
/// geometries it does not fast-path.
/// </summary>
public class CachedVectorStyleRendererTests
{
    private static readonly RenderService Service = new();

    // World coordinates roughly span [0,1000] in both axes so a 1.0 resolution
    // maps them onto a 1000 px canvas with the centre at (500, 500).
    private const int CanvasSize = 256;

    private static GeometryFeature MakeLineFeature(IEnumerable<Coordinate> coords, long id)
    {
        // Each GeometryFeature is auto-assigned a stable Id; reusing the same
        // instance across renders keeps the cache key constant (the id arg is
        // only for test readability).
        _ = id;
        var feature = new GeometryFeature(new LineString(coords.ToArray()));
        feature.Styles.Add(new VectorStyle { Line = new Pen { Color = Color.Black, Width = 2.0 }, Outline = null, Fill = null });
        return feature;
    }

    private static Mapsui.Viewport ViewportFor(double centerX, double centerY, double resolution) =>
        new(centerX, centerY, resolution, rotation: 0, width: CanvasSize, height: CanvasSize);

    private static byte[] RenderToPng(Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(CanvasSize, CanvasSize, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        draw(surface.Canvas);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static int OpaqueDarkPixels(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.Alpha > 128 && p.Red < 80 && p.Green < 80 && p.Blue < 80)
            {
                count++;
            }
        }
        return count;
    }

    [Fact]
    public void Pan_AtConstantResolution_BuildsPathOnce()
    {
        var renderer = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var feature = MakeLineFeature(
            new[] { new Coordinate(100, 100), new Coordinate(900, 900) }, id: 1);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };

        RenderToPng(canvas => renderer.Draw(canvas, ViewportFor(500, 500, 1.0), layer, feature, style, Service, 0));
        RenderToPng(canvas => renderer.Draw(canvas, ViewportFor(540, 500, 1.0), layer, feature, style, Service, 0));
        RenderToPng(canvas => renderer.Draw(canvas, ViewportFor(580, 540, 1.0), layer, feature, style, Service, 0));

        // Three pans at one resolution -> exactly one cached path.
        Assert.Equal(1, renderer.CachedPathCount);

        // A zoom (new resolution) adds a second entry.
        RenderToPng(canvas => renderer.Draw(canvas, ViewportFor(500, 500, 2.0), layer, feature, style, Service, 0));
        Assert.Equal(2, renderer.CachedPathCount);
    }

    [Fact]
    public void DisabledSimplification_MatchesMapsuiOutput()
    {
        var feature = MakeLineFeature(
            new[]
            {
                new Coordinate(100, 200), new Coordinate(300, 700),
                new Coordinate(550, 250), new Coordinate(820, 760),
            },
            id: 2);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        var viewport = ViewportFor(500, 500, 3.0);

        var cached = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var mapsui = new VectorStyleRenderer();

        var cachedPng = RenderToPng(c => cached.Draw(c, viewport, layer, feature, style, Service, 0));
        var mapsuiPng = RenderToPng(c => mapsui.Draw(c, viewport, layer, feature, style, Service, 0));

        Assert.Equal(mapsuiPng, cachedPng);
    }

    [Fact]
    public void Simplification_PreservesEndpoints()
    {
        // A line whose interior vertices are all sub-pixel apart at this
        // resolution; only the endpoints carry geometric meaning.
        var coords = new List<Coordinate> { new(100, 500) };
        for (var i = 1; i < 400; i++)
        {
            coords.Add(new Coordinate(100 + i * 0.05, 500 + ((i % 2) * 0.05)));
        }
        coords.Add(new Coordinate(900, 500));

        var feature = MakeLineFeature(coords, id: 3);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        var viewport = ViewportFor(500, 500, 1.0);

        var simplified = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0.6);
        var exact = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);

        using var simplifiedBmp = SKBitmap.Decode(
            RenderToPng(c => simplified.Draw(c, viewport, layer, feature, style, Service, 0)));
        using var exactBmp = SKBitmap.Decode(
            RenderToPng(c => exact.Draw(c, viewport, layer, feature, style, Service, 0)));

        // Both must draw the same horizontal stroke (endpoints preserved): the
        // dark-pixel counts should be within a few percent of each other.
        var s = OpaqueDarkPixels(simplifiedBmp);
        var e = OpaqueDarkPixels(exactBmp);
        Assert.True(s > 0 && e > 0, $"expected a visible stroke in both (simplified={s}, exact={e})");
        Assert.True(Math.Abs(s - e) <= e * 0.1, $"stroke coverage differs too much: simplified={s}, exact={e}");
    }

    [Fact]
    public void Point_IsDelegatedToInnerRenderer()
    {
        var inner = new CountingRenderer();
        var renderer = new CachedVectorStyleRenderer(inner);
        var feature = new GeometryFeature(new Point(500, 500));
        var style = new SymbolStyle();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };

        RenderToPng(c => renderer.Draw(c, ViewportFor(500, 500, 1.0), layer, feature, style, Service, 0));

        Assert.Equal(1, inner.DrawCalls);
        Assert.Equal(0, renderer.CachedPathCount);
    }

    [Fact]
    public void Line_IsFastPathed_NotDelegated()
    {
        var inner = new CountingRenderer();
        var renderer = new CachedVectorStyleRenderer(inner, simplifyTolerancePx: 0);
        var feature = MakeLineFeature(
            new[] { new Coordinate(100, 100), new Coordinate(900, 900) }, id: 4);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };

        var handled = false;
        RenderToPng(c => handled = renderer.Draw(c, ViewportFor(500, 500, 1.0), layer, feature, style, Service, 0));

        Assert.True(handled);
        Assert.Equal(0, inner.DrawCalls);
        Assert.Equal(1, renderer.CachedPathCount);
    }

    private sealed class CountingRenderer : ISkiaStyleRenderer
    {
        public int DrawCalls { get; private set; }

        public bool Draw(SKCanvas canvas, Mapsui.Viewport viewport, Mapsui.Layers.ILayer layer,
            Mapsui.IFeature feature, Mapsui.Styles.IStyle style, Mapsui.Rendering.RenderService renderService, long iteration)
        {
            DrawCalls++;
            return true;
        }
    }
}
