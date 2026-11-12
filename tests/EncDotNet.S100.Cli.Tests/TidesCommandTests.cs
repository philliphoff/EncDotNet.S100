using System.Text.Json;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S104.Writing;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// <c>s100 tides stations</c> and <c>s100 tides predict</c> (#1452) in-process,
/// over NOAA's Puget Sound stations (tests/datasets/Tides/Stations/puget-sound.json).
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class TidesCommandTests : IDisposable
{
    private const string Seattle = "9447130";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "s100-tides-" + Guid.NewGuid().ToString("N"));

    private static string Catalogue => Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", "puget-sound.json");

    public TidesCommandTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Stations_json_is_the_list_tide_stations_result()
    {
        var (exit, stdout, _) = Run(["tides", "stations", "--near", "47.6,-122.34", "--format", "json", "--catalogue", Catalogue]);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout);
        var stations = doc.RootElement.GetProperty("stations");
        Assert.Equal(Seattle, stations[0].GetProperty("id").GetString());
        Assert.Equal("harmonic", stations[0].GetProperty("kind").GetString());
        Assert.Equal("subordinate", stations[1].GetProperty("kind").GetString());
        Assert.Equal(Seattle, stations[1].GetProperty("referenceStationId").GetString());
        Assert.Equal("NOAA CO-OPS", doc.RootElement.GetProperty("catalogue").GetProperty("provider").GetString());
    }

    [Fact]
    public void Stations_csv_has_a_row_per_station_and_the_catalogue_age_on_stderr()
    {
        var (exit, stdout, stderr) = Run(["tides", "stations", "--near", "47.6,-122.34", "--radius", "50km", "--limit", "2", "--format", "csv", "--catalogue", Catalogue]);

        Assert.Equal(0, exit);
        var lines = stdout.TrimEnd().Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("id,name,kind,", lines[0]);
        Assert.StartsWith($"{Seattle},\"SEATTLE (Madison St.), Elliott Bay\",harmonic,", lines[1]);
        Assert.Contains("Stations: NOAA CO-OPS, retrieved", stderr);
    }

    [Fact]
    public void Predict_json_is_the_predict_tide_result_in_utc()
    {
        var (exit, stdout, _) = Run([
            "tides", "predict", "Seattle", "--from", "2026-11-13T12:00-08:00", "--to", "2026-11-13T18:00-08:00",
            "--step", "10m", "--format", "json", "--catalogue", Catalogue]);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Equal(Seattle, root.GetProperty("station").GetProperty("id").GetString());
        Assert.Equal("prediction", root.GetProperty("kind").GetString());
        Assert.Equal("MLLW", root.GetProperty("datum").GetString());
        Assert.Equal(600, root.GetProperty("stepSeconds").GetInt32());
        var series = root.GetProperty("series");
        Assert.Equal(37, series.GetArrayLength());
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 20, 0, 0, TimeSpan.Zero), series[0].GetProperty("time").GetDateTimeOffset());
        Assert.Equal("Astronomical tide only, not a forecast.", root.GetProperty("caveat").GetString());
    }

    [Fact]
    public void Predict_highs_and_lows_as_csv_in_utc()
    {
        var (exit, stdout, stderr) = Run([
            "tides", "predict", Seattle, "--highs-lows", "--from", "2026-11-13", "--to", "2026-11-14",
            "--utc", "--format", "csv", "--catalogue", Catalogue]);

        Assert.Equal(0, exit);
        var lines = stdout.TrimEnd().Split('\n');
        Assert.Equal("time,kind,predicted_m_above_mllw", lines[0]);
        Assert.InRange(lines.Length - 1, 3, 5);
        Assert.All(lines.Skip(1), line => Assert.Matches(@"^2026-11-1[34]T\d\d:\d\d:00Z,(high|low),-?\d+\.\d{3}$", line));
        Assert.Contains("not a forecast", stderr);
    }

    [Fact]
    public void Predict_near_a_point_uses_the_nearest_station()
    {
        var (exit, stdout, _) = Run(["tides", "predict", "--near", "47.27,-122.55", "--format", "json", "--catalogue", Catalogue]);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout);
        var station = doc.RootElement.GetProperty("station");
        Assert.Equal("9446486", station.GetProperty("id").GetString());
        Assert.Equal("subordinate", station.GetProperty("kind").GetString());
        Assert.True(station.GetProperty("distanceMeters").GetDouble() < 1_000);
    }

    [Fact]
    public void Predict_s104_writes_a_station_dataset_that_reads_back()
    {
        var output = Path.Combine(_directory, "tacoma.h5");

        var (exit, _, stderr) = Run([
            "tides", "predict", "9446486", "--from", "2026-11-13T00:00Z", "--to", "2026-11-14T00:00Z",
            "--step", "6m", "--format", "s104", "-o", output, "--catalogue", Catalogue]);

        Assert.Equal(0, exit);
        Assert.Contains("241 predicted heights above MLLW", stderr);
        using var file = PureHdfFile.Open(output);
        var data = Assert.IsType<S104DatasetData.StationSeries>(S104DatasetReader.ReadAny(file));
        Assert.True(S104TidePredictions.IsPrediction(data.Dataset));
        Assert.Contains("from SEATTLE (Madison St.), Elliott Bay 9447130, with offsets", data.Dataset.MethodWaterLevelProduct);
        var station = Assert.Single(data.Dataset.Stations);
        Assert.Equal("9446486", station.Identifier);
        Assert.Equal(241, station.NumberOfTimes);
        Assert.Equal(new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), station.StartTime);
    }

    [Fact]
    public void Predict_reports_an_unknown_station_and_a_missing_datum()
    {
        var unknown = Run(["tides", "predict", "Nowhere", "--catalogue", Catalogue]);
        var noLat = Run(["tides", "predict", "9446486", "--datum", "LAT", "--catalogue", Catalogue]);

        Assert.Equal(2, unknown.Exit);
        Assert.Contains("No tide station has the id or name 'Nowhere'", unknown.Stderr);
        Assert.Equal(2, noLat.Exit);
        Assert.Contains("can't be predicted on LAT; it can on MLLW", noLat.Stderr);
    }

    [Fact]
    public void A_missing_catalogue_says_how_to_get_one()
    {
        var missing = Path.Combine(_directory, "none.json.gz");

        var (exit, _, stderr) = Run(["tides", "stations", "--near", "47.6,-122.34", "--catalogue", missing]);

        Assert.Equal(2, exit);
        Assert.Contains($"s100 tides update --catalogue {missing}", stderr);
    }

    [Theory]
    [InlineData("tides", "predict")]
    [InlineData("tides", "predict", "9447130", "--near", "47.6,-122.3")]
    [InlineData("tides", "predict", "9447130", "--step", "soon")]
    [InlineData("tides", "predict", "9447130", "--from", "tomorrow")]
    [InlineData("tides", "predict", "9447130", "--highs-lows", "--format", "s104")]
    [InlineData("tides", "predict", "9447130", "-o", "out.h5")]
    [InlineData("tides", "stations")]
    [InlineData("tides", "stations", "--near", "147,-122")]
    [InlineData("tides", "stations", "--near", "47.6,-122.3", "--radius", "far")]
    public void Bad_arguments_are_refused(params string[] args)
    {
        var (exit, _, _) = Run([.. args, "--catalogue", Catalogue]);

        Assert.NotEqual(0, exit);
    }

    [Theory]
    [InlineData("10", 600)]
    [InlineData("10m", 600)]
    [InlineData("1h", 3600)]
    [InlineData("90s", 90)]
    [InlineData("00:15", 900)]
    public void Steps_parse_with_units(string text, int seconds)
    {
        Assert.True(TideCli.TryParseStep(text, out var step));
        Assert.Equal(TimeSpan.FromSeconds(seconds), step);
    }

    [Theory]
    [InlineData("20", 20_000)]
    [InlineData("20km", 20_000)]
    [InlineData("5000m", 5_000)]
    [InlineData("10nm", 18_520)]
    public void Distances_parse_with_units(string text, double meters)
    {
        Assert.True(TideCli.TryParseDistanceMeters(text, out var parsed));
        Assert.Equal(meters, parsed, 6);
    }

    [Fact]
    public void A_time_without_an_offset_is_in_the_given_zone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-8), "test", "test");

        Assert.True(TideCli.TryParseTime("2026-11-13T12:00", zone, out var local));
        Assert.True(TideCli.TryParseTime("2026-11-13", zone, out var date));
        Assert.True(TideCli.TryParseTime("2026-11-13T20:00Z", zone, out var utc));

        Assert.Equal(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(-8)), local);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.FromHours(-8)), date);
        Assert.Equal(local, utc);
    }

    private static (int Exit, string Stdout, string Stderr) Run(string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = CliApp.Build().Run(args);
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
