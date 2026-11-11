using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using static EncDotNet.S100.TestSupport.GribFixtures;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1075: a folder holding one model run in hourly GRIB2 files is one
/// dataset through the registry and both processor routes (a path, and a
/// file-system asset source as the viewer's Library opens it).
/// </summary>
public sealed class GribRunFolderProcessorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("grib-run-folder-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteRun(string name = "00")
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        var bytes = File.ReadAllBytes(Fixture(Waves));
        var messages = new List<byte[]>();
        for (var position = 0; position < bytes.Length;)
        {
            long length = 0;
            for (int i = 8; i < 16; i++)
                length = (length << 8) | bytes[position + i];
            messages.Add(bytes[position..(position + (int)length)]);
            position += (int)length;
        }

        for (int step = 0; step < 3; step++)
            File.WriteAllBytes(Path.Combine(folder, $"gfswave.t00z.global.0p25.f{step * 3:000}.grib2"), [.. messages.Skip(step * 3).Take(3).SelectMany(m => m)]);
        return folder;
    }

    [Fact]
    public void A_run_folder_is_detected_as_GRIB2_and_another_folder_is_not()
    {
        var run = WriteRun();
        var other = Directory.CreateDirectory(Path.Combine(_root, "notes")).FullName;
        File.WriteAllText(Path.Combine(other, "readme.txt"), "GRIB");

        Assert.Equal("GRIB2", DatasetPipelineFactory.DetectProductSpec(run));
        Assert.Null(DatasetPipelineFactory.DetectProductSpec(other));
    }

    [Fact]
    public void A_run_folder_opens_by_path_with_every_hour_as_a_step()
    {
        using var processor = new GribDatasetProcessor(WriteRun(), new ProjNetCrsTransformFactory());

        Assert.Equal([Run, Run.AddHours(3), Run.AddHours(6)], processor.AvailableTimes);
        Assert.Equal("GFS-Wave", processor.ForecastRun?.Model);
    }

    [Fact]
    public void A_run_folder_opens_from_a_file_system_source_as_the_viewers_library_opens_it()
    {
        WriteRun();
        using var source = FileSystemAssetSource.Create(_root);

        using var processor = new GribDatasetProcessor(source, "00", new ProjNetCrsTransformFactory());

        Assert.Equal(3, processor.AvailableTimes.Count);
    }

    [Fact]
    public void A_run_folders_metadata_covers_its_hours()
    {
        var metadata = WeatherProducts.Grib2.ReadMetadata!(WriteRun());

        Assert.Equal("GRIB2", metadata.Spec.Name);
        Assert.Equal(new TimeCoverage(Run, Run.AddHours(6)), metadata.TimeCoverage);
    }
}
