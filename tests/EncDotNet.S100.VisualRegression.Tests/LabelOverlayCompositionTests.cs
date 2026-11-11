using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Integrated, machine-independent composition tests for the tiled "B"
/// subsystem's live label plane. These exercise the exact pieces
/// <c>S100VectorTileRenderer.DrawOverlay</c> composes for the north-up case —
/// <see cref="LabelDeclutterer"/> to pick survivors, then
/// <see cref="SkiaDisplayListRenderer.RenderOnto(SKCanvas, VectorScene, Viewport, OverlayDrawOptions)"/>
/// with the suppressed set — and assert the resulting pixels show decluttered
/// labels, symbols-on-top draw order, and labels rendered alongside symbols.
/// They establish a labels+symbols fidelity check for the Label-plane work
/// without committing a binary golden image (the full real-cell golden-image set
/// is a separate #347 item).
/// </summary>
public sealed class LabelOverlayCompositionTests
{
    private static readonly RgbaColor Black = new(0, 0, 0, 255);
    private static readonly RgbaColor Red = new(220, 20, 60, 255);

    private static TextPaintOp Label(string id, double lon, double lat, string text = "HARBOUR") =>
        new()
        {
            FeatureReference = id,
            World = WebMercator.FromLonLat(lon, lat),
            Text = text,
            FontSizePx = 14,
            ForeColor = Black,
            HorizontalAlignment = TextHorizontalAlignment.Center,
            VerticalAlignment = TextVerticalAlignment.Center,
        };

    private static PointPaintOp Symbol(string id, double lon, double lat) =>
        new()
        {
            FeatureReference = id,
            World = WebMercator.FromLonLat(lon, lat),
            FallbackColor = Red,
            FallbackScale = 1.5,
        };

    private const int Px = 512;

    private static Viewport Viewport(double lon, double lat, double span = 0.4) =>
        new()
        {
            MinLongitude = lon - span / 2,
            MaxLongitude = lon + span / 2,
            MinLatitude = lat - span / 2,
            MaxLatitude = lat + span / 2,
            WidthPixels = Px,
            HeightPixels = Px,
            ScaleDenominator = 50000,
        };

    private static (int Black, int Red) RenderOverlay(VectorScene scene, Viewport viewport, bool declutter)
    {
        var cull = new SKRect(-256, -256, Px + 256, Px + 256);
        IReadOnlySet<TextPaintOp>? suppressed = declutter
            ? new LabelDeclutterer().Declutter(scene, viewport, cull, honorScaleVisibility: false, 0, Px / 2f, Px / 2f)
            : null;

        var renderer = new SkiaDisplayListRenderer { HonorScaleVisibility = false };
        using var bitmap = new SKBitmap(Px, Px, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            renderer.RenderOnto(canvas, scene, viewport,
                new OverlayDrawOptions { PointCullBounds = cull, SuppressedText = suppressed });
            canvas.Flush();
        }

        int black = 0, red = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red < 80 && p.Green < 80 && p.Blue < 80)
                    black++;
                else if (p.Red > 150 && p.Green < 100 && p.Blue < 100)
                    red++;
            }
        }
        return (black, red);
    }

    [Fact]
    public void DeclutteredOverlay_DrawsFewerLabelPixelsThanRaw()
    {
        // Three labels stacked at one anchor (overlapping) plus one clear label.
        // Decluttering keeps the highest-priority stacked label + the clear one;
        // the raw pass draws all four, so it has strictly more black ink.
        var scene = new VectorScene(new List<PaintOp>
        {
            Label("a", 10.0, 0.0),
            Label("b", 10.0, 0.0),
            Label("c", 10.0, 0.0),
            Label("far", 10.15, 0.0),
        });
        var viewport = Viewport(10.0, 0.0);

        var raw = RenderOverlay(scene, viewport, declutter: false);
        var declut = RenderOverlay(scene, viewport, declutter: true);

        Assert.True(declut.Black > 0, "decluttered overlay drew no labels");
        Assert.True(declut.Black < raw.Black,
            $"declutter should remove overlapping label ink: raw={raw.Black}, declut={declut.Black}");
    }

    [Fact]
    public void Overlay_SymbolAndSeparateLabel_BothRendered()
    {
        // A symbol and a label at different anchors: both survive declutter and
        // both appear (red symbol + black label).
        var scene = new VectorScene(new List<PaintOp>
        {
            Symbol("sym", 9.9, 0.0),
            Label("name", 10.1, 0.0),
        });
        var viewport = Viewport(10.0, 0.0);

        var (black, red) = RenderOverlay(scene, viewport, declutter: true);

        Assert.True(red > 0, "symbol did not render");
        Assert.True(black > 0, "separate label was wrongly suppressed");
    }

    [Fact]
    public void Overlay_LabelOverSymbol_BothRendered()
    {
        // A label sharing the symbol's anchor is NOT suppressed: point symbols
        // always draw but never displace a label (parity with the Mapsui "A"
        // arm; issue #347). Both the symbol's red pixels and the label's black
        // ink are present.
        var scene = new VectorScene(new List<PaintOp>
        {
            Symbol("sym", 10.0, 0.0),
            Label("name", 10.0, 0.0),
        });
        var viewport = Viewport(10.0, 0.0);

        var (black, red) = RenderOverlay(scene, viewport, declutter: true);

        Assert.True(red > 0, "symbol did not render");
        Assert.True(black > 0, $"label over symbol should survive, but {black} label pixels remain");
    }
}
