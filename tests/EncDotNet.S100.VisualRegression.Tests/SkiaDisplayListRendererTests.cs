using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using Mapsui.Projections;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Unit / parity tests for the headless direct-Skia vector renderer and the
/// shared S-100 Part 9 rendering core it consumes
/// (<see cref="VectorSceneBuilder"/> → <see cref="VectorScene"/> →
/// <see cref="SkiaDisplayListRenderer"/>). These validate the seam directly
/// rather than via the Mapsui visual-regression baselines, per the deliberate
/// "honest parity" scoping of the split-renderer spike.
/// </summary>
public sealed class SkiaDisplayListRendererTests
{
    private static readonly RgbaColor White = new(255, 255, 255, 255);
    private static readonly RgbaColor Black = new(0, 0, 0, 255);

    // ── Projection parity ──────────────────────────────────────────────

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(-122.3321, 47.6062)]   // Seattle
    [InlineData(151.2093, -33.8688)]   // Sydney
    [InlineData(179.9, 70.0)]          // near-antimeridian, high latitude
    [InlineData(-179.9, -60.0)]
    public void WebMercator_MatchesMapsui_WithinTightTolerance(double lon, double lat)
    {
        var (x, y) = WebMercator.FromLonLat(lon, lat);
        var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

        // EPSG:3857 eastings/northings span ±2.0036e7 m; a 1e-3 m tolerance is
        // ~12 orders of magnitude tighter than a display pixel at any usable
        // chart scale, so the reimplementation is render-equivalent to Mapsui's.
        Assert.Equal(mx, x, 3);
        Assert.Equal(my, y, 3);
    }

    // ── Scale-visibility boundary agreement ────────────────────────────

    [Fact]
    public void IsVisibleAtScale_IsInclusiveAtBothBounds()
    {
        // ScaleMaximum = most zoomed-in (smallest denom); ScaleMinimum = most
        // zoomed-out (largest denom). Visible iff ScaleMaximum ≤ denom ≤ ScaleMinimum.
        var op = MakeLine(ScaleMinimum: 50_000, ScaleMaximum: 10_000);

        Assert.False(ScaleVisibility.IsVisibleAtScale(op, 9_999));    // too zoomed in
        Assert.True(ScaleVisibility.IsVisibleAtScale(op, 10_000));    // lower bound inclusive
        Assert.True(ScaleVisibility.IsVisibleAtScale(op, 30_000));
        Assert.True(ScaleVisibility.IsVisibleAtScale(op, 50_000));    // upper bound inclusive
        Assert.False(ScaleVisibility.IsVisibleAtScale(op, 50_001));   // too zoomed out
    }

    [Fact]
    public void IsVisibleAtScale_NullBoundsAlwaysVisible()
    {
        var op = MakeLine(ScaleMinimum: null, ScaleMaximum: null);
        Assert.True(ScaleVisibility.IsVisibleAtScale(op, 1));
        Assert.True(ScaleVisibility.IsVisibleAtScale(op, 10_000_000));
    }

    [Fact]
    public void Render_OmitsOpsOutsideScaleRange()
    {
        var scene = new VectorScene([
            new AreaPaintOp
            {
                FeatureReference = "F1",
                ScaleMinimum = 10_000,   // only visible when zoomed in to ≤ 1:10000
                WorldShell = SquareAround(0.005, 0.005, 0.004),
                Fill = Black,
            },
        ]);

        // Viewport scale denominator (50_000) is more zoomed-out than the op's
        // ScaleMinimum (10_000) upper bound → the area must be culled.
        using var bitmap = Render(scene, MakeViewport(denom: 50_000));
        Assert.True(IsBlank(bitmap, White), "Op outside its scale range should not be drawn.");
    }

    // ── Resolution-independent sizing (the IR unit contract) ───────────

    [Fact]
    public void StrokeWidth_IsConstantAcrossZoomLevels()
    {
        const double widthPx = 6.0;

        // A horizontal line across the middle of the viewport, in world space.
        var wide = RenderLineAndMeasureCentreWidth(widthPx, geoSpan: 0.02);
        var zoomed = RenderLineAndMeasureCentreWidth(widthPx, geoSpan: 0.002);

        // Stroke width is realised in display pixels per the IR contract, so a
        // 10× zoom must NOT change the on-screen stroke thickness.
        Assert.InRange(wide, widthPx - 1.5, widthPx + 1.5);
        Assert.InRange(zoomed, widthPx - 1.5, widthPx + 1.5);
        Assert.InRange(Math.Abs(wide - zoomed), 0, 1);
    }

    // ── End-to-end smoke: all four op kinds produce output ─────────────

    [Fact]
    public void Render_PointLineAreaText_ProducesOutput()
    {
        var scene = new VectorScene([
            new AreaPaintOp
            {
                FeatureReference = "area",
                WorldShell = SquareAround(0.005, 0.005, 0.003),
                Fill = new RgbaColor(200, 220, 255, 255),
            },
            new LinePaintOp
            {
                FeatureReference = "line",
                World = [Project(0.001, 0.005), Project(0.009, 0.005)],
                Color = new RgbaColor(0, 128, 0, 255),
                WidthPx = 3.0,
            },
            new PointPaintOp
            {
                FeatureReference = "point",
                World = Project(0.005, 0.005),
                FallbackColor = new RgbaColor(255, 0, 0, 255),
                FallbackScale = 0.5,
            },
            new TextPaintOp
            {
                FeatureReference = "text",
                World = Project(0.005, 0.008),
                Text = "X",
                FontSizePx = 24,
                ForeColor = Black,
            },
        ]);

        using var bitmap = Render(scene, MakeViewport(denom: 25_000));
        Assert.False(IsBlank(bitmap, White), "Scene with four op kinds rendered nothing.");

        // The red fallback dot sits on the projected anchor at the viewport
        // centre, validating WorldToScreen placement.
        var centre = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.True(centre.Red > 150 && centre.Green < 100 && centre.Blue < 100,
            $"Expected a red dot at the centre, got {centre}.");
    }

    [Fact]
    public void Render_SolidArea_FillsInteriorWithFillColour()
    {
        var fill = new RgbaColor(10, 80, 200, 255);
        var scene = new VectorScene([
            new AreaPaintOp
            {
                FeatureReference = "area",
                WorldShell = SquareAround(0.005, 0.005, 0.004),
                Fill = fill,
            },
        ]);

        using var bitmap = Render(scene, MakeViewport(denom: 25_000));
        var centre = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.InRange((int)centre.Blue, 170, 230);
        Assert.True(centre.Blue > centre.Red && centre.Blue > centre.Green);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static double RenderLineAndMeasureCentreWidth(double widthPx, double geoSpan)
    {
        double midLat = geoSpan / 2.0;
        var scene = new VectorScene([
            new LinePaintOp
            {
                FeatureReference = "line",
                World = [Project(0.0, midLat), Project(geoSpan, midLat)],
                Color = Black,
                WidthPx = widthPx,
            },
        ]);

        var viewport = new Viewport
        {
            MinLongitude = 0.0,
            MaxLongitude = geoSpan,
            MinLatitude = 0.0,
            MaxLatitude = geoSpan,
            WidthPixels = 200,
            HeightPixels = 200,
            ScaleDenominator = 25_000,
        };

        using var bitmap = Render(scene, viewport);

        // Count opaque-black pixels in the centre column; the round-capped
        // horizontal stroke is widthPx tall there.
        int col = bitmap.Width / 2;
        int run = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            var p = bitmap.GetPixel(col, y);
            if (p.Red < 80 && p.Green < 80 && p.Blue < 80 && p.Alpha > 128)
                run++;
        }
        return run;
    }

    private static SKBitmap Render(VectorScene scene, Viewport viewport)
    {
        var renderer = new SkiaDisplayListRenderer { Background = White };
        return renderer.Render(scene, viewport);
    }

    private static Viewport MakeViewport(double denom) => new()
    {
        MinLongitude = 0.0,
        MaxLongitude = 0.01,
        MinLatitude = 0.0,
        MaxLatitude = 0.01,
        WidthPixels = 200,
        HeightPixels = 200,
        ScaleDenominator = denom,
    };

    private static LinePaintOp MakeLine(double? ScaleMinimum, double? ScaleMaximum) => new()
    {
        FeatureReference = "F",
        World = [Project(0.001, 0.005), Project(0.009, 0.005)],
        Color = Black,
        WidthPx = 2.0,
        ScaleMinimum = ScaleMinimum,
        ScaleMaximum = ScaleMaximum,
    };

    private static (double X, double Y) Project(double lon, double lat) =>
        WebMercator.FromLonLat(lon, lat);

    private static IReadOnlyList<(double X, double Y)> SquareAround(
        double lon, double lat, double half)
    {
        return
        [
            Project(lon - half, lat - half),
            Project(lon + half, lat - half),
            Project(lon + half, lat + half),
            Project(lon - half, lat + half),
        ];
    }

    private static bool IsBlank(SKBitmap bitmap, RgbaColor background)
    {
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.Red != background.R || p.Green != background.G || p.Blue != background.B)
                return false;
        }
        return true;
    }
}
