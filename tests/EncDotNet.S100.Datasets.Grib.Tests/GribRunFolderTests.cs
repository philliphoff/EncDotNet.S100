using EncDotNet.S100.Datasets.Weather;
using static EncDotNet.S100.TestSupport.GribFixtures;

namespace EncDotNet.S100.Datasets.Grib.Tests;

/// <summary>
/// Issue #1075: a folder holding one model run, one GRIB2 file per forecast
/// hour (as NOMADS and AWS publish them), read as one dataset: the files
/// appended, fields merged by parameter, steps in valid-time order, the
/// latest run kept when runs are mixed.
/// </summary>
public sealed class GribRunFolderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "grib-run-" + Guid.NewGuid().ToString("N"));

    public GribRunFolderTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Splits appended messages (Section 0 gives each one's length).</summary>
    private static IReadOnlyList<byte[]> Split(byte[] grib)
    {
        var messages = new List<byte[]>();
        var position = 0;
        while (position + 16 <= grib.Length)
        {
            long length = 0;
            for (int i = 8; i < 16; i++)
                length = (length << 8) | grib[position + i];
            messages.Add(grib[position..(position + (int)length)]);
            position += (int)length;
        }

        return messages;
    }

    /// <summary>Writes the wave fixture as one file per forecast hour, the names in reverse of the hours.</summary>
    private void WriteHours(string prefix = "gfswave.t00z.global.0p25")
    {
        var messages = Split(File.ReadAllBytes(TestData.Path(Waves)));
        for (int step = 0; step < 3; step++)
            File.WriteAllBytes(Path.Combine(_folder, $"{prefix}.f{step * 3:000}.grib2"), [.. messages.Skip(step * 3).Take(3).SelectMany(m => m)]);
    }

    [Fact]
    public void A_folder_of_hourly_files_reads_as_the_appended_file_would()
    {
        WriteHours();
        File.WriteAllText(Path.Combine(_folder, "gfswave.t00z.global.0p25.f000.grib2.idx"), "1:0:d=2026102500:HTSGW:surface:anl:\n");
        File.WriteAllText(Path.Combine(_folder, "README.txt"), "not GRIB");

        Assert.Equal(3, GribRunFolder.Files(_folder).Count);
        Assert.True(GribRunFolder.IsRunFolder(_folder));
        using var folder = GribWeatherDataset.OpenFolder(_folder);
        using var appended = GribWeatherDataset.Open(TestData.Path(Waves));

        Assert.Equal(Run, folder.ReferenceTime);
        Assert.Equal(appended.Times, folder.Times);
        Assert.Equal("GFS-Wave", folder.Model);
        Assert.Equal(
            appended.GridSet.Parameters.Select(p => (p.Id, p.Steps.Count)),
            folder.GridSet.Parameters.Select(p => (p.Id, p.Steps.Count)));
        var height = folder.GridSet.FindParameter(WeatherParameters.WaveHeight)!;
        var expected = appended.GridSet.FindParameter(WeatherParameters.WaveHeight)!;
        for (int step = 0; step < 3; step++)
            Assert.Equal(appended.GridSet.ReadStep(expected, step, Ct), folder.GridSet.ReadStep(height, step, Ct));
    }

    [Fact]
    public void Files_given_out_of_order_still_give_steps_in_valid_time_order()
    {
        WriteHours();
        var files = GribRunFolder.Files(_folder).Reverse().ToArray();

        using var dataset = GribWeatherDataset.Open(files);

        Assert.Equal([Run, Run.AddHours(3), Run.AddHours(6)], dataset.Times);
    }

    [Fact]
    public void A_folder_mixing_runs_keeps_the_latest_and_lists_the_older_runs_fields()
    {
        var grid = new Grid(10, 10, 1, 2, 2);
        byte[] Height(DateTime run, int hours, double value) =>
            Encode(new Message { Discipline = 10, Number = 3, Process = 11, ReferenceTime = run, ForecastHours = hours, Grid = grid, Values = [value, value, value, value] });
        var older = Run.AddHours(-6);
        File.WriteAllBytes(Path.Combine(_folder, "gfswave.t18z.f006"), Height(older, 6, 1));
        File.WriteAllBytes(Path.Combine(_folder, "gfswave.t00z.f000"), Height(Run, 0, 3));
        File.WriteAllBytes(Path.Combine(_folder, "gfswave.t00z.f003"), Height(Run, 3, 4));

        using var dataset = GribWeatherDataset.OpenFolder(_folder);

        Assert.Equal(Run, dataset.ReferenceTime);
        Assert.Equal([Run, Run.AddHours(3)], dataset.Times);
        var ignored = Assert.Single(dataset.IgnoredFields);
        Assert.Equal("It's from an older model run (2026-10-24 18Z); the file's latest run, 2026-10-25 00Z, is used.", ignored.Reason);
        Assert.Equal(4f, dataset.GridSet.ReadStep(dataset.GridSet.Parameters[0], 1, Ct)[0], 3);
    }

    [Fact]
    public void A_folder_without_grib2_files_is_not_a_run()
    {
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "GRIB? no");

        Assert.False(GribRunFolder.IsRunFolder(_folder));
        Assert.Throws<UnsupportedGribException>(() => GribWeatherDataset.OpenFolder(_folder));
    }

    [Fact]
    public void Reading_a_field_across_the_files_after_opening_works_and_disposing_closes_every_file()
    {
        WriteHours();
        var dataset = GribWeatherDataset.OpenFolder(_folder);
        var height = dataset.GridSet.FindParameter(WeatherParameters.WaveHeight)!;
        Assert.Equal(1089, dataset.GridSet.ReadStep(height, 2, Ct).Length);

        dataset.Dispose();

        // Every file can be deleted once the dataset is closed (Windows refuses an open file).
        foreach (var file in Directory.EnumerateFiles(_folder))
            File.Delete(file);
        Assert.Empty(Directory.EnumerateFiles(_folder));
    }
}
