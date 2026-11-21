using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Aml;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.TestSupport;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Issue #1377 (AML S9): CLB drawn with SoundCharts' own AML portrayal, as in
/// the design's B2e frame. Thin seabed-brown contours at 55 % with italic
/// labels and no depth-area fill; and, over a chart, the chart's safety
/// contour keeping its weight and gaining a halo (decision 22).
/// </summary>
public sealed class AmlClbRenderingTests
{
    private static readonly SpecRef Spec = new(SpecName.Aml, new SpecVersion(1, 0, 0));

    private const double West = -1.48, East = -0.98, South = 50.40, North = 50.68;
    private const int Width = 1000, Height = 880;

    // The chart's safety contour runs just west of CLB's 30 m contour.
    private const double SafetyContourLongitude = -1.215;

    private static RgbaColor Background(PaletteType palette) => palette == PaletteType.Night
        ? RgbaColor.FromHex("#0B0E14")
        : RgbaColor.FromHex("#D6E7F3");

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Night)]
    public async Task The_CLB_contours_without_text(PaletteType palette)
    {
        // Contours only: no fonts, so the comparison is tight.
        using var bitmap = Render(palette, Clb(palette, text: false));

        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 4_000, maxDifferentInkFraction: 0.03, Background(palette).ToSkia())
            .UseParameters(palette);
    }

    [Fact]
    public async Task The_CLB_contours_with_their_labels_and_soundings()
    {
        // A third of the ink may differ: the labels and figures draw in each
        // platform's own sans-serif font.
        using var bitmap = Render(PaletteType.Day, Clb(PaletteType.Day, text: true));

        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 4_000, maxDifferentInkFraction: 0.33, Background(PaletteType.Day).ToSkia());
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Night)]
    public async Task The_chart_safety_contour_keeps_its_weight_and_gains_a_halo_under_CLB(PaletteType palette)
    {
        using var bitmap = Render(palette, Chart(palette, halo: true), Clb(palette, text: false));

        await TestHelpers.VerifySparseBitmap(bitmap.Copy(), minimumInkPixels: 20_000, maxDifferentInkFraction: 0.03, Background(palette).ToSkia())
            .UseParameters(palette);
    }

    [Fact]
    public void The_halo_clears_the_contours_beside_the_safety_contour()
    {
        // Along a row clear of the soundings, the safety contour is the only
        // dark ink, with paper-white either side of it only when haloed.
        using var plain = Render(PaletteType.Day, Chart(PaletteType.Day, halo: false));
        using var haloed = Render(PaletteType.Day, Chart(PaletteType.Day, halo: true));
        var (x, y) = Pixel(SafetyContourLongitude, 50.60);

        var white = new SKColor(0xFF, 0xFF, 0xFF);
        Assert.Equal(0, CountNear(plain, white, x, y));
        Assert.True(CountNear(haloed, white, x, y) >= 2, "No halo beside the safety contour.");
        Assert.True(IsDark(haloed.GetPixel(x, y)), "The safety contour lost its ink under its halo.");
    }

    private static VectorPortrayalResult Clb(PaletteType palette, bool text)
    {
        var result = new AmlPortrayal().Draw(AmlFixtures.Clb(), "UKC6TEST.000")
            .ToVectorPortrayalResult(Spec, "UKC6TEST.000", new RenderContext { Palette = palette });
        return text ? result : WithoutText(result);
    }

    // A small chart: shallow water to the west of the safety contour, a 20 m
    // contour in it, and the safety contour (S-101 DEPARE03: solid 0.64 mm
    // DEPSC) drawn by the depth areas either side, with the halo when asked.
    private static VectorPortrayalResult Chart(PaletteType palette, bool halo)
    {
        var geometry = new Dictionary<string, FeatureGeometry>
        {
            ["shallow"] = new()
            {
                Type = GeometryType.Surface,
                Coordinates = Ring((West, North), (SafetyContourLongitude, North), (SafetyContourLongitude, South), (West, South)),
            },
            ["contour20"] = Curve((-1.45, North), (-1.45, South)),
            ["safety"] = Curve((SafetyContourLongitude, North), (SafetyContourLongitude, South)),
            ["safetyEast"] = Curve((SafetyContourLongitude, North), (SafetyContourLongitude, South)),
        };
        var areas = new InstructionBuilder { ViewingGroup = 13030, DrawingPriority = 3 };
        var lines = new InstructionBuilder { ViewingGroup = 33020, DrawingPriority = 13 };
        IReadOnlyList<DrawingInstruction> instructions =
        [
            areas.Area("shallow", "DEPMS"),
            lines.Line("contour20", "DEPCN", 0.32),
            new InstructionBuilder { ViewingGroup = 13030, DrawingPriority = 13 }.Line("safety", SafetyContourHalo.SafetyContourColorToken, 0.64),

            // The deeper area on the other side draws the same edge too, as
            // adjoining S-101 depth areas do: Part 9 line suppression must
            // not take the halo off the shared edge.
            new InstructionBuilder { ViewingGroup = 13030, DrawingPriority = 13 }.Line("safetyEast", SafetyContourHalo.SafetyContourColorToken, 0.64),
        ];
        if (halo)
            instructions = SafetyContourHalo.Apply(instructions);

        var colors = new ApplicationPalette("chart", new Dictionary<string, PaletteColors>
        {
            ["DEPMS"] = PaletteColors.FromHex("#A9CFEA", "#213A55", "#14243A"),
            ["DEPCN"] = PaletteColors.FromHex("#768C97", "#4C5B63", "#3A464C"),
            ["DEPSC"] = PaletteColors.FromHex("#343E44", "#A1B4BC", "#7C8C94"),
            ["UIBCK"] = PaletteColors.FromHex("#FFFFFF", "#000000", "#000000"),
        });

        return new VectorPortrayalResult
        {
            SubLayers =
            [
                new VectorSubLayer
                {
                    LayerKey = "chart",
                    LayerName = "Chart",
                    Instructions = instructions,
                    Plane = S98DisplayPlane.BaseChartOver,
                    SourceFeatureType = "chart",
                },
            ],
            Palette = colors.GetColorPalette(palette),
            GeometryProvider = new Geometry(geometry),
            Product = "S-101",
            Spec = new SpecRef("S-101", new SpecVersion(2, 0, 0)),
            SourceDatasetId = "chart",
            Info = "A test chart",
            LayerNames = ["chart"],
        };
    }

    private static SKBitmap Render(PaletteType palette, params VectorPortrayalResult[] vectors) =>
        new ChartCompositor(new ProjNetCrsTransformFactory(), new InteroperabilityAuthority(ProductRegistry.Default.DisplayPlanes)).Render(
            [.. vectors.Select(v => ChartCompositeInput.ForVector(v))],
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

    private static FeatureGeometry Curve(params (double Lon, double Lat)[] points) => new()
    {
        Type = GeometryType.Curve,
        Coordinates = [.. points.Select(p => new GeoPosition(p.Lat, p.Lon))],
    };

    private static GeoPosition[] Ring(params (double Lon, double Lat)[] points) =>
        [.. points.Select(p => new GeoPosition(p.Lat, p.Lon)), new GeoPosition(points[0].Lat, points[0].Lon)];

    // The pixel of a position in the Render viewport (Web Mercator).
    private static (int X, int Y) Pixel(double longitude, double latitude)
    {
        static double MercatorY(double lat) => Math.Log(Math.Tan(Math.PI / 4 + lat * Math.PI / 360));
        double x = (longitude - West) / (East - West) * Width;
        double y = (MercatorY(North) - MercatorY(latitude)) / (MercatorY(North) - MercatorY(South)) * Height;
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    // Pixels within 4 px either side of (x, y) on its row that are near color.
    private static int CountNear(SKBitmap bitmap, SKColor color, int x, int y)
    {
        int n = 0;
        for (int dx = -4; dx <= 4; dx++)
        {
            var p = bitmap.GetPixel(x + dx, y);
            if (Math.Abs(p.Red - color.Red) < 10 && Math.Abs(p.Green - color.Green) < 10 && Math.Abs(p.Blue - color.Blue) < 10)
                n++;
        }

        return n;
    }

    private static bool IsDark(SKColor color) => color.Red < 0x70 && color.Green < 0x70 && color.Blue < 0x70;

    private sealed class Geometry(IReadOnlyDictionary<string, FeatureGeometry> features) : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) =>
            features.TryGetValue(featureReference, out var geometry) ? geometry : null;
    }
}
