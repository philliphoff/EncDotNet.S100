using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
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
    public void Render_ResolvedSymbol_DrawsAtNaturalPixelSize_NotMmRescaled()
    {
        // Svg.Skia rasterises a symbol's millimetre dimensions to pixels at
        // 96 DPI, so a 3.98 mm × 5.35 mm symbol has a 15 × 20 px CullRect. The
        // backend must draw it at that natural pixel size (× Scale), matching
        // the Mapsui ImageStyle convention. A regression where the renderer
        // re-applied an mm→px factor oversized every symbol by ~3.78×
        // (15 px → ~57 px); this guards against that.
        const string svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"3.98mm\" " +
            "height=\"5.35mm\" viewBox=\"-1.92 -2.55 3.98 5.35\">" +
            "<rect x=\"-1.92\" y=\"-2.55\" width=\"3.98\" height=\"5.35\" fill=\"red\"/></svg>";

        var scene = new VectorScene([
            new PointPaintOp
            {
                FeatureReference = "sym",
                World = Project(0.005, 0.005),
                Symbol = new ResolvedSymbol(svg, Scale: 1.0, PivotRelativeX: 0.0, PivotRelativeY: 0.0),
            },
        ]);

        using var bitmap = Render(scene, MakeViewport(denom: 25_000));

        // Measure the painted (non-white) bounding box.
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red != 255 || p.Green != 255 || p.Blue != 255)
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            }
        Assert.True(maxX >= 0, "Symbol rendered nothing.");

        int paintedWidth = maxX - minX + 1;
        int paintedHeight = maxY - minY + 1;

        // Natural size is ~15 × 20 px. Allow a small antialiasing margin, but
        // stay well below the ~57 × 76 px the mm-rescale bug would produce.
        Assert.InRange(paintedWidth, 13, 22);
        Assert.InRange(paintedHeight, 18, 28);
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

    // ── Rotated pivot placement (#335) ─────────────────────────────────

    [Theory]
    [InlineData(0.0, 0.0, -1.0)]    // glyph directly above the anchor
    [InlineData(90.0, 1.0, 0.0)]    // rotated 90° CW (screen) => glyph to the right
    [InlineData(180.0, 0.0, 1.0)]   // rotated 180° => glyph below the anchor
    public void Render_RotatedSymbol_PivotsAboutAnchor_NotBboxCentre(
        double rotation, double expectDirX, double expectDirY)
    {
        // A symbol whose pivot (S-100 Part 9 §11.5) sits at the BOTTOM-centre
        // with its only painted content (a red square) near the TOP. When the
        // pivot is pinned to the anchor and the glyph is rotated about it, the
        // glyph must swing around the anchor at a fixed radius. The earlier
        // renderer applied the pivot shift in screen space *before* rotating,
        // so rotated secondary symbols (e.g. a buoy's oriented light flare)
        // drifted off the anchor — the #335 compound-buoy defect.
        const string svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4mm\" height=\"10mm\" " +
            "viewBox=\"-2 -10 4 10\">" +
            "<rect x=\"-1\" y=\"-10\" width=\"2\" height=\"2\" fill=\"red\"/>" +
            "<circle class=\"pivotPoint\" cx=\"0\" cy=\"0\" r=\"0.2\" fill=\"none\"/></svg>";

        var asset = VectorSceneBuilder.ResolveSymbolAsset(svg, null)!.Value;
        var scene = new VectorScene([
            new PointPaintOp
            {
                FeatureReference = "sym",
                World = Project(0.005, 0.005),  // centre => screen (100, 100)
                Rotation = rotation,
                Symbol = new ResolvedSymbol(
                    asset.ProcessedSvg, Scale: 2.0,
                    asset.PivotRelativeX, asset.PivotRelativeY),
            },
        ]);

        using var bitmap = Render(scene, MakeViewport(denom: 25_000));

        // Red-square centroid.
        long sx = 0, sy = 0, n = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red > 150 && p.Green < 100 && p.Blue < 100) { sx += x; sy += y; n++; }
            }
        Assert.True(n > 0, "Symbol rendered nothing.");

        double cx = sx / (double)n, cy = sy / (double)n;
        const double anchorX = 100, anchorY = 100;
        double dx = cx - anchorX, dy = cy - anchorY;
        double radius = Math.Sqrt(dx * dx + dy * dy);

        // The glyph sits ~9 mm above the pivot; at 96 DPI × Scale 2 that is a
        // fixed ~68 px radius regardless of rotation. A wrong (pre-rotation,
        // screen-space) pivot shift collapses or shifts this radius.
        Assert.InRange(radius, 60.0, 76.0);

        // ...and it must lie in the expected direction once rotated.
        Assert.InRange(dx / radius, expectDirX - 0.15, expectDirX + 0.15);
        Assert.InRange(dy / radius, expectDirY - 0.15, expectDirY + 0.15);
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

    // ── Embedded font fallback (issue #23) ─────────────────────────────

    [Fact]
    public void EmbeddedFallbackFont_LoadsWithGlyphs()
    {
        // The headless renderer falls back to this embedded face when the host
        // has no usable system font (e.g. the NoDependencies SkiaSharp native on
        // a box without fontconfig). The resource must be present and decodable
        // on every platform, independent of any system font infrastructure.
        using var typeface = RendererFonts.LoadEmbeddedFallback();

        Assert.NotNull(typeface);
        Assert.True(typeface!.GlyphCount > 0, "Embedded fallback font has no glyphs.");
        Assert.Contains("Open Sans", typeface.FamilyName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_UsesEmbeddedFallback_WhenHostDefaultIsUnusable()
    {
        // Simulates the headless-Linux case where SKTypeface.Default is the empty
        // typeface (no fontconfig): the embedded fallback must engage.
        using var embedded = RendererFonts.LoadEmbeddedFallback();
        Assert.NotNull(embedded);

        int factoryCalls = 0;
        var selected = RendererFonts.Select(hostDefault: null, embeddedFactory: () =>
        {
            factoryCalls++;
            return embedded;
        });

        Assert.Same(embedded, selected);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void Select_PrefersHostDefault_WhenUsable_AndDoesNotLoadEmbedded()
    {
        // When the host exposes a usable font (every desktop / CI runner with
        // fontconfig), the OS typeface is used and the embedded fallback is never
        // even loaded — so OS-font output (and VR baselines) is unchanged.
        using var usableHost = RendererFonts.LoadEmbeddedFallback(); // a known-usable face
        Assert.NotNull(usableHost);
        Assert.True(RendererFonts.IsUsable(usableHost));

        int factoryCalls = 0;
        var selected = RendererFonts.Select(hostDefault: usableHost, embeddedFactory: () =>
        {
            factoryCalls++;
            return null;
        });

        Assert.Same(usableHost, selected);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public void Render_TextOnlyScene_DrawsForegroundGlyphs()
    {
        // A text-only scene must paint real glyphs, not a blank background. This
        // guards the label path that resolves its typeface via RendererFonts so
        // that text renders even where SKTypeface.Default is unusable.
        var scene = new VectorScene([
            new TextPaintOp
            {
                FeatureReference = "text",
                World = Project(0.005, 0.005),
                Text = "ABC",
                FontSizePx = 40,
                ForeColor = Black,
            },
        ]);

        using var bitmap = Render(scene, MakeViewport(denom: 25_000));

        int foreground = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red < 128 && p.Green < 128 && p.Blue < 128)
                    foreground++;
            }

        Assert.True(foreground > 20,
            $"Text-only scene produced too few foreground pixels ({foreground}); labels did not render.");
    }

    private static SKBitmap Render(VectorScene scene, Viewport viewport)
    {
        var renderer = new SkiaDisplayListRenderer { Background = White };
        return renderer.Render(scene, viewport);
    }

    [Fact]
    public void Render_TextWithBackground_PaintsBothBackgroundAndForeground()
    {
        // The per-render text scratch reuses one SKPaint for the label
        // background rectangle and the glyph fill, resetting its colour between
        // them. This guards that the background still paints (its colour is not
        // left as the glyph colour) and the foreground glyph paints over it.
        var background = new RgbaColor(255, 0, 0, 255);   // red box
        var foreground = new RgbaColor(0, 0, 255, 255);   // blue text
        var scene = new VectorScene([
            new TextPaintOp
            {
                FeatureReference = "text",
                World = Project(0.005, 0.005),
                Text = "ABC",
                FontSizePx = 40,
                ForeColor = foreground,
                BackColor = background,
            },
        ]);

        using var bitmap = Render(scene, MakeViewport(denom: 25_000));

        int red = 0, blue = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red > 200 && p.Green < 80 && p.Blue < 80) red++;
                if (p.Blue > 200 && p.Red < 80 && p.Green < 80) blue++;
            }

        Assert.True(red > 50, $"label background did not paint (red pixels={red}).");
        Assert.True(blue > 20, $"label foreground did not paint (blue pixels={blue}).");
    }

    [Fact]
    public void Render_TextOpsWithDifferentFontSizes_RenderAtDistinctSizes()
    {
        // The text scratch caches SKFont by pixel size; a regression that keyed
        // the cache wrongly (or shared one font) would render both labels at the
        // same size. Two same-text labels at 12 px and 40 px must differ in
        // painted extent, and both must paint.
        int Extent(double fontPx)
        {
            var scene = new VectorScene([
                new TextPaintOp
                {
                    FeatureReference = "t",
                    World = Project(0.005, 0.005),
                    Text = "8",
                    FontSizePx = fontPx,
                    ForeColor = Black,
                },
            ]);
            using var bitmap = Render(scene, MakeViewport(denom: 25_000));
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var p = bitmap.GetPixel(x, y);
                    if (p.Red < 128 && p.Green < 128 && p.Blue < 128)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            return maxX < 0 ? 0 : (maxY - minY + 1);
        }

        int small = Extent(12);
        int large = Extent(40);

        Assert.True(small > 0, "12 px label did not render.");
        Assert.True(large > small * 1.5,
            $"40 px label ({large}px tall) was not clearly larger than the 12 px label ({small}px tall).");
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
