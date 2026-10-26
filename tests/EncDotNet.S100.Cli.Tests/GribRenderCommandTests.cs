using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.TestSupport;
using SkiaSharp;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #1074: <c>s100 render</c> draws the NOAA GRIB2 fixtures (a GFS-Wave
/// GRIB filter download with no GRIB extension, GFS 10 m wind) on their own,
/// at a chosen time step and composited, and <c>s100 info</c> lists what a
/// GRIB2 file holds and why anything isn't used.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class GribRenderCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grib-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(GribFixtures.WaveFilter, "filter_gfswave.pl")]
    [InlineData(GribFixtures.Waves, "gfswave.t00z.global.0p25.grib2")]
    public void Render_draws_a_GRIB2_wave_file(string fixture, string name)
    {
        var dataset = GribFixtures.CopyFixture(fixture, Path.Combine(_dir, name));
        var output = Path.Combine(_dir, "out.png");

        int exit = CliApp.Build().Run(["render", dataset, output, "--width", "320", "--height", "320"]);

        Assert.Equal(0, exit);
        using var bitmap = SKBitmap.Decode(output);
        Assert.NotNull(bitmap);
        Assert.Equal(320, bitmap.Width);
        Assert.True(InkFraction(bitmap) > 0.5, "The wave fill should cover most of the image.");
    }

    [Fact]
    public void The_time_step_option_selects_the_step_drawn()
    {
        var dataset = GribFixtures.CopyFixture(GribFixtures.WaveFilter, Path.Combine(_dir, "waves.grib2"));
        var first = Path.Combine(_dir, "t0.png");
        var last = Path.Combine(_dir, "t2.png");

        Assert.Equal(0, CliApp.Build().Run(["render", dataset, first, "--width", "200", "--height", "200", "--time-step", "0"]));
        Assert.Equal(0, CliApp.Build().Run(["render", dataset, last, "--width", "200", "--height", "200", "--time-step", "2"]));

        using var a = SKBitmap.Decode(first);
        using var b = SKBitmap.Decode(last);
        int changed = 0;
        for (int y = 0; y < a.Height; y += 2)
        {
            for (int x = 0; x < a.Width; x += 2)
            {
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                    changed++;
            }
        }

        Assert.True(changed > 500, $"Only {changed} sampled pixels differ between steps 0 and 2.");
    }

    [Fact]
    public void A_composite_draws_waves_and_wind_together()
    {
        var waves = GribFixtures.CopyFixture(GribFixtures.Waves, Path.Combine(_dir, "gfswave.grib2"));
        var wind = GribFixtures.CopyFixture(GribFixtures.Wind10m, Path.Combine(_dir, "gfs.grib2"));
        var output = Path.Combine(_dir, "composite.png");

        int exit = CliApp.Build().Run(
            ["render", "--layer", waves, "--layer", wind, output, "--width", "320", "--height", "320", "--time-step", "1",
             "--bbox", "-76,32,-68,40"]);

        Assert.Equal(0, exit);
        using var bitmap = SKBitmap.Decode(output);
        Assert.NotNull(bitmap);
        Assert.True(InkFraction(bitmap) > 0.5);
    }

    [Fact]
    public void A_JPEG_2000_only_file_fails()
    {
        var dataset = GribFixtures.CopyFixture(GribFixtures.Jpeg2000, Path.Combine(_dir, "wcoast.grib2"));

        int exit = CliApp.Build().Run(["render", dataset, Path.Combine(_dir, "wcoast.png")]);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(Path.Combine(_dir, "wcoast.png")));
    }

    [Fact]
    public void Info_lists_the_parameters_and_why_any_isnt_used()
    {
        var dataset = Path.Combine(_dir, "gfswave.grib2");
        File.WriteAllBytes(dataset, GribFixtures.Concat(
            File.ReadAllBytes(GribFixtures.Fixture(GribFixtures.WaveFilter)),
            File.ReadAllBytes(GribFixtures.Fixture(GribFixtures.Jpeg2000))));

        var (exit, stdout) = RunCapturingAnsiConsole(["info", dataset]);

        Assert.Equal(0, exit);
        Assert.Contains("GRIB2", stdout);
        Assert.Contains("SoundCharts portrayal: GRIB2 defines no symbology", stdout);
        Assert.Contains("Weather parameters", stdout);
        Assert.Contains("wave.hs (HTSGW)", stdout);
        Assert.Contains("grib2:10,0,8 (SWELL)", stdout);
        Assert.Contains("the first partition the file has", stdout);
        Assert.Contains("5.40 (JPEG 2000)", stdout);
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
        for (int y = 0; y < bitmap.Height; y += 4)
        {
            for (int x = 0; x < bitmap.Width; x += 4)
            {
                total++;
                if (bitmap.GetPixel(x, y) != SKColors.White)
                    ink++;
            }
        }

        return (double)ink / total;
    }
}
