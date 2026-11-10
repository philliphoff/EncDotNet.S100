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
/// Issue #1375 (AML S7): the seabed family drawn from synthetic SBO and LBO
/// cells, as in the design's B2c frame: framed contacts, mines and anomalies,
/// scours, dash-dot rings round the chart classes, and the dense field as one
/// counted bubble at 1:90,000 that expands into its points on zoom.
/// </summary>
public sealed class AmlSeabedRenderingTests
{
    private const double West = -1.48, East = -0.98, South = 50.40, North = 50.68;

    private static RgbaColor Background(PaletteType palette) => palette switch
    {
        PaletteType.Night => RgbaColor.FromHex("#0B0E14"),
        _ => RgbaColor.FromHex("#D6E7F3"),
    };

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Night)]
    public async Task The_seabed_family_with_a_dense_cluster(PaletteType palette)
    {
        using var bitmap = Render(Cells(palette), palette, West, East, South, North, 1000, 880, 90_000);

        // The bubble's count and label draw in each platform's own font.
        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 1_500, maxDifferentInkFraction: 0.25, Background(palette).ToSkia())
            .UseParameters(palette);
    }

    [Fact]
    public async Task The_seabed_symbols_without_text()
    {
        // No count or label, so no fonts: the comparison is tight.
        using var bitmap = Render([.. Cells(PaletteType.Day).Select(WithoutText)], PaletteType.Day, West, East, South, North, 1000, 880, 90_000);

        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 1_200, maxDifferentInkFraction: 0.03, Background(PaletteType.Day).ToSkia());
    }

    [Fact]
    public async Task The_dense_cluster_expands_into_its_points_on_zoom()
    {
        // About 1:4,000 over the field: each of the 38 contacts on its own.
        var (lon, lat) = AmlFixtures.SboClusterCentre;
        var sbo = WithoutText(new AmlPortrayal().Draw(AmlFixtures.Sbo(), "UKS0TEST.000").ToVectorPortrayalResult(Spec, "UKS0TEST.000"));
        using var bitmap = Render([sbo], PaletteType.Day, lon - 0.012, lon + 0.012, lat - 0.0075, lat + 0.0075, 800, 800, 4_000);

        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 4_000, maxDifferentInkFraction: 0.03, Background(PaletteType.Day).ToSkia());
    }

    [Fact]
    public void The_seabed_ink_draws_and_the_field_is_one_bubble_at_the_design_scale()
    {
        using var day = Render([.. Cells(PaletteType.Day).Select(WithoutText)], PaletteType.Day, West, East, South, North, 1000, 880, 90_000);
        // Thin anti-aliased strokes: few pixels are the exact ink.
        int brown = Count(day, new SKColor(0x7A, 0x4B, 0x1F));
        Assert.True(brown > 25, $"Too little seabed brown: {brown}.");

        // At the cluster centre the bubble's halo fill is white.
        var (lon, lat) = AmlFixtures.SboClusterCentre;
        var (x, y) = Pixel(lon, lat, 1000, 880);
        Assert.True(Count(day, SKColors.White, x - 8, y - 8, 16, 16) > 20, "No bubble at the field.");
    }

    // The S-57 spec until S1 (#1236) registers AML's own.
    private static SpecRef Spec { get; } = new("S-57", default);

    private static IReadOnlyList<VectorPortrayalResult> Cells(PaletteType palette) =>
    [
        new AmlPortrayal().Draw(AmlFixtures.Lbo(), "UKL0TEST.000").ToVectorPortrayalResult(Spec, "UKL0TEST.000", new RenderContext { Palette = palette }),
        new AmlPortrayal().Draw(AmlFixtures.Sbo(), "UKS0TEST.000").ToVectorPortrayalResult(Spec, "UKS0TEST.000", new RenderContext { Palette = palette }),
    ];

    private static SKBitmap Render(
        IReadOnlyList<VectorPortrayalResult> vectors, PaletteType palette, double west, double east, double south, double north, int width, int height, double scale) =>
        new ChartCompositor(new ProjNetCrsTransformFactory(), new InteroperabilityAuthority(ProductRegistry.Default.DisplayPlanes)).Render(
            [.. vectors.Select(v => ChartCompositeInput.ForVector(v))],
            new ChartCompositeOptions
            {
                Viewport = new Viewport
                {
                    MinLongitude = west,
                    MaxLongitude = east,
                    MinLatitude = south,
                    MaxLatitude = north,
                    WidthPixels = width,
                    HeightPixels = height,
                    ScaleDenominator = scale,
                },
                Background = Background(palette),
                // The bubbles and the points they stand for draw in scale windows.
                HonorScaleVisibility = true,
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

    // The pixel of a position in the full viewport (Web Mercator).
    private static (int X, int Y) Pixel(double longitude, double latitude, int width, int height)
    {
        static double MercatorY(double lat) => Math.Log(Math.Tan(Math.PI / 4 + lat * Math.PI / 360));
        double x = (longitude - West) / (East - West) * width;
        double y = (MercatorY(North) - MercatorY(latitude)) / (MercatorY(North) - MercatorY(South)) * height;
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    private static int Count(SKBitmap bitmap, SKColor color, int left = 0, int top = 0, int width = int.MaxValue, int height = int.MaxValue)
    {
        int n = 0;
        for (int y = Math.Max(0, top); y < Math.Min(bitmap.Height, top + Math.Min(height, bitmap.Height)); y++)
        {
            for (int x = Math.Max(0, left); x < Math.Min(bitmap.Width, left + Math.Min(width, bitmap.Width)); x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (Math.Abs(p.Red - color.Red) < 12 && Math.Abs(p.Green - color.Green) < 12 && Math.Abs(p.Blue - color.Blue) < 12)
                    n++;
            }
        }

        return n;
    }
}
