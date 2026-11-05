using System.Globalization;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.TropicalCyclones;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.TestSupport;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Issue #1296 (vector weather S2): an archived NHC advisory, Hurricane Ian's
/// advisory 20, drawn in storm ink as in the design mock-ups' frame 1 (the
/// cone tinted and stippled beyond 72 hours, the radii nested, the tracks,
/// forecast points and the storm with its label) in each palette; and the
/// class symbols of both hemispheres without text.
/// </summary>
public sealed class TropicalCycloneRenderingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public async Task An_advisory_in_storm_ink(PaletteType palette)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var storm = new TropicalCycloneDatasetProcessor(TropicalCycloneFixtures.IanForecast);
            var vector = await storm.BuildVectorPortrayalAsync(new RenderContext { Palette = palette }, Ct);

            using var bitmap = new HeadlessCompositor(new ProjNetCrsTransformFactory()).Render(
                [HeadlessCompositeInput.ForVector(vector)],
                new HeadlessCompositeOptions
                {
                    Viewport = Viewport(-90, -72, 20, 37, 900, 760, 8_000_000),
                    Background = Background(palette),
                });

            // A quarter of the ink may differ: the labels draw in each
            // platform's own sans-serif font.
            await TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 20_000, maxDifferentInkFraction: 0.25, Background(palette).ToSkia())
                .UseParameters(palette);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task The_class_symbols_in_both_hemispheres()
    {
        // One storm per row: depression, storm, hurricane, major, subtropical
        // depression and storm, and post-tropical, north of the equator on
        // the top row and mirrored south of it on the bottom.
        TropicalCycloneClass[] classes =
        [
            TropicalCycloneClass.TropicalDepression,
            TropicalCycloneClass.TropicalStorm,
            TropicalCycloneClass.Hurricane,
            TropicalCycloneClass.MajorHurricane,
            TropicalCycloneClass.SubtropicalDepression,
            TropicalCycloneClass.SubtropicalStorm,
            TropicalCycloneClass.PostTropical,
        ];
        var issued = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
        TropicalCycloneForecastPoint Point(int tau, double latitude, double longitude, TropicalCycloneClass classification) => new()
        {
            Tau = tau,
            ValidTime = issued.UtcDateTime.AddHours(tau),
            Position = new GeoPosition(latitude, longitude),
            Classification = classification,
            ClassCode = "HU",
        };

        TropicalCycloneAdvisory Row(double latitude) => new()
        {
            Storm = new TropicalCycloneStorm("AL142026", "Zelda"),
            Advisory = "17",
            IssueTime = issued,
            Forecast = [.. classes.Select((c, i) => Point(12 * i, latitude, -80 + 4 * i, c))],
            ForecastTrack = [],
        };

        var north = new TropicalCycloneDatasetProcessor(Row(3), "north.zip");
        var south = new TropicalCycloneDatasetProcessor(Row(-3), "south.zip");
        // Drawn three times their size, so the arms can be checked by eye.
        var sheet = new RenderContext { SymbolScale = 3 };
        var options = new HeadlessCompositeOptions
        {
            Viewport = Viewport(-82, -54, -6, 6, 800, 340, 12_000_000),
            Background = RgbaColor.FromHex("#FFFFFF"),
        };

        using var bitmap = new HeadlessCompositor(new ProjNetCrsTransformFactory()).Render(
            [
                HeadlessCompositeInput.ForVector(WithoutText(await north.BuildVectorPortrayalAsync(sheet, Ct))),
                HeadlessCompositeInput.ForVector(WithoutText(await south.BuildVectorPortrayalAsync(sheet, Ct))),
            ],
            options);

        // Anti-aliasing on the circles' edges differs by about a tenth of the
        // ink between macOS and Linux; the arms' direction is pinned by
        // TropicalCycloneStyleTests.
        await TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 2_000, maxDifferentInkFraction: 0.15, SKColors.White);
    }

    private static VectorPortrayalResult WithoutText(VectorPortrayalResult result) => new()
    {
        SubLayers = [.. result.SubLayers.Select(layer => new VectorSubLayer
        {
            LayerKey = layer.LayerKey,
            LayerName = layer.LayerName,
            Instructions = [.. layer.Instructions.Where(i => i is not TextInstruction)],
            Plane = layer.Plane,
            WithinPlanePriority = layer.WithinPlanePriority,
        })],
        Palette = result.Palette,
        GeometryProvider = result.GeometryProvider,
        Product = result.Product,
        Spec = result.Spec,
        SourceDatasetId = result.SourceDatasetId,
        Info = result.Info,
        SymbolScale = result.SymbolScale,
        TextScale = result.TextScale,
        SymbolProvider = result.SymbolProvider,
        LineStyleProvider = result.LineStyleProvider,
        AreaFillProvider = result.AreaFillProvider,
    };

    private static Viewport Viewport(double west, double east, double south, double north, int width, int height, double scale) => new()
    {
        MinLongitude = west,
        MaxLongitude = east,
        MinLatitude = south,
        MaxLatitude = north,
        WidthPixels = width,
        HeightPixels = height,
        ScaleDenominator = scale,
    };

    // The chart's paper: white by day, dark blues at dusk and night.
    private static RgbaColor Background(PaletteType palette) => palette switch
    {
        PaletteType.Dusk => RgbaColor.FromHex("#3B4250"),
        PaletteType.Night => RgbaColor.FromHex("#0B0E14"),
        _ => RgbaColor.FromHex("#FFFFFF"),
    };
}
