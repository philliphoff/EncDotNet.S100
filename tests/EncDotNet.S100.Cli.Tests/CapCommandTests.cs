using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.TestSupport;
using SkiaSharp;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #1069: <c>s100 info</c> lists a CAP file's warnings, when each is in
/// force and the zone codes the marine zone layer doesn't have, and
/// <c>s100 render</c> draws the warnings in force at a time step.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class CapCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cap-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Info_lists_the_alerts_and_the_codes_not_found()
    {
        var dataset = CapFixtures.CopyFixture(CapFixtures.Circle, Path.Combine(_dir, "circle.xml"));

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("CAP", stdout);
        Assert.Contains("1.2.0", stdout);
        Assert.Contains("SoundCharts portrayal: CAP 1.2 defines no symbology", stdout);
        Assert.Contains("Warnings", stdout);
        Assert.Contains("Hazardous Seas Warning", stdout);
        Assert.Contains("Extreme", stdout);
        Assert.Contains("2026-08-28 15:00", stdout);
        Assert.Contains("2026-08-28 18:00", stdout);
        Assert.Contains("circle", stdout);
        Assert.Contains("zones ANZ450; not found: ANZ999", stdout);
        Assert.Contains("Zone codes not in the marine zone layer: ANZ999", stdout);
        Assert.Contains("Time steps", stdout);
    }

    [Fact]
    public void Info_lists_every_alert_of_an_NWS_feed()
    {
        var dataset = CapFixtures.CopyFixture(CapFixtures.Atom, Path.Combine(_dir, "alerts.atom"));

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("Special Marine Warning", stdout);
        Assert.Contains("polygon", stdout);
        Assert.Contains("zones GMZ475", stdout);
        Assert.Contains("zones GMZ452, GMZ455, GMZ472", stdout);
        Assert.DoesNotContain("not found", stdout);
    }

    [Fact]
    public void Render_draws_the_warnings_in_force_at_the_time_step()
    {
        var dataset = CapFixtures.CopyFixture(CapFixtures.Circle, Path.Combine(_dir, "circle.xml"));
        var during = Path.Combine(_dir, "during.png");
        var after = Path.Combine(_dir, "after.png");

        // Step 0 is the onset; step 1 the tick after it expires.
        Assert.Equal(0, CliApp.Build().Run(["render", dataset, during, "--width", "320", "--height", "320", "--time-step", "0"], TestContext.Current.CancellationToken));
        Assert.Equal(0, CliApp.Build().Run(["render", dataset, after, "--width", "320", "--height", "320", "--time-step", "1"], TestContext.Current.CancellationToken));

        using var a = SKBitmap.Decode(during);
        using var b = SKBitmap.Decode(after);
        Assert.Equal(320, a.Width);
        Assert.True(InkFraction(a) > 0.02, $"{InkFraction(a):P1} ink while in force");
        Assert.Equal(0, InkFraction(b));
    }

    [Fact]
    public void Render_draws_a_zone_coded_alert()
    {
        var dataset = CapFixtures.CopyFixture(CapFixtures.MarineWeatherStatement, Path.Combine(_dir, "mws.cap"));
        var output = Path.Combine(_dir, "mws.png");

        Assert.Equal(0, CliApp.Build().Run(["render", dataset, output, "--width", "320", "--height", "320"], TestContext.Current.CancellationToken));

        using var bitmap = SKBitmap.Decode(output);
        Assert.True(InkFraction(bitmap) > 0.02);
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

    private static double InkFraction(SKBitmap bitmap)
    {
        int ink = 0, total = 0;
        for (int y = 0; y < bitmap.Height; y += 2)
        {
            for (int x = 0; x < bitmap.Width; x += 2)
            {
                total++;
                if (bitmap.GetPixel(x, y) != SKColors.White)
                    ink++;
            }
        }

        return (double)ink / total;
    }
}
