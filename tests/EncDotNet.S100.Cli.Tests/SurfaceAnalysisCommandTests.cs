using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.TestSupport;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #1295: <c>s100 info</c> describes a WPC coded surface bulletin — what
/// it is, who issued it, that it covers WPC's domain only, and each analysis's
/// valid time and feature counts.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class SurfaceAnalysisCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("codsus-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Info_describes_a_high_resolution_analysis()
    {
        var dataset = CodsusFixtures.CopyFixture(CodsusFixtures.HighResolution, Path.Combine(_dir, "codsus_hr.txt"));

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("CODSUS", stdout);
        Assert.Contains("SoundCharts portrayal: WPC's coded surface bulletin defines no symbology", stdout);
        Assert.Contains("Weather analysis, not for navigation.", stdout);
        Assert.Contains("analysis · high resolution (0.1°) · CODSUS · ASUS02 KWBC 040000", stdout);
        Assert.Contains("NWS WEATHER PREDICTION CENTER COLLEGE PARK MD", stdout);
        Assert.Contains("WPC's domain only: -180,0,0,90", stdout);
        Assert.Contains("Valid (UTC)", stdout);
        Assert.Contains("Surface analyses", stdout);
        // Valid, lead, cold, warm, occluded, stationary, troughs, highs, lows.
        Assert.Matches(@"2026-11-04 00:00\s*│\s*analysis\s*│\s*13\s*│\s*8\s*│\s*6\s*│\s*18\s*│\s*29\s*│\s*15\s*│\s*29", stdout);
    }

    [Fact]
    public void Info_lists_each_forecast_time_of_a_forecast_bulletin()
    {
        var dataset = CodsusFixtures.CopyFixture(CodsusFixtures.Forecast, Path.Combine(_dir, "codsrp"));

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("forecast · standard (1°) · CODSRP", stdout);
        Assert.Matches(@"2026-11-05 12:00\s*│\s*12 h", stdout);
        Assert.Matches(@"2026-11-06 00:00\s*│\s*24 h", stdout);
    }

    [Fact]
    public void Info_names_codes_it_skipped()
    {
        var dataset = CodsusFixtures.CopyFixture(CodsusFixtures.Antimeridian, Path.Combine(_dir, "synthetic.txt"));

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("Skipped codes", stdout);
        Assert.Contains("DRYLN", stdout);
        Assert.Contains("2025-12-31 21:00", stdout);
    }

    [Fact]
    public void List_specs_lists_CODSUS_as_rendered_headless()
    {
        var (exit, stdout) = RunCapturingAnsiConsole(["list-specs"]);

        Assert.Equal(0, exit);
        Assert.Matches(@"CODSUS\s*│\s*SoundCharts, in code\s*│\s*yes", stdout);
        Assert.Contains("GRIB2, NetCDF, CAP, CODSUS aren't S-100 products", stdout);
    }

    [Fact]
    public void Render_draws_a_bulletin()
    {
        var dataset = CodsusFixtures.CopyFixture(CodsusFixtures.AtlanticLow, Path.Combine(_dir, "atlantic.txt"));
        var output = Path.Combine(_dir, "fronts.png");

        var (exit, stdout) = RunCapturingAnsiConsole(["render", dataset, output, "--width", "400", "--height", "300"]);

        Assert.Equal(0, exit);
        Assert.Contains("CODSUS", stdout);
        Assert.True(new FileInfo(output).Length > 1_000);
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
