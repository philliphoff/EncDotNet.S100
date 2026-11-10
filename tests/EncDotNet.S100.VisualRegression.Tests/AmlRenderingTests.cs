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
/// Issue #1373 (AML S5): synthetic AML cells drawn with SoundCharts' own AML
/// portrayal, as in the design's B2a, B2b and B2f frames: the RAL air and
/// operational families on the overlay signature (dash-dot on a halo, ticked
/// unfilled areas, framed points, italic labels, limits as a stacked
/// fraction) in each palette; the same without text, held tighter; and the
/// AML 3.0 air classes.
/// </summary>
public sealed class AmlRenderingTests
{
    // Chart-like paper: a day sea blue, and the chart's night dark.
    private static RgbaColor Background(PaletteType palette) => palette switch
    {
        PaletteType.Dusk => RgbaColor.FromHex("#3B4250"),
        PaletteType.Night => RgbaColor.FromHex("#0B0E14"),
        _ => RgbaColor.FromHex("#D6E7F3"),
    };

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public async Task The_RAL_air_and_operational_families(PaletteType palette)
    {
        var drawing = new AmlPortrayal().Draw(AmlFixtures.Ral(), "UKR0TEST.000");
        using var bitmap = Render(drawing.ToVectorPortrayalResult(Spec, "UKR0TEST.000", new RenderContext { Palette = palette }), palette);

        // A quarter of the ink may differ: the labels draw in each platform's
        // own sans-serif font.
        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 15_000, maxDifferentInkFraction: 0.25, Background(palette).ToSkia())
            .UseParameters(palette);
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Night)]
    public async Task The_RAL_signature_without_text(PaletteType palette)
    {
        // Every line, tick, chevron and framed symbol, no labels: no fonts, so
        // the comparison is tight.
        var drawing = new AmlPortrayal().Draw(AmlFixtures.Ral(), "UKR0TEST.000");
        using var bitmap = Render(WithoutText(drawing.ToVectorPortrayalResult(Spec, "UKR0TEST.000", new RenderContext { Palette = palette })), palette);

        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 12_000, maxDifferentInkFraction: 0.03, Background(palette).ToSkia())
            .UseParameters(palette);
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Night)]
    public async Task The_AML_3_air_classes(PaletteType palette)
    {
        var drawing = new AmlPortrayal().Draw(AmlFixtures.Fai(), "AMLTEST0U_FAI.000");
        // Without text: the cell is small, so labels in each platform's own font
        // would be most of its ink. The unit tests check the labels' text. The
        // framed symbols are still a large share of the ink and antialias a
        // little differently on Linux, hence the looser tolerance.
        var vector = WithoutText(drawing.ToVectorPortrayalResult(Spec, "AMLTEST0U_FAI.000", new RenderContext { Palette = palette }));
        using var bitmap = new ChartCompositor(new ProjNetCrsTransformFactory(), new InteroperabilityAuthority(ProductRegistry.Default.DisplayPlanes)).Render(
            [ChartCompositeInput.ForVector(vector)],
            new ChartCompositeOptions
            {
                Viewport = new Viewport
                {
                    MinLongitude = 8.8,
                    MaxLongitude = 10.0,
                    MinLatitude = 53.5,
                    MaxLatitude = 54.1,
                    WidthPixels = 800,
                    HeightPixels = 680,
                    ScaleDenominator = 500_000,
                },
                Background = Background(palette),
            });

        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 2_500, maxDifferentInkFraction: 0.15, Background(palette).ToSkia())
            .UseParameters(palette);
    }

    [Fact]
    public void The_framed_symbols_and_lines_draw_in_the_family_ink()
    {
        // The air ink appears by Day, and the operational ink, and no fill:
        // the paper shows inside the areas.
        var drawing = new AmlPortrayal().Draw(AmlFixtures.Ral(), "UKR0TEST.000");
        using var bitmap = Render(WithoutText(drawing.ToVectorPortrayalResult(Spec, "UKR0TEST.000")), PaletteType.Day);

        Assert.True(Count(bitmap, new SKColor(0x23, 0x50, 0xB5)) > 500, "Too little air blue.");
        Assert.True(Count(bitmap, new SKColor(0xB2, 0x38, 0x1B)) > 500, "Too little operational brick.");
        // Inside the danger area, clear of its boundary, ticks and label: paper.
        var (x, y) = Pixel(-1.10, 50.61);
        Assert.Equal(Background(PaletteType.Day).ToSkia(), bitmap.GetPixel(x, y));
    }

    private static SpecRef Spec { get; } = new(SpecName.Aml, new SpecVersion(1, 0, 0));

    private const double West = -1.48, East = -0.98, South = 50.40, North = 50.68;
    private const int Width = 1000, Height = 880;

    private static SKBitmap Render(VectorPortrayalResult vector, PaletteType palette) =>
        new ChartCompositor(new ProjNetCrsTransformFactory(), new InteroperabilityAuthority(ProductRegistry.Default.DisplayPlanes)).Render(
            [ChartCompositeInput.ForVector(vector)],
            new ChartCompositeOptions
            {
                Viewport = new Viewport
                {
                    MinLongitude = West,
                    MaxLongitude = East,
                    MinLatitude = South,
                    MaxLatitude = North,
                    WidthPixels = Width,
                    HeightPixels = Height,
                    ScaleDenominator = 90_000,
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
        LayerNames = result.LayerNames,
        GeographicExtent = result.GeographicExtent,
    };

    // The pixel of a position in the Render viewport (Web Mercator).
    private static (int X, int Y) Pixel(double longitude, double latitude)
    {
        static double MercatorY(double lat) => Math.Log(Math.Tan(Math.PI / 4 + lat * Math.PI / 360));
        double x = (longitude - West) / (East - West) * Width;
        double y = (MercatorY(North) - MercatorY(latitude)) / (MercatorY(North) - MercatorY(South)) * Height;
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    private static int Count(SKBitmap bitmap, SKColor color)
    {
        int n = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (Math.Abs(p.Red - color.Red) < 12 && Math.Abs(p.Green - color.Green) < 12 && Math.Abs(p.Blue - color.Blue) < 12)
                    n++;
            }
        }

        return n;
    }
}
