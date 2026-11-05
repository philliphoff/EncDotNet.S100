using System.Text;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.TestSupport;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Issue #1300 (vector weather S7): a surface analysis drawn with WMO frontal
/// symbology in the monochrome front ink, over a pressure field's isobars as
/// in the design mock-ups' fronts frame, in each palette; the real
/// high-resolution WPC analysis; and pixel checks that the pips fall on the
/// left of the bulletin's point order (the rule S6 confirmed).
/// </summary>
public sealed class SurfaceAnalysisRenderingTests
{
    private static readonly DateTime Run = new(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public async Task Fronts_and_centres_over_isobars(PaletteType palette)
    {
        // The hand-written 996 hPa low and 1032 hPa high, over a pressure field
        // with the same centres, as in the mock-ups' frame 3.
        var fronts = new SurfaceAnalysisDatasetProcessor(CodsusFixtures.Fixture(CodsusFixtures.AtlanticLow));
        var isobars = await WeatherPortrayal.BuildAsync(
            AtlanticPressure(), [new PressureIsobarStyle()], "gfs.grb2", new RenderContext { Palette = palette }, cancellationToken: Ct);
        var vector = await fronts.BuildVectorPortrayalAsync(new RenderContext { Palette = palette }, Ct);

        var bitmap = new HeadlessCompositor(new ProjNetCrsTransformFactory()).Render(
            [HeadlessCompositeInput.ForCoverage(isobars), HeadlessCompositeInput.ForVector(vector)],
            new HeadlessCompositeOptions
            {
                Viewport = Viewport(-72, -24, 27, 51, 900, 600, 15_000_000),
                Background = Background(palette),
            });

        await TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 8_000, maxDifferentInkFraction: 0.05, Background(palette).ToSkia())
            .UseParameters(palette);
    }

    [Fact]
    public async Task The_real_high_resolution_analysis()
    {
        var fronts = new SurfaceAnalysisDatasetProcessor(CodsusFixtures.Fixture(CodsusFixtures.HighResolution));

        using var bitmap = await fronts.RenderHeadlessAsync(
            1000,
            640,
            new RenderContext { Viewport = Viewport(-125, -60, 20, 55, 1000, 640, 25_000_000) },
            cancellationToken: Ct);

        await TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 8_000, maxDifferentInkFraction: 0.05, SKColors.White);
    }

    [Theory]
    [InlineData("COLD")]
    [InlineData("WARM")]
    [InlineData("OCFNT")]
    public async Task Pips_go_on_the_left_of_the_bulletins_point_order(string code)
    {
        // West to east, left is north; east to west, left is south.
        using var eastward = await RenderLine(code, "4000700 4000500");
        using var westward = await RenderLine(code, "4000500 4000700");

        Assert.True(InkRows(eastward, above: true) >= 3, $"{code} west to east: no pips north of the line.");
        Assert.Equal(0, InkRows(eastward, above: false));
        Assert.True(InkRows(westward, above: false) >= 3, $"{code} east to west: no pips south of the line.");
        Assert.Equal(0, InkRows(westward, above: true));
    }

    [Fact]
    public async Task A_stationary_front_has_pips_on_both_sides_and_a_trough_none()
    {
        using var stationary = await RenderLine("STNRY", "4000700 4000500");
        using var trough = await RenderLine("TROF", "4000700 4000500");

        Assert.True(InkRows(stationary, above: true) >= 3);
        Assert.True(InkRows(stationary, above: false) >= 3);
        Assert.Equal(0, InkRows(trough, above: true));
        Assert.Equal(0, InkRows(trough, above: false));
    }

    // One front along 40°N, coded at high resolution, rendered alone at
    // 1:10,000,000 over 64 px of latitude.
    private static async Task<SKBitmap> RenderLine(string code, string points)
    {
        var text = $"CODED SURFACE FRONTAL POSITIONS\n\nVALID 110418Z\n{code} {points}\n$$\n";
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(text));
        var bulletin = CodedSurfaceBulletinReader.Read(stream, new DateTimeOffset(Run));
        var processor = new SurfaceAnalysisDatasetProcessor(bulletin, "line.txt");
        return await processor.RenderHeadlessAsync(
            800, 64, new RenderContext { Viewport = Viewport(-72, -48, 39.4, 40.6, 800, 64, 10_000_000) }, cancellationToken: Ct);
    }

    // The number of pixel rows above (or below) the line, clear of its pen
    // (1.1 px either side), that hold ink: the pips on that side.
    private static int InkRows(SKBitmap bitmap, bool above)
    {
        int middle = bitmap.Height / 2;
        int rows = 0;
        for (int offset = 3; offset < middle; offset++)
        {
            int y = above ? middle - offset : middle + offset;
            bool ink = false;
            for (int x = 0; x < bitmap.Width && !ink; x++)
            {
                var p = bitmap.GetPixel(x, y);
                ink = (p.Red + p.Green + p.Blue) / 3 < 128;
            }

            if (ink)
                rows++;
        }

        return rows;
    }

    // Mean sea level pressure with a 996 hPa low at 45°N 50°W and a 1032 hPa
    // high at 37°N 30°W, on a 1° grid.
    private static WeatherCoverageSource AtlanticPressure()
    {
        const int rows = 31, cols = 56;
        const double south = 24, west = -76;
        var values = new float[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double lat = south + r, lon = west + c;
                double low = -20 * Math.Exp(-(Square(lat - 45) + Square((lon + 50) * 0.75)) / 60.0);
                double high = 16 * Math.Exp(-(Square(lat - 37) + Square((lon + 30) * 0.75)) / 90.0);
                values[r * cols + c] = (float)((1016 + low + high) * 100);
            }
        }

        var grid = new WeatherNativeGrid(rows, cols, south, west, 1, 1);
        var definition = WeatherParameters.Find(WeatherParameters.PressureMsl)!;
        WeatherParameter[] parameters =
            [new(definition, definition.Level ?? WeatherLevel.Surface, [WeatherTimeStep.FromValues(Run, values)])];
        return new WeatherCoverageSource(
            new WeatherGridSet(new WeatherOrigin("TEST", Run), grid, parameters), WeatherParameters.PressureGroup, parameters);

        static double Square(double x) => x * x;
    }

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

    // The paper of the isobar renders: white by day, the chart's dark blues at dusk and night.
    private static RgbaColor Background(PaletteType palette) => palette switch
    {
        PaletteType.Dusk => RgbaColor.FromHex("#3B4250"),
        PaletteType.Night => RgbaColor.FromHex("#0B0E14"),
        _ => RgbaColor.FromHex("#FFFFFF"),
    };
}
