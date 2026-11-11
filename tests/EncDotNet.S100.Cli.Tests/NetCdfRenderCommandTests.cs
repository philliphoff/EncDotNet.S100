using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.TestSupport;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #1071: <c>s100 render</c> draws generated NetCDF-CF files (NetCDF-4
/// and NetCDF-3) on their own, at a chosen time step, and composited.
/// </summary>
public sealed class NetCdfRenderCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("netcdf-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("waves.nc", false)]
    [InlineData("ww3.nc", true)]
    public void Render_draws_a_NetCDF_wave_file(string name, bool netCdf3)
    {
        var dataset = Path.Combine(_dir, name);
        if (netCdf3)
            NetCdfFixtures.WriteWaveNetCdf3(dataset);
        else
            NetCdfFixtures.WriteWaveNetCdf4(dataset);
        var output = Path.Combine(_dir, "out.png");

        int exit = CliApp.Build().Run(["render", dataset, output, "--width", "320", "--height", "320"], TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        using var bitmap = SKBitmap.Decode(output);
        Assert.NotNull(bitmap);
        Assert.Equal(320, bitmap.Width);
        Assert.True(InkFraction(bitmap) > 0.5, "The wave fill should cover most of the image.");
    }

    [Fact]
    public void The_time_step_option_selects_the_step_drawn()
    {
        var dataset = NetCdfFixtures.WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc"));
        var first = Path.Combine(_dir, "t0.png");
        var last = Path.Combine(_dir, "t2.png");

        Assert.Equal(0, CliApp.Build().Run(["render", dataset, first, "--width", "200", "--height", "200", "--time-step", "0"], TestContext.Current.CancellationToken));
        Assert.Equal(0, CliApp.Build().Run(["render", dataset, last, "--width", "200", "--height", "200", "--time-step", "2"], TestContext.Current.CancellationToken));

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

        Assert.True(changed > 2000, $"Only {changed} sampled pixels differ between steps 0 and 2.");
    }

    [Fact]
    public void A_composite_draws_waves_and_wind_together()
    {
        var waves = NetCdfFixtures.WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc"));
        var wind = NetCdfFixtures.WriteWindNetCdf4(Path.Combine(_dir, "wind.nc"));
        var output = Path.Combine(_dir, "composite.png");

        // No --bbox: the composite frames the 0.2° grids on their own extent
        // (issue #1097), so they fill most of the image.
        int exit = CliApp.Build().Run(
            ["render", "--layer", waves, "--layer", wind, output, "--width", "320", "--height", "320", "--time-step", "1"], TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        using var bitmap = SKBitmap.Decode(output);
        Assert.NotNull(bitmap);
        Assert.True(InkFraction(bitmap) > 0.5);
    }

    [Fact]
    public void A_NetCDF_file_without_CF_coordinates_fails()
    {
        var path = Path.Combine(_dir, "plain.nc");
        new PureHDF.H5File { ["x"] = new float[] { 1, 2 } }.Write(path);

        int exit = CliApp.Build().Run(["render", path, Path.Combine(_dir, "plain.png")], TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(Path.Combine(_dir, "plain.png")));
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
