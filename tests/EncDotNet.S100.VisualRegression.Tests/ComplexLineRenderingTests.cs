using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.TestSupport;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// The complex line style of the code-defined portrayal toolkit (#1293,
/// vector weather S0) drawn through the shared Skia renderer: symbols repeated
/// along a line on a chosen side, alternating sides and inks, at a spacing
/// that holds on screen at every zoom; and the legend glyphs of those lines.
/// The styles are synthetic front-like fixtures (<see cref="FrontPipFixtures"/>),
/// not the fronts portrayal (#1300).
/// </summary>
public sealed class ComplexLineRenderingTests
{
    // Three bent lines, each running west to east, so "left" is the north side.
    private static readonly Dictionary<string, FeatureGeometry> Fronts = new()
    {
        ["cold"] = Curve((10.4, -2.0), (11.0, -0.5), (10.6, 1.0), (10.9, 2.0)),
        ["warm"] = Curve((9.2, -2.0), (9.6, -0.8), (9.0, 0.6), (9.4, 2.0)),
        ["stationary"] = Curve((8.0, -2.0), (8.4, -0.6), (7.8, 0.8), (8.2, 2.0)),
        ["dashed"] = Curve((7.0, -2.0), (7.0, 2.0)),
    };

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public async Task Sided_symbols_along_lines(PaletteType palette)
    {
        var builder = new InstructionBuilder { DrawingPriority = 92 };
        DrawingInstruction[] instructions =
        [
            builder.ComplexLine("cold", FrontPipFixtures.Cold.Name),
            builder.ComplexLine("warm", FrontPipFixtures.Warm.Name),
            builder.ComplexLine("stationary", FrontPipFixtures.Stationary.Name),
            builder.ComplexLine("dashed", FrontPipFixtures.Dashed.Name),
        ];

        var bitmap = Render(instructions, palette, Viewport(-2.4, 2.4, 6.6, 11.6, 640, 640, 2_000_000));

        var background = FrontPipFixtures.Background(palette).ToSkia();
        await TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 2_000, maxDifferentInkFraction: 0.05, background)
            .UseParameters(palette);
    }

    [Fact]
    public void Symbols_keep_their_screen_spacing_when_zooming()
    {
        // A straight line along the equator, drawn at one scale and at four
        // times it: the triangles sit 9 mm (28.1 px at 0.32 mm a pixel) apart
        // on screen both times.
        var line = new Dictionary<string, FeatureGeometry> { ["cold"] = Curve((0, -1.0), (0, 1.0)) };
        DrawingInstruction[] instructions = [new InstructionBuilder().ComplexLine("cold", FrontPipFixtures.Cold.Name)];

        using var wide = Render(instructions, PaletteType.Day, Viewport(-1.2, 1.2, -0.15, 0.15, 800, 100, 1_000_000), line);
        using var close = Render(instructions, PaletteType.Day, Viewport(-0.3, 0.3, -0.0375, 0.0375, 800, 100, 250_000), line);

        var wideGaps = Gaps(PipCentres(wide, above: true));
        var closeGaps = Gaps(PipCentres(close, above: true));

        Assert.True(wideGaps.Count > 10, $"Expected many triangles, found {wideGaps.Count + 1}.");
        Assert.True(closeGaps.Count > 10, $"Expected many triangles, found {closeGaps.Count + 1}.");
        Assert.All(wideGaps, gap => Assert.InRange(gap, 9 / 0.32 - 1, 9 / 0.32 + 1));
        Assert.All(closeGaps, gap => Assert.InRange(gap, 9 / 0.32 - 1, 9 / 0.32 + 1));

        // The first sits 4 mm (12.5 px) from the start of the line.
        double start = 400 - 800 * 1.0 / 2.4;
        Assert.InRange(PipCentres(wide, above: true)[0] - start, 4 / 0.32 - 1.5, 4 / 0.32 + 1.5);

        // Nothing on the other side.
        Assert.Empty(PipCentres(wide, above: false));
    }

    [Fact]
    public void The_right_side_and_alternating_sides_put_symbols_where_they_say()
    {
        var line = new Dictionary<string, FeatureGeometry>
        {
            ["warm"] = Curve((0, -1.0), (0, 1.0)),
        };
        var viewport = Viewport(-1.2, 1.2, -0.15, 0.15, 800, 100, 1_000_000);

        using var warm = Render([new InstructionBuilder().ComplexLine("warm", FrontPipFixtures.Warm.Name)], PaletteType.Day, viewport, line);
        Assert.Empty(PipCentres(warm, above: true));
        Assert.True(PipCentres(warm, above: false).Count > 10);

        using var stationary = Render([new InstructionBuilder().ComplexLine("warm", FrontPipFixtures.Stationary.Name)], PaletteType.Day, viewport, line);
        var north = PipCentres(stationary, above: true);
        var south = PipCentres(stationary, above: false);

        // Alternating: each side every 18 mm, offset from each other by 9 mm.
        Assert.All(Gaps(north), gap => Assert.InRange(gap, 18 / 0.32 - 1, 18 / 0.32 + 1));
        Assert.All(Gaps(south), gap => Assert.InRange(gap, 18 / 0.32 - 1, 18 / 0.32 + 1));
        Assert.InRange(south[0] - north[0], 9 / 0.32 - 1.5, 9 / 0.32 + 1.5);
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Night)]
    public async Task Legend_glyphs(PaletteType palette)
    {
        var legend = new LegendBuilder(FrontPipFixtures.Palette, palette, FrontPipFixtures.Symbols);
        LegendEntry[] rows =
        [
            legend.Swatch(FrontPipFixtures.WarmInk, "Band"),
            legend.Area(FrontPipFixtures.WarmInk, "Area", legend.Sample(FrontPipFixtures.Ink, 0.6, 1.6, 1.2), transparency: 0.6),
            legend.Symbol(FrontPipFixtures.Triangle, "Triangle"),
            legend.Line(FrontPipFixtures.Cold, "Cold"),
            legend.Line(FrontPipFixtures.Warm, "Warm"),
            legend.Line(FrontPipFixtures.Stationary, "Stationary"),
            legend.Line(FrontPipFixtures.Dashed, "Dashed with pips"),
            legend.Line(FrontPipFixtures.Ink, 0.5, "Trough", dashLength: 1.5, gapLength: 1),
        ];

        // One row per glyph, at 4× so the snapshot shows the detail.
        const float scale = 4f;
        var background = FrontPipFixtures.Background(palette).ToSkia();
        var bitmap = new SKBitmap((int)(32 * scale), (int)(rows.Length * 18 * scale));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(background);
            for (int i = 0; i < rows.Length; i++)
            {
                var (width, height) = LegendGlyphRenderer.SizeOf(rows[i]);
                using var glyph = LegendGlyphRenderer.Render(rows[i], scale);
                Assert.Equal((int)(width * scale), glyph.Width);
                Assert.Equal((int)(height * scale), glyph.Height);
                canvas.DrawBitmap(glyph, 2 * scale, (i * 18 + (18 - height) / 2f) * scale);
            }
        }

        await TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 1_500, maxDifferentInkFraction: 0.05, background)
            .UseParameters(palette);
    }

    private static SKBitmap Render(
        IReadOnlyList<DrawingInstruction> instructions,
        PaletteType palette,
        Viewport viewport,
        IReadOnlyDictionary<string, FeatureGeometry>? geometry = null) =>
        HeadlessVectorRenderer.Render(
            instructions,
            new Geometry(geometry ?? Fronts),
            FrontPipFixtures.Palette.GetColorPalette(palette),
            FrontPipFixtures.Symbols,
            FrontPipFixtures.LineStyles.Provider,
            symbolScale: 1.0,
            textScale: 1.0,
            viewport.WidthPixels,
            viewport.HeightPixels,
            FrontPipFixtures.Background(palette),
            viewport: viewport);

    // The x centres of the runs of ink in the pixel row 3 px above (or below)
    // the middle of a white render, clear of the 2.2 px pen: the pips of a
    // line along the middle row.
    private static List<double> PipCentres(SKBitmap bitmap, bool above)
    {
        int y = bitmap.Height / 2 + (above ? -3 : 3);
        var centres = new List<double>();
        int runStart = -1;
        for (int x = 0; x <= bitmap.Width; x++)
        {
            bool ink = x < bitmap.Width && IsInk(bitmap.GetPixel(x, y));
            if (ink && runStart < 0)
                runStart = x;
            else if (!ink && runStart >= 0)
            {
                // Skip runs cut by the image's edge.
                if (runStart > 0 && x < bitmap.Width)
                    centres.Add((runStart + x - 1) / 2.0);
                runStart = -1;
            }
        }

        return centres;
    }

    private static bool IsInk(SKColor pixel) => (pixel.Red + pixel.Green + pixel.Blue) / 3 < 160;

    private static List<double> Gaps(List<double> centres) =>
        [.. centres.Zip(centres.Skip(1), (a, b) => b - a)];

    private static FeatureGeometry Curve(params (double Latitude, double Longitude)[] points) => new()
    {
        Type = GeometryType.Curve,
        Coordinates = [.. points.Select(p => new GeoPosition(p.Latitude, p.Longitude))],
    };

    private static Viewport Viewport(
        double west, double east, double south, double north, int width, int height, double scale) => new()
        {
            MinLongitude = west,
            MaxLongitude = east,
            MinLatitude = south,
            MaxLatitude = north,
            WidthPixels = width,
            HeightPixels = height,
            ScaleDenominator = scale,
        };

    private sealed class Geometry(IReadOnlyDictionary<string, FeatureGeometry> features) : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) =>
            features.TryGetValue(featureReference, out var geometry) ? geometry : null;
    }
}
