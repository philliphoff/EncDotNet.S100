using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for the live label plane's text rendering: upright-under-rotation
/// (anchor rotates with the chart, glyphs stay horizontal) and per-run font
/// fallback (no <c>.notdef</c> "tofu" boxes for codepoints the primary face
/// lacks). Pure, machine-independent.
/// </summary>
public class LabelPlaneTextTests
{
    private static TextPaintOp Text(double lon, double lat, string text) =>
        new()
        {
            FeatureReference = "label",
            World = WebMercator.FromLonLat(lon, lat),
            Text = text,
            FontSizePx = 16,
            ForeColor = new RgbaColor(0, 0, 0, 255),
            HorizontalAlignment = TextHorizontalAlignment.Center,
            VerticalAlignment = TextVerticalAlignment.Center,
        };

    private static Viewport Centred(double lon, double lat, double span, int px) =>
        new()
        {
            MinLongitude = lon - span / 2,
            MaxLongitude = lon + span / 2,
            MinLatitude = lat - span / 2,
            MaxLatitude = lat + span / 2,
            WidthPixels = px,
            HeightPixels = px,
            ScaleDenominator = 50000,
        };

    private static (int Width, int Height) RenderAndMeasure(VectorScene scene, Viewport viewport, OverlayDrawOptions options, bool rotateCanvas, float rotationDeg)
    {
        var renderer = new SkiaDisplayListRenderer { HonorScaleVisibility = false };
        using var bitmap = new SKBitmap(viewport.WidthPixels, viewport.HeightPixels, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            if (rotateCanvas)
                canvas.RotateDegrees(rotationDeg, viewport.WidthPixels / 2f, viewport.HeightPixels / 2f);
            renderer.RenderOnto(canvas, scene, viewport, options);
            canvas.Flush();
        }
        return Measure(bitmap);
    }

    private static (int Width, int Height) Measure(SKBitmap bitmap)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                    continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }
        return maxX < 0 ? (0, 0) : (maxX - minX + 1, maxY - minY + 1);
    }

    [Fact]
    public void RenderOnto_AnchorRotation_KeepsTextUpright()
    {
        // A wide word anchored at the viewport centre. Under a 90° viewport
        // rotation, the upright label plane rotates the *anchor* (a no-op at the
        // centre) but draws glyphs horizontally, so the ink stays wider than it
        // is tall — proving the text did not rake with the chart.
        const int px = 400;
        var scene = new VectorScene(new List<PaintOp> { Text(10.0, 0.0, "ABCDEFGHIJ") });
        var viewport = Centred(10.0, 0.0, 0.2, px);

        var upright = RenderAndMeasure(
            scene, viewport,
            new OverlayDrawOptions
            {
                TextAnchorRotationDegrees = 90,
                ScreenCenterX = px / 2f,
                ScreenCenterY = px / 2f,
                DrawPoints = false,
            },
            rotateCanvas: false, rotationDeg: 0);

        // Control: the *old* behaviour (rotate the whole canvas) rakes the
        // glyphs, making the same word taller than it is wide.
        var raked = RenderAndMeasure(
            scene, viewport,
            new OverlayDrawOptions { DrawPoints = false },
            rotateCanvas: true, rotationDeg: 90);

        Assert.True(upright.Width > upright.Height,
            $"upright label should be wider than tall, got {upright.Width}x{upright.Height}");
        Assert.True(raked.Height > raked.Width,
            $"canvas-rotated label should be taller than wide, got {raked.Width}x{raked.Height}");
    }

    [Fact]
    public void RenderOnto_NorthUp_DrawsText()
    {
        const int px = 400;
        var scene = new VectorScene(new List<PaintOp> { Text(10.0, 0.0, "ABCDEFGHIJ") });
        var viewport = Centred(10.0, 0.0, 0.2, px);

        var bounds = RenderAndMeasure(scene, viewport, new OverlayDrawOptions { DrawPoints = false }, false, 0);

        Assert.True(bounds.Width > bounds.Height, $"north-up wide label, got {bounds.Width}x{bounds.Height}");
    }

    [Fact]
    public void SegmentRuns_AsciiOnly_IsSingleRun()
    {
        // Primary face has every glyph → one run, no fallback.
        var runs = SkiaDisplayListRenderer.SegmentRuns("12.3", _ => null);

        Assert.Single(runs);
        Assert.Equal((0, 4, (object?)null), runs[0]);
    }

    [Fact]
    public void SegmentRuns_SplitsMissingGlyphRun()
    {
        // The degree sign is resolved to a fallback face; ASCII stays on primary.
        var deg = new object();
        var runs = SkiaDisplayListRenderer.SegmentRuns("12\u00B034", cp => cp == 0x00B0 ? deg : null);

        Assert.Equal(3, runs.Count);
        Assert.Equal((0, 2, (object?)null), runs[0]);
        Assert.Equal((2, 1, (object?)deg), runs[1]);
        Assert.Equal((3, 2, (object?)null), runs[2]);
    }

    [Fact]
    public void SegmentRuns_GroupsConsecutiveSameFallbackFace()
    {
        var face = new object();
        var runs = SkiaDisplayListRenderer.SegmentRuns("\u00B0\u00B0A", cp => cp == 0x00B0 ? face : null);

        Assert.Equal(2, runs.Count);
        Assert.Equal((0, 2, (object?)face), runs[0]);
        Assert.Equal((2, 1, (object?)null), runs[1]);
    }

    [Fact]
    public void SegmentRuns_HandlesSurrogatePairAsOneCodepoint()
    {
        // U+1F600 (😀) is a surrogate pair (length 2) but a single codepoint.
        var emoji = new object();
        var runs = SkiaDisplayListRenderer.SegmentRuns("A\U0001F600B", cp => cp == 0x1F600 ? emoji : null);

        Assert.Equal(3, runs.Count);
        Assert.Equal((0, 1, (object?)null), runs[0]);
        Assert.Equal((1, 2, (object?)emoji), runs[1]);
        Assert.Equal((3, 1, (object?)null), runs[2]);
    }
}
