using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.TestSupport;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #1294: <c>s100 info</c> describes an NHC tropical cyclone advisory:
/// the storm, the advisory, the forecast span and intensity, and every
/// forecast point with its radii.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class TropicalCycloneCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tc-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Info_describes_the_storm_the_advisory_and_its_forecast()
    {
        var dataset = TropicalCycloneFixtures.CopyIan(_dir);

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("TC", stdout);
        Assert.Contains("1.0.0", stdout);
        Assert.Contains("not for navigation", stdout);
        Assert.Contains("Major Hurricane Ian (AL092022), North Atlantic", stdout);
        Assert.Contains("20, issued 2022-09-28 03:00 UTC", stdout);
        Assert.Contains("2022-09-28 00:00 UTC to 2022-10-02 00:00 UTC (96 h, 8 points)", stdout);
        Assert.Contains("105 kt, gusts 130 kt, 952 hPa, category 3, peak forecast 115 kt", stdout);
        Assert.Contains("days 1–3 and 4–5", stdout);
        Assert.Contains("at 6 of 8 forecast points", stdout);
        Assert.Contains("points from 2022-09-22 18:00 UTC", stdout);

        Assert.Contains("Forecast points", stdout);
        Assert.Contains("24.9°N 82.9°W", stdout);
        Assert.Contains("120/120/100/120", stdout);
        Assert.Contains("35/35/20/20", stdout);
        Assert.Contains("STD", stdout);
    }

    [Fact]
    public void Info_on_a_forecast_zip_alone_says_what_is_missing()
    {
        var dataset = Path.Combine(_dir, "al092022_5day_020.zip");
        File.Copy(TropicalCycloneFixtures.IanForecast, dataset);

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("none (no best track read)", stdout);
        Assert.Matches(@"Wind radii\s*│\s*none", stdout);
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
