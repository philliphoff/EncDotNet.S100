using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Testing.Scenarios.Hdf5;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for the SoundCharts S-104 water-level portrayal
/// (S-104 Ed 2.0.0 §9 defines none): station gauges and surface isolines in
/// the Day, Dusk and Night palettes (#937, #483).
/// </summary>
public sealed class S104RenderingTests
{
    // A Rotterdam-like station set: rising, falling, steady, unknown trend, and
    // a station whose sample at the step is the fill value.
    private const float StationLatitude = 51.90f;
    private const float StationLongitude = 4.40f;

    [Fact]
    public Task WaterLevel_FirstTimeStep_DayPalette()
    {
        var path = Path.Combine(
            TestHelpers.DatasetsRoot, "S104", "104US004SC1BO_20251217T12Z.h5");
        Assert.SkipUnless(File.Exists(path), $"S-104 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
            TimeStepIndex = 0,
        });

        return TestHelpers.VerifyBitmap(bitmap);
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public Task Stations_show_trend_and_value_at_harbour_scale(PaletteType palette)
    {
        var path = WriteStations();
        try
        {
            using var harness = new RenderHarness();
            var bitmap = harness.Render(path, new HarnessOptions
            {
                Width = 480,
                Height = 320,
                Palette = palette,
                BackgroundColor = Background(palette),
                // About 1:60 000, inside the 1:100 000 label limit.
                Viewport = new GeographicBounds(West: 4.33, South: 51.87, East: 4.47, North: 51.93),
            });

            Assert.True(TestHelpers.CountNonBackgroundPixels(bitmap, new SKColor(Background(palette))) > 500);
            return TestHelpers.VerifyBitmap(bitmap).UseParameters(palette);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public Task Stations_drop_their_labels_below_1_to_100000()
    {
        var path = WriteStations();
        try
        {
            using var harness = new RenderHarness();
            var bitmap = harness.Render(path, new HarnessOptions
            {
                Width = 480,
                Height = 320,
                // About 1:400 000.
                Viewport = new GeographicBounds(West: 3.95, South: 51.70, East: 4.85, North: 52.10),
            });

            return TestHelpers.VerifyBitmap(bitmap);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public Task Surface_draws_labelled_isolines_and_no_fill(PaletteType palette)
    {
        var path = WriteGrid(baseLevel: -0.6, slope: 0.45);
        try
        {
            using var harness = new RenderHarness();
            var bitmap = harness.Render(path, new HarnessOptions
            {
                Width = 480,
                Height = 400,
                Palette = palette,
                BackgroundColor = Background(palette),
                TimeStepIndex = 1,
            });

            Assert.True(TestHelpers.CountNonBackgroundPixels(bitmap, new SKColor(Background(palette))) > 500);
            return TestHelpers.VerifyBitmap(bitmap).UseParameters(palette);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public Task Surface_with_a_small_range_uses_quarter_metre_isolines()
    {
        var path = WriteGrid(baseLevel: 0.1, slope: 0.12);
        try
        {
            using var harness = new RenderHarness();
            var bitmap = harness.Render(path, new HarnessOptions { Width = 480, Height = 400 });
            return TestHelpers.VerifyBitmap(bitmap);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static uint Background(PaletteType palette) => palette switch
    {
        PaletteType.Dusk => 0xFF5B6B78u,
        PaletteType.Night => 0xFF0B0F14u,
        _ => 0xFFF6FBFEu,
    };

    private static string WriteStations()
    {
        var path = Path.Combine(Path.GetTempPath(), $"104NL00_STATIONS_{Guid.NewGuid():N}.h5");
        S104Dcf8FixtureBuilder.WriteFile(
            path,
            new[]
            {
                Station("RTM-HvH", 0.000f, -0.040f, 1.42f, 2),
                Station("RTM-Maassluis", 0.012f, -0.010f, 0.87f, 1),
                Station("RTM-Vlaardingen", -0.010f, 0.012f, -0.12f, 3),
                Station("RTM-Schiedam", 0.015f, 0.040f, 0.31f, 0),
                Station("RTM-Botlek", -0.018f, -0.022f, -9999f, 0),
            },
            waterLevelTrendThreshold: 0.05,
            productSpecification: "INT.IHO.S-104.2.0");
        return path;
    }

    private static S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow> Station(
        string id, float dLat, float dLon, float height, byte trend) => new()
        {
            Identifier = id,
            Latitude = StationLatitude + dLat,
            Longitude = StationLongitude + dLon,
            StartDateTime = "20261014T100000Z",
            EndDateTime = "20261014T110000Z",
            TimeRecordInterval = 3600,
            Values =
            [
                new() { WaterLevelHeight = height, WaterLevelTrend = trend },
                new() { WaterLevelHeight = height == -9999f ? -9999f : height + 0.1f, WaterLevelTrend = trend },
            ],
        };

    /// <summary>
    /// A 5 × 6 grid over the Rotterdam port area with three steps. Levels rise
    /// from south-west to north-east by <paramref name="slope"/> per node, with a
    /// gentle bulge, and the whole field rises 0.1 m a step.
    /// </summary>
    private static string WriteGrid(double baseLevel, double slope)
    {
        const int rows = 5, columns = 6;
        var path = Path.Combine(Path.GetTempPath(), $"104NL00_GRID_{Guid.NewGuid():N}.h5");
        var steps = new List<(string, S104FixtureBuilder.SpecRow[])>();
        for (int step = 0; step < 3; step++)
        {
            var values = new S104FixtureBuilder.SpecRow[rows * columns];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    double bulge = 0.3 * slope * Math.Sin(r * 0.9) * Math.Cos(c * 0.7);
                    values[r * columns + c] = new S104FixtureBuilder.SpecRow
                    {
                        WaterLevelHeight = (float)(baseLevel + step * 0.1 + slope * (0.6 * r + 0.4 * c) + bulge),
                        WaterLevelTrend = 2,
                    };
                }
            }
            // A missing corner node breaks the lines there.
            values[(rows - 1) * columns + columns - 1].WaterLevelHeight = -9999f;
            steps.Add(($"20261014T{10 + step:00}0000Z", values));
        }

        S104FixtureBuilder.WriteTimeSeries(
            path,
            new S104FixtureBuilder.GridGeoreference(51.86, 4.30, 0.02, 0.03, rows, columns),
            steps,
            new Dictionary<string, object>
            {
                ["productSpecification"] = "INT.IHO.S-104.2.0",
                ["horizontalCRS"] = 4326,
                ["geographicIdentifier"] = "Rotterdam",
                ["issueDate"] = "20261014",
                ["issueTime"] = "090000Z",
                ["verticalDatum"] = 12,
            });
        return path;
    }
}
