using System.Text.Json;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.TestSupport;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #1076: <c>s100 identify</c> (the headless pick) samples GRIB2 and
/// NetCDF grids and finds CAP warning areas, with the not-for-navigation
/// notice; <c>s100 info</c> gives a weather grid's model, run and grid; and
/// <c>s100 list-specs</c> lists the weather formats.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class WeatherCommandTests : IDisposable
{
    // Inside the hand-written CAP circle (30 km round 38.5°N 74.5°W), which
    // lies inside the GRIB2 crop off the US East Coast.
    private const string CircleLatitude = "38.5";
    private const string CircleLongitude = "-74.5";

    private readonly string _dir = Directory.CreateTempSubdirectory("weather-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Identify_samples_a_GRIB2_grid_at_the_requested_time()
    {
        var dataset = GribFixtures.CopyFixture(GribFixtures.WaveFilter, Path.Combine(_dir, "filter_gfswave.pl"));
        var (latitude, longitude) = GribFixtures.CropPoint(566);

        var (exit, stdout) = RunCapturingStdout(
            ["identify", dataset, "--lat", Invariant(latitude), "--lon", Invariant(longitude),
             "--time", "2026-10-25T03:00:00Z", "--format", "json"]);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Equal("Weather model data, not for navigation.", root.GetProperty("notices")[0].GetString());
        var sample = Assert.Single(root.GetProperty("samples").EnumerateArray());
        Assert.Equal("GRIB2", sample.GetProperty("spec").GetString());
        var value = sample.GetProperty("value");
        Assert.Equal("fields", value.GetProperty("kind").GetString());
        Assert.StartsWith("2026-10-25T03:00:00", value.GetProperty("sampleTime").GetString());
        var height = value.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "wave.hs");
        Assert.Equal("m", height.GetProperty("unit").GetString());
        Assert.Equal(GribFixtures.FilterAt566.Height[1], height.GetProperty("value").GetDouble(), 1e-4);
    }

    [Fact]
    public void Identify_prints_a_GRIB2_sample_as_a_table()
    {
        var dataset = GribFixtures.CopyFixture(GribFixtures.WaveFilter, Path.Combine(_dir, "waves.grib2"));
        var (latitude, longitude) = GribFixtures.CropPoint(566);

        var (exit, stdout) = RunCapturingAnsiConsole(
            ["identify", dataset, "--lat", Invariant(latitude), "--lon", Invariant(longitude), "--spec", "grib2"]);

        Assert.Equal(0, exit);
        Assert.Contains("Weather model data, not for navigation.", stdout);
        Assert.Contains("Coverage samples", stdout);
        Assert.Contains("GRIB2", stdout);
        Assert.Contains("wave.hs 2.52 m", stdout);
        Assert.Contains("@ 2026-10-25 00:00Z", stdout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Identify_samples_a_NetCDF_grid(bool netCdf3)
    {
        var dataset = Path.Combine(_dir, netCdf3 ? "ww3.nc" : "waves.nc");
        if (netCdf3)
            NetCdfFixtures.WriteWaveNetCdf3(dataset);
        else
            NetCdfFixtures.WriteWaveNetCdf4(dataset);
        var grid = NetCdfFixtures.ChartGrid;

        var (exit, stdout) = RunCapturingStdout(
            ["identify", dataset, "--lat", Invariant(grid.South + 5 * grid.Spacing), "--lon", Invariant(grid.West + 6 * grid.Spacing),
             "--time", "2026-10-25T06:00:00Z", "--spec", "netcdf", "--format", "json"]);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout);
        var sample = Assert.Single(doc.RootElement.GetProperty("samples").EnumerateArray());
        Assert.Equal("NetCDF", sample.GetProperty("spec").GetString());
        var height = sample.GetProperty("value").GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == "wave.hs");
        Assert.Equal(NetCdfFixtures.WaveHeight(5, 6, 2), height.GetProperty("value").GetDouble(), 3);
    }

    [Fact]
    public void Identify_finds_the_CAP_warning_area_under_the_point()
    {
        var dataset = CapFixtures.CopyFixture(CapFixtures.Circle, Path.Combine(_dir, "circle.xml"));

        var (exit, stdout) = RunCapturingStdout(
            ["identify", dataset, "--lat", CircleLatitude, "--lon", CircleLongitude, "--attributes", "--format", "json"]);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Equal(
            "Weather warning (CAP), not an S-100 product and not for navigation.",
            root.GetProperty("notices")[0].GetString());
        var feature = Assert.Single(root.GetProperty("features").EnumerateArray());
        Assert.Equal("CAP", feature.GetProperty("spec").GetString());
        Assert.Equal("CapWarningArea", feature.GetProperty("featureType").GetString());
        Assert.Equal("surface", feature.GetProperty("geometry").GetString());
        Assert.Equal("inside", feature.GetProperty("containment").GetString());
        var attributes = feature.GetProperty("attributes").GetProperty("attributes");
        Assert.Equal("Hazardous Seas Warning", attributes.GetProperty("event").GetString());
        Assert.Equal("Extreme", attributes.GetProperty("severity").GetString());
        Assert.StartsWith("2026-08-28T18:00:00", attributes.GetProperty("expires").GetString());
        Assert.Empty(root.GetProperty("samples").EnumerateArray());
    }

    [Fact]
    public void Identify_outside_every_CAP_area_finds_nothing()
    {
        var dataset = CapFixtures.CopyFixture(CapFixtures.Circle, Path.Combine(_dir, "circle.xml"));

        var (exit, stdout) = RunCapturingStdout(
            ["identify", dataset, "--lat", "36.0", "--lon", "-74.5", "--format", "json"]);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Empty(doc.RootElement.GetProperty("features").EnumerateArray());
        Assert.False(doc.RootElement.TryGetProperty("notices", out _));
    }

    [Fact]
    public void Identify_picks_a_CAP_warning_and_a_wave_grid_together()
    {
        var warnings = CapFixtures.CopyFixture(CapFixtures.Circle, Path.Combine(_dir, "circle.xml"));
        var waves = GribFixtures.CopyFixture(GribFixtures.WaveFilter, Path.Combine(_dir, "waves.grib2"));

        var (exit, stdout) = RunCapturingAnsiConsole(
            ["identify", "--layer", warnings, "--layer", waves, "--lat", CircleLatitude, "--lon", CircleLongitude]);

        Assert.Equal(0, exit);
        Assert.Contains("Weather warning (CAP), not an S-100 product and not for navigation.", stdout);
        Assert.Contains("Weather model data, not for navigation.", stdout);
        Assert.Contains("CapWarningArea", stdout);
        Assert.Contains("wave.hs", stdout);
    }

    [Fact]
    public void Info_gives_a_GRIB2_files_model_run_and_grid()
    {
        var dataset = GribFixtures.CopyFixture(GribFixtures.WaveFilter, Path.Combine(_dir, "filter_gfswave.pl"));

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("Weather model data, not for navigation.", stdout);
        Assert.Contains("GFS-Wave", stdout);
        Assert.Contains("2026-10-25 00:00", stdout);
        Assert.Contains("regular latitude/longitude, 33 × 33 nodes, 0.25° × 0.25°", stdout);
        Assert.Contains("-76.125,31.875,-67.875,40.125", stdout);
        Assert.Contains("Available time steps", stdout);
        Assert.Contains("2026-10-25 06:00:00", stdout);
    }

    [Fact]
    public void Info_lists_a_NetCDF_files_parameters_levels_steps_and_run()
    {
        var dataset = NetCdfFixtures.WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc"));

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("NetCDF", stdout);
        Assert.Contains("SoundCharts portrayal: NetCDF-CF defines no symbology", stdout);
        Assert.Contains("Weather model data, not for navigation.", stdout);
        Assert.Contains("Run (UTC)", stdout);
        Assert.Contains("2026-10-25 00:00", stdout);
        Assert.Contains("regular latitude/longitude, 20 × 20 nodes, 0.01° × 0.01°", stdout);
        Assert.Contains("Weather parameters", stdout);
        Assert.Contains("wave.hs (VHM0)", stdout);
        Assert.Contains("wave.dir (VMDR)", stdout);
        Assert.Contains("wave.period (VTPK)", stdout);
        Assert.Contains("2026-10-25 03:00:00", stdout);
    }

    [Fact]
    public void Info_counts_a_CAP_files_alerts()
    {
        var dataset = CapFixtures.CopyFixture(CapFixtures.Circle, Path.Combine(_dir, "circle.xml"));

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("Weather warning (CAP), not an S-100 product and not for navigation.", stdout);
        Assert.Contains("1 (2 areas, 2 drawn)", stdout);
        Assert.DoesNotContain("Run (UTC)", stdout);
    }

    [Fact]
    public void List_specs_lists_the_weather_formats()
    {
        var (exit, stdout) = RunCapturingAnsiConsole(["list-specs"]);

        Assert.Equal(0, exit);
        Assert.Contains("GRIB2", stdout);
        Assert.Contains("NetCDF", stdout);
        Assert.Contains("CAP", stdout);
        Assert.Contains("SoundCharts, in code", stdout);
        Assert.Contains("aren't S-100 products: weather data, not for navigation.", stdout);
    }

    private static string Invariant(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static (int Exit, string Stdout) RunCapturingStdout(string[] args)
    {
        var original = Console.Out;
        var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            int exit = CliApp.Build().Run(args);
            return (exit, buffer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static (int Exit, string Stdout) RunCapturingAnsiConsole(string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 1000;

        var original = AnsiConsole.Console;
        AnsiConsole.Console = console;
        try
        {
            int exit = CliApp.Build(console: console).Run(args);
            return (exit, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = original;
        }
    }
}
