using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class CachingDatasetMetadataReaderTests : IDisposable
{
    private readonly string _cacheDir;

    public CachingDatasetMetadataReaderTests()
    {
        _cacheDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "dmeta-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }

    private static string S101Cell() =>
        System.IO.Path.Combine("TestData", "S101", "DATASET_FILES", "101AA00DS0003.000");

    [Fact]
    public void Returns_null_for_empty_path()
    {
        var reader = new CachingDatasetMetadataReader(
            new DiskDatasetMetadataCache(_cacheDir, 1_000_000));

        Assert.Null(reader.TryRead(string.Empty));
    }

    [Fact]
    public void Returns_null_for_unrecognised_product()
    {
        var file = System.IO.Path.Combine(_cacheDir, "not-a-dataset.txt");
        File.WriteAllText(file, "hello");
        var reader = new CachingDatasetMetadataReader(
            new DiskDatasetMetadataCache(_cacheDir, 1_000_000));

        Assert.Null(reader.TryRead(file));
    }

    [SkippableFact]
    public void Reads_s101_extent_then_serves_a_hit()
    {
        var cell = S101Cell();
        Skip.IfNot(File.Exists(cell), $"S-101 test cell not present: {cell}");

        var cache = new DiskDatasetMetadataCache(_cacheDir, 1_000_000);
        var reader = new CachingDatasetMetadataReader(cache);

        var first = reader.TryRead(cell);
        Assert.NotNull(first);
        Assert.Equal("S-101", first!.Spec.Name);
        Assert.NotNull(first.Extent);
        Assert.Equal(1, cache.Misses);

        var second = reader.TryRead(cell);
        Assert.NotNull(second);
        Assert.Equal(first.Extent, second!.Extent);
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }
}
