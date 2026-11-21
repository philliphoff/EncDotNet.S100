using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Aml;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.TestSupport;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Issue #1376 (AML S8): the environmental family as in the design's frame
/// B2d. A synthetic ESB cell's sediment patterns over their 7 % wash, bottom
/// features, scours, zones, beach exit, framed locations and ice, in each
/// palette; the same without text, held tighter; and a synthetic AML 3.0 SED
/// cell's patterns.
/// </summary>
public sealed class AmlEnvironmentalRenderingTests
{
    private static RgbaColor Background(PaletteType palette) => palette switch
    {
        PaletteType.Dusk => RgbaColor.FromHex("#3B4250"),
        PaletteType.Night => RgbaColor.FromHex("#0B0E14"),
        _ => RgbaColor.FromHex("#D6E7F3"),
    };

    private static SpecRef Spec { get; } = new("AML", default);

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public async Task The_ESB_environmental_family(PaletteType palette)
    {
        var drawing = new AmlPortrayal().Draw(AmlFixtures.Esb(), "UKE6TEST.000");
        using var bitmap = Render(drawing.ToVectorPortrayalResult(Spec, "UKE6TEST.000", new RenderContext { Palette = palette }), palette, Esb);

        // A quarter of the ink may differ: the labels draw in each platform's
        // own sans-serif font.
        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 15_000, maxDifferentInkFraction: 0.25, Background(palette).ToSkia())
            .UseParameters(palette);
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Night)]
    public async Task The_ESB_patterns_and_symbols_without_text(PaletteType palette)
    {
        // Every pattern, boundary, stroke, arrow and framed symbol, no labels:
        // no fonts, so the comparison is tight.
        var drawing = new AmlPortrayal().Draw(AmlFixtures.Esb(), "UKE6TEST.000");
        using var bitmap = Render(WithoutText(drawing.ToVectorPortrayalResult(Spec, "UKE6TEST.000", new RenderContext { Palette = palette })), palette, Esb);

        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 15_000, maxDifferentInkFraction: 0.03, Background(palette).ToSkia())
            .UseParameters(palette);
    }

    [Fact]
    public async Task The_AML_3_SED_patterns()
    {
        var drawing = new AmlPortrayal().Draw(AmlFixtures.Sed(), "AMLTEST6U_SED.000");
        using var bitmap = Render(WithoutText(drawing.ToVectorPortrayalResult(Spec, "AMLTEST6U_SED.000")), PaletteType.Day, Sed);

        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 10_000, maxDifferentInkFraction: 0.03, Background(PaletteType.Day).ToSkia());
    }

    [Fact]
    public void Sediment_is_a_faint_wash_with_marks_in_the_environmental_ink()
    {
        // Inside the sand band, the paper is tinted, not covered: the wash is
        // 7 % of the green. And the ink appears, in the marks.
        var drawing = new AmlPortrayal().Draw(AmlFixtures.Esb(), "UKE6TEST.000");
        using var bitmap = Render(WithoutText(drawing.ToVectorPortrayalResult(Spec, "UKE6TEST.000")), PaletteType.Day, Esb);

        var paper = Background(PaletteType.Day).ToSkia();
        var ink = new SKColor(0x2B, 0x75, 0x47);
        var washed = Blend(paper, ink, 0.07);
        var (x0, y0) = Pixel(Esb, -1.28, 50.665);
        var (x1, y1) = Pixel(Esb, -1.16, 50.635);
        int wash = 0, marks = 0, other = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (Near(p, washed, 3))
                    wash++;
                else if (p.Green < washed.Green - 20)
                    marks++;
                else
                    other++;
            }
        }

        Assert.True(wash > (x1 - x0) * (y1 - y0) / 3, $"Too little wash: {wash} of {(x1 - x0) * (y1 - y0)}.");
        Assert.True(marks > 200, $"Too few sand dots: {marks}.");
        Assert.Equal(0, Count(bitmap, paper, x0, y0, x1, y1));
    }

    private sealed record View(double West, double East, double South, double North, int Width, int Height, double Scale);

    private static readonly View Esb = new(-1.48, -0.98, 50.40, 50.68, 1000, 880, 90_000);
    private static readonly View Sed = new(9.98, 10.42, 54.38, 54.62, 800, 750, 150_000);

    private static SKBitmap Render(VectorPortrayalResult vector, PaletteType palette, View view) =>
        new ChartCompositor(new ProjNetCrsTransformFactory(), new InteroperabilityAuthority(ProductRegistry.Default.DisplayPlanes)).Render(
            [ChartCompositeInput.ForVector(vector)],
            new ChartCompositeOptions
            {
                Viewport = new Viewport
                {
                    MinLongitude = view.West,
                    MaxLongitude = view.East,
                    MinLatitude = view.South,
                    MaxLatitude = view.North,
                    WidthPixels = view.Width,
                    HeightPixels = view.Height,
                    ScaleDenominator = view.Scale,
                },
                Background = Background(palette),
            });

    private static VectorPortrayalResult WithoutText(VectorPortrayalResult result) => new()
    {
        SubLayers = [.. result.SubLayers.Select(s => new VectorSubLayer
        {
            LayerKey = s.LayerKey,
            LayerName = s.LayerName,
            Instructions = [.. s.Instructions.Where(i => i is not TextInstruction)],
            Plane = s.Plane,
            WithinPlanePriority = s.WithinPlanePriority,
            SourceFeatureType = s.SourceFeatureType,
            Legend = s.Legend,
        })],
        Palette = result.Palette,
        GeometryProvider = result.GeometryProvider,
        Product = result.Product,
        Spec = result.Spec,
        SourceDatasetId = result.SourceDatasetId,
        Info = result.Info,
        SymbolProvider = result.SymbolProvider,
        LineStyleProvider = result.LineStyleProvider,
        AreaFillProvider = result.AreaFillProvider,
        LayerNames = result.LayerNames,
        GeographicExtent = result.GeographicExtent,
    };

    // The pixel of a position in a viewport (Web Mercator).
    private static (int X, int Y) Pixel(View view, double longitude, double latitude)
    {
        static double MercatorY(double lat) => Math.Log(Math.Tan(Math.PI / 4 + lat * Math.PI / 360));
        double x = (longitude - view.West) / (view.East - view.West) * view.Width;
        double y = (MercatorY(view.North) - MercatorY(latitude)) / (MercatorY(view.North) - MercatorY(view.South)) * view.Height;
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    private static SKColor Blend(SKColor under, SKColor over, double alpha) => new(
        (byte)Math.Round(under.Red + (over.Red - under.Red) * alpha),
        (byte)Math.Round(under.Green + (over.Green - under.Green) * alpha),
        (byte)Math.Round(under.Blue + (over.Blue - under.Blue) * alpha));

    private static bool Near(SKColor a, SKColor b, int tolerance) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance && Math.Abs(a.Blue - b.Blue) <= tolerance;

    private static int Count(SKBitmap bitmap, SKColor color, int x0, int y0, int x1, int y1)
    {
        int n = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                if (bitmap.GetPixel(x, y) == color)
                    n++;
            }
        }

        return n;
    }
}
