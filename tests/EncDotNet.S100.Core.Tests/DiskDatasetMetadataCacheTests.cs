using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Core.Tests;

public class DiskDatasetMetadataCacheTests : IDisposable
{
    private readonly string _cacheDir;
    private readonly string _sourceDir;

    public DiskDatasetMetadataCacheTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "dmeta-tests-" + Guid.NewGuid().ToString("N"));
        _cacheDir = Path.Combine(root, "cache");
        _sourceDir = Path.Combine(root, "src");
        Directory.CreateDirectory(_cacheDir);
        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_cacheDir)!, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }

    private string WriteSource(string name, byte[] content)
    {
        var path = Path.Combine(_sourceDir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static DatasetMetadata Sample() => new()
    {
        Spec = new SpecRef("S-102", new SpecVersion(2, 1, 0)),
        Extent = new BoundingBox(1, 2, 3, 4),
        HorizontalCrsEpsg = 32610,
    };

    [Fact]
    public void Miss_then_hit_avoids_second_parse()
    {
        var source = WriteSource("a.h5", [1, 2, 3]);
        var cache = new DiskDatasetMetadataCache(_cacheDir, 1_000_000);
        var calls = 0;

        var first = cache.GetOrRead(source, _ => { calls++; return Sample(); });
        var second = cache.GetOrRead(source, _ => { calls++; return Sample(); });

        Assert.Equal(1, calls);
        Assert.Equal(first, second);
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void Hit_survives_a_new_cache_instance()
    {
        var source = WriteSource("b.h5", [4, 5, 6]);
        new DiskDatasetMetadataCache(_cacheDir, 1_000_000)
            .GetOrRead(source, _ => Sample());

        var fresh = new DiskDatasetMetadataCache(_cacheDir, 1_000_000);
        var calls = 0;
        var restored = fresh.GetOrRead(source, _ => { calls++; return Sample(); });

        Assert.Equal(0, calls);
        Assert.Equal(Sample(), restored);
        Assert.Equal(1, fresh.Hits);
    }

    [Fact]
    public void Content_change_invalidates_entry()
    {
        var source = WriteSource("c.h5", [1, 1, 1]);
        var cache = new DiskDatasetMetadataCache(_cacheDir, 1_000_000);
        cache.GetOrRead(source, _ => Sample());

        // Rewrite with a different length + newer mtime.
        File.WriteAllBytes(source, [1, 1, 1, 9]);
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(5));

        var calls = 0;
        cache.GetOrRead(source, _ => { calls++; return Sample(); });

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Mtime_change_alone_invalidates_entry()
    {
        var source = WriteSource("d.h5", [7, 7, 7]);
        var cache = new DiskDatasetMetadataCache(_cacheDir, 1_000_000);
        cache.GetOrRead(source, _ => Sample());

        // Same length, later write time.
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(1));

        var calls = 0;
        cache.GetOrRead(source, _ => { calls++; return Sample(); });

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Corrupt_sidecar_is_a_miss_and_never_throws()
    {
        var source = WriteSource("e.h5", [2, 2]);
        var cache = new DiskDatasetMetadataCache(_cacheDir, 1_000_000);
        cache.GetOrRead(source, _ => Sample());

        foreach (var f in Directory.GetFiles(_cacheDir, "*.dmeta"))
            File.WriteAllBytes(f, [0xFF, 0xFF, 0xFF]);

        var calls = 0;
        var result = cache.GetOrRead(source, _ => { calls++; return Sample(); });

        Assert.Equal(1, calls);
        Assert.Equal(Sample(), result);
    }

    [Fact]
    public void TryGet_returns_false_when_absent_and_does_not_produce()
    {
        var source = WriteSource("f.h5", [3]);
        var cache = new DiskDatasetMetadataCache(_cacheDir, 1_000_000);

        Assert.False(cache.TryGet(source, out _));

        cache.GetOrRead(source, _ => Sample());
        Assert.True(cache.TryGet(source, out var got));
        Assert.Equal(Sample(), got);
    }

    [Fact]
    public void Missing_source_still_returns_producer_value_without_persisting()
    {
        var missing = Path.Combine(_sourceDir, "does-not-exist.h5");
        var cache = new DiskDatasetMetadataCache(_cacheDir, 1_000_000);

        var result = cache.GetOrRead(missing, _ => Sample());

        Assert.Equal(Sample(), result);
        Assert.Empty(Directory.GetFiles(_cacheDir, "*.dmeta"));
    }
}
