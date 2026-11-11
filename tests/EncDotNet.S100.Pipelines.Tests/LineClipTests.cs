using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;
using CoreViewport = EncDotNet.S100.Pipelines.Viewport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins that <see cref="SkiaDisplayListRenderer"/> clips a line reaching far
/// past the view before stroking it, without moving its dashes: a dash effect
/// walks the whole path, so a long route leg zoomed in used to cost its full
/// projected length every frame.
/// </summary>
public sealed class LineClipTests
{
    private static readonly SKRect Clip = new(0, 0, 100, 100);

    [Fact]
    public void ClipSegment_Inside_KeepsWholeSegment()
    {
        Assert.True(SkiaDisplayListRenderer.ClipSegment(new SKPoint(10, 10), 50, 30, Clip, out var t0, out var t1));
        Assert.Equal(0, t0);
        Assert.Equal(1, t1);
    }

    [Fact]
    public void ClipSegment_Crossing_KeepsPartInside()
    {
        // From x = -100 to x = 200 along y = 50: inside for x in 0…100.
        Assert.True(SkiaDisplayListRenderer.ClipSegment(new SKPoint(-100, 50), 300, 0, Clip, out var t0, out var t1));
        Assert.Equal(1.0 / 3, t0, 9);
        Assert.Equal(2.0 / 3, t1, 9);
    }

    [Fact]
    public void ClipSegment_Outside_IsRejected()
    {
        Assert.False(SkiaDisplayListRenderer.ClipSegment(new SKPoint(-100, 150), 300, 0, Clip, out _, out _));
        Assert.False(SkiaDisplayListRenderer.ClipSegment(new SKPoint(-100, -50), 50, 300, Clip, out _, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_LineFarPastView_MatchesTheWholeLineDrawnInALargerView(bool dashed)
    {
        // A zig-zag 80 km long at 10 m/px. The small view sees 200 px of its
        // middle and clips it; the wide view holds all of it and strokes it
        // whole. Their overlap must match, dashes included.
        const double resolution = 10;
        const int smallPx = 200;
        const int wideWidthPx = 8400;
        var points = new List<(double X, double Y)>();
        for (var i = 0; i <= 16; i++)
        {
            points.Add((-40_000 + i * 5_000, i % 2 == 0 ? -600 : 600));
        }

        var scene = new VectorScene(new List<PaintOp>
        {
            new LinePaintOp
            {
                FeatureReference = "leg",
                World = points,
                Color = new RgbaColor(220, 60, 40, 255),
                WidthPx = 2,
                DashArrayPx = dashed ? [7f, 5f] : null,
            },
        });

        var renderer = new SkiaDisplayListRenderer { Background = RgbaColor.Transparent, EnableSeamWrap = false };
        using var small = renderer.Render(scene, View(0, 0, smallPx, smallPx, resolution));
        using var wide = renderer.Render(scene, View(0, 0, wideWidthPx, smallPx, resolution));

        var offsetX = (wideWidthPx - smallPx) / 2;
        int ink = 0, mismatched = 0;
        for (var y = 0; y < smallPx; y++)
        {
            for (var x = 0; x < smallPx; x++)
            {
                var a = small.GetPixel(x, y);
                var b = wide.GetPixel(x + offsetX, y);
                if (a.Alpha > 0 || b.Alpha > 0)
                {
                    ink++;
                }

                if (Math.Abs(a.Alpha - b.Alpha) > 64)
                {
                    mismatched++;
                }
            }
        }

        Assert.True(ink > 200, $"expected the line to cross the view, ink={ink}");
        Assert.True(mismatched <= ink / 50, $"{mismatched} of {ink} ink pixels differ");
    }

    private static CoreViewport View(double centerX, double centerY, int widthPx, int heightPx, double resolution)
    {
        var halfW = widthPx * resolution / 2;
        var halfH = heightPx * resolution / 2;
        var (minLon, minLat) = WebMercator.ToLonLat(centerX - halfW, centerY - halfH, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(centerX + halfW, centerY + halfH, clampLatitude: false);
        return new CoreViewport
        {
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            WidthPixels = widthPx,
            HeightPixels = heightPx,
            ScaleDenominator = 50_000,
        };
    }
}
