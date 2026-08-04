using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector.Caching;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Behaviour of <see cref="DiskLineLodCache"/>: round-trip fidelity,
/// cross-restart persistence (a second instance over the same directory
/// serves a hit), distinct-key misses, tolerant handling of corrupted /
/// truncated files, LRU size-cap eviction, and argument validation.
/// Mirrors <c>DiskPortrayalInstructionCacheTests</c> — the two caches share
/// the same "atomic write + version header + LRU + IO-error-swallowed"
/// contract by design.
/// </summary>
public class DiskLineLodCacheTests : IDisposable
{
    private readonly string _dir;

    public DiskLineLodCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "encdotnet-llodcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    private static LineLodPyramid Sample(int vertexOffset = 0)
    {
        var coords = new List<GeoPosition>();
        for (var i = 0; i <= 100; i++)
        {
            coords.Add(new GeoPosition(
                50.0 + i * 0.001 + vertexOffset * 0.0001,
                -1.0 + i * 0.001));
        }
        return LineLodPyramid.Build(coords, LineLodTolerances.HalfOctaveDefault);
    }

    private static void AssertPyramidsEqual(LineLodPyramid expected, LineLodPyramid actual)
    {
        Assert.Equal(expected.InputVertexCount, actual.InputVertexCount);
        Assert.Equal(expected.Levels.Count, actual.Levels.Count);
        for (var i = 0; i < expected.Levels.Count; i++)
        {
            var e = expected.Levels[i];
            var a = actual.Levels[i];
            Assert.Equal(e.ToleranceMetres, a.ToleranceMetres);
            Assert.Equal(e.IsPassthrough, a.IsPassthrough);
            Assert.Equal(e.Coordinates.Count, a.Coordinates.Count);
            for (var c = 0; c < e.Coordinates.Count; c++)
            {
                Assert.Equal(e.Coordinates[c].Latitude, a.Coordinates[c].Latitude);
                Assert.Equal(e.Coordinates[c].Longitude, a.Coordinates[c].Longitude);
            }
        }
    }

    [Fact]
    public void RoundTrip_HitDoesNotInvokeFactory()
    {
        var cache = new DiskLineLodCache(_dir, maxBytes: 16 * 1024 * 1024);
        var expected = Sample();

        var first = cache.GetOrCompute("k", () => expected);
        var second = cache.GetOrCompute("k", () => throw new InvalidOperationException("should not recompute"));

        AssertPyramidsEqual(expected, first);
        AssertPyramidsEqual(expected, second);
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void PersistsAcrossInstances()
    {
        var expected = Sample();
        var first = new DiskLineLodCache(_dir, maxBytes: 16 * 1024 * 1024);
        _ = first.GetOrCompute("k", () => expected);

        var reopened = new DiskLineLodCache(_dir, maxBytes: 16 * 1024 * 1024);
        var served = reopened.GetOrCompute("k", () => throw new InvalidOperationException("should not recompute"));

        AssertPyramidsEqual(expected, served);
        Assert.Equal(1, reopened.Hits);
        Assert.Equal(0, reopened.Misses);
    }

    [Fact]
    public void DistinctKeysAreDistinct()
    {
        var cache = new DiskLineLodCache(_dir, maxBytes: 16 * 1024 * 1024);
        var a = Sample(vertexOffset: 0);
        var b = Sample(vertexOffset: 1);

        _ = cache.GetOrCompute("a", () => a);
        _ = cache.GetOrCompute("b", () => b);

        var servedA = cache.GetOrCompute("a", () => throw new InvalidOperationException("should not recompute a"));
        var servedB = cache.GetOrCompute("b", () => throw new InvalidOperationException("should not recompute b"));

        AssertPyramidsEqual(a, servedA);
        AssertPyramidsEqual(b, servedB);
    }

    [Fact]
    public async Task ConcurrentWritesPersistAllEntries()
    {
        var cache = new DiskLineLodCache(_dir, maxBytes: 64 * 1024 * 1024);
        const int entryCount = 100;

        await Task.WhenAll(Enumerable.Range(0, entryCount).Select(index =>
            Task.Run(() => cache.GetOrCompute(
                $"key-{index}",
                () => Sample(index)))));

        Assert.Equal(entryCount, cache.Misses);
        Assert.Equal(entryCount, Directory.EnumerateFiles(_dir, "*.llod").Count());
    }

    [Fact]
    public void CorruptedFileFallsBackToMiss()
    {
        var cache = new DiskLineLodCache(_dir, maxBytes: 16 * 1024 * 1024);
        _ = cache.GetOrCompute("k", () => Sample());

        // Overwrite every persisted entry with garbage so the version /
        // magic check fails on the next read.
        foreach (var f in Directory.EnumerateFiles(_dir, "*.llod"))
        {
            File.WriteAllBytes(f, [1, 2, 3, 4]);
        }

        var recomputed = false;
        _ = cache.GetOrCompute("k", () =>
        {
            recomputed = true;
            return Sample();
        });

        Assert.True(recomputed);
    }

    [Fact]
    public void LruEvictsOldEntriesUnderSizeCap()
    {
        // First write one entry to a fresh cache with an unbounded cap so
        // we can measure the on-disk size accurately without triggering
        // the sweep.
        var probeCache = new DiskLineLodCache(_dir, maxBytes: 1024L * 1024 * 1024);
        _ = probeCache.GetOrCompute("probe", () => Sample(vertexOffset: 42));
        var probeFile = Directory.EnumerateFiles(_dir, "*.llod").Single();
        var entrySize = new FileInfo(probeFile).Length;
        File.Delete(probeFile);

        // A cap that comfortably fits one entry but cannot fit two. The
        // second write will therefore trip the LRU sweep and evict "a".
        var cap = entrySize + entrySize / 2;

        var cache = new DiskLineLodCache(_dir, maxBytes: cap);

        _ = cache.GetOrCompute("a", () => Sample(vertexOffset: 0));
        // Space the writes so mtime/atime ordering is unambiguous even on
        // filesystems with second-granularity timestamps.
        Thread.Sleep(1100);
        _ = cache.GetOrCompute("b", () => Sample(vertexOffset: 1));

        var recomputed = false;
        _ = cache.GetOrCompute("a", () =>
        {
            recomputed = true;
            return Sample(vertexOffset: 0);
        });

        Assert.True(recomputed, "'a' should have been evicted by the LRU sweep under the size cap.");
    }

    [Fact]
    public void NullOrEmptyDirectoryThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new DiskLineLodCache(null!, 1024));
        Assert.Throws<ArgumentException>(() => new DiskLineLodCache("", 1024));
    }

    [Fact]
    public void NonPositiveBudgetThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiskLineLodCache(_dir, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiskLineLodCache(_dir, -1));
    }

    [Fact]
    public void NullKeyOrFactoryThrows()
    {
        var cache = new DiskLineLodCache(_dir, maxBytes: 1024 * 1024);
        Assert.Throws<ArgumentNullException>(() => cache.GetOrCompute(null!, () => Sample()));
        Assert.Throws<ArgumentNullException>(() => cache.GetOrCompute("k", null!));
    }
}
