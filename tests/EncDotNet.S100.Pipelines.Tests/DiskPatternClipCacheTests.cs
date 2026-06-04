using EncDotNet.S100.Renderers.Mapsui;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the disk-backed pattern-clip cache: WKB round-trip fidelity,
/// cross-restart persistence (a second cache instance over the same directory
/// returns a hit), self-invalidation on key/<see cref="DiskPatternClipCache.FormatVersion"/>
/// change, miss-on-corruption resilience, and LRU size-cap eviction.
/// </summary>
public class DiskPatternClipCacheTests : IDisposable
{
    private readonly string _dir;

    public DiskPatternClipCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "encdotnet-clipcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    private static readonly GeometryFactory Gf = new();

    // A simple polygon plus an overlapping polygon-with-hole, mirroring typical
    // NTS overlay output (Polygon / MultiPolygon with interior rings).
    private static List<(string PatternRef, int Priority, Geometry Geometry)> SampleEntries()
    {
        var square = Gf.CreatePolygon(
        [
            new Coordinate(0, 0), new Coordinate(0, 4),
            new Coordinate(4, 4), new Coordinate(4, 0), new Coordinate(0, 0),
        ]);

        var shell = Gf.CreateLinearRing(
        [
            new Coordinate(10, 10), new Coordinate(10, 20),
            new Coordinate(20, 20), new Coordinate(20, 10), new Coordinate(10, 10),
        ]);
        var hole = Gf.CreateLinearRing(
        [
            new Coordinate(13, 13), new Coordinate(13, 17),
            new Coordinate(17, 17), new Coordinate(17, 13), new Coordinate(13, 13),
        ]);
        var holed = Gf.CreatePolygon(shell, [hole]);

        return
        [
            ("DQUAL", 5, square),
            ("DIAMOND1", 9, holed),
        ];
    }

    [Fact]
    public void RoundTrip_IsGeometryExact_AndPreservesRefPriorityOrder()
    {
        var cache = new DiskPatternClipCache(_dir, maxBytes: 16 * 1024 * 1024);
        var expected = SampleEntries();

        // First call is a miss (writes); second call is a hit (reads back).
        var first = cache.GetOrCompute("k", () => expected);
        var second = cache.GetOrCompute("k", () => throw new InvalidOperationException("should not recompute"));

        Assert.Equal(1, cache.Misses);
        Assert.Equal(1, cache.Hits);

        Assert.Equal(expected.Count, second.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].PatternRef, second[i].PatternRef);
            Assert.Equal(expected[i].Priority, second[i].Priority);
            Assert.True(
                expected[i].Geometry.EqualsExact(second[i].Geometry),
                $"Geometry {i} did not round-trip exactly.");
        }

        // The in-memory first result must equal the expected too.
        Assert.Same(expected, first);
    }

    [Fact]
    public void SecondInstance_SameDirectory_ReturnsHit_ProvingPersistence()
    {
        var expected = SampleEntries();

        var writer = new DiskPatternClipCache(_dir, maxBytes: 16 * 1024 * 1024);
        writer.GetOrCompute("k", () => expected);
        Assert.Equal(1, writer.Misses);

        // A brand-new instance over the same directory simulates a restart.
        var reader = new DiskPatternClipCache(_dir, maxBytes: 16 * 1024 * 1024);
        var hit = reader.GetOrCompute("k", () => throw new InvalidOperationException("should be served from disk"));

        Assert.Equal(1, reader.Hits);
        Assert.Equal(0, reader.Misses);
        Assert.Equal(expected.Count, hit.Count);
        Assert.True(expected[0].Geometry.EqualsExact(hit[0].Geometry));
    }

    [Fact]
    public void DifferentKey_IsMiss()
    {
        var cache = new DiskPatternClipCache(_dir, maxBytes: 16 * 1024 * 1024);
        cache.GetOrCompute("scope-A|portrayal", SampleEntries);

        var recomputed = false;
        cache.GetOrCompute("scope-B|portrayal", () => { recomputed = true; return SampleEntries(); });

        Assert.True(recomputed);
        Assert.Equal(2, cache.Misses);
        Assert.Equal(0, cache.Hits);
    }

    [Fact]
    public void FormatVersionMismatch_IsMiss()
    {
        var expected = SampleEntries();
        var cache = new DiskPatternClipCache(_dir, maxBytes: 16 * 1024 * 1024);
        cache.GetOrCompute("k", () => expected);

        // Corrupt the leading FormatVersion int (first 4 bytes) of the only file.
        var file = Directory.GetFiles(_dir, "*.clip").Single();
        var bytes = File.ReadAllBytes(file);
        BitConverter.GetBytes(DiskPatternClipCache.FormatVersion + 999).CopyTo(bytes, 0);
        File.WriteAllBytes(file, bytes);

        var recomputed = false;
        var fresh = new DiskPatternClipCache(_dir, maxBytes: 16 * 1024 * 1024);
        fresh.GetOrCompute("k", () => { recomputed = true; return expected; });

        Assert.True(recomputed);
        Assert.Equal(1, fresh.Misses);
        Assert.Equal(0, fresh.Hits);
    }

    [Fact]
    public void CorruptOrTruncatedFile_IsMiss_NoThrow()
    {
        var expected = SampleEntries();
        var cache = new DiskPatternClipCache(_dir, maxBytes: 16 * 1024 * 1024);
        cache.GetOrCompute("k", () => expected);

        // Truncate the file to a few bytes so deserialization runs off the end.
        var file = Directory.GetFiles(_dir, "*.clip").Single();
        File.WriteAllBytes(file, [1, 2, 3]);

        var recomputed = false;
        var fresh = new DiskPatternClipCache(_dir, maxBytes: 16 * 1024 * 1024);
        var result = fresh.GetOrCompute("k", () => { recomputed = true; return expected; });

        Assert.True(recomputed);
        Assert.Equal(1, fresh.Misses);
        Assert.Same(expected, result);
    }

    [Fact]
    public void GarbageLengthHeader_IsMiss_NoHugeAllocation()
    {
        var expected = SampleEntries();
        var cache = new DiskPatternClipCache(_dir, maxBytes: 16 * 1024 * 1024);
        cache.GetOrCompute("k", () => expected);

        // Valid FormatVersion, then a hostile entry count of int.MaxValue.
        var file = Directory.GetFiles(_dir, "*.clip").Single();
        using (var fs = File.Create(file))
        using (var w = new BinaryWriter(fs))
        {
            w.Write(DiskPatternClipCache.FormatVersion);
            w.Write(int.MaxValue);
        }

        var fresh = new DiskPatternClipCache(_dir, maxBytes: 16 * 1024 * 1024);
        var result = fresh.GetOrCompute("k", () => expected);

        Assert.Equal(1, fresh.Misses);
        Assert.Same(expected, result);
    }

    [Fact]
    public void ExceedingMaxBytes_EvictsToHonourCap_AndProtectsFreshEntry()
    {
        // Deterministic circle polygon (many vertices, identical WKB size for
        // every seed since it is only translated), so cache file sizes are equal
        // and predictable.
        static Geometry Circle(int seed)
        {
            var coords = new Coordinate[129];
            for (int i = 0; i < 128; i++)
            {
                var a = 2 * Math.PI * i / 128;
                coords[i] = new Coordinate(seed + Math.Cos(a), seed + Math.Sin(a));
            }
            coords[128] = coords[0];
            return Gf.CreatePolygon(coords);
        }

        List<(string, int, Geometry)> Entry(int seed) => [($"P{seed}", seed, Circle(seed))];

        // Measure one entry's on-disk size in an isolated probe directory.
        var probeDir = Path.Combine(_dir, "probe");
        var probe = new DiskPatternClipCache(probeDir, maxBytes: long.MaxValue);
        probe.GetOrCompute("x", () => Entry(1));
        var oneSize = new FileInfo(Directory.GetFiles(probeDir, "*.clip").Single()).Length;

        // Cap holds at most ONE entry, so the second write must evict the first
        // (the just-written entry is protected, so it is the older one that goes).
        var capDir = Path.Combine(_dir, "cap1");
        Directory.CreateDirectory(capDir);
        var cache = new DiskPatternClipCache(capDir, maxBytes: oneSize + oneSize / 2);

        cache.GetOrCompute("a", () => Entry(1));
        cache.GetOrCompute("b", () => Entry(2));

        // Only the most recent entry survives.
        Assert.Single(Directory.GetFiles(capDir, "*.clip"));

        var bRecomputed = false;
        cache.GetOrCompute("b", () => { bRecomputed = true; return Entry(2); });
        Assert.False(bRecomputed); // "b" is still cached (a hit).

        var aRecomputed = false;
        cache.GetOrCompute("a", () => { aRecomputed = true; return Entry(1); });
        Assert.True(aRecomputed); // "a" was evicted (a miss).
    }

    [Fact]
    public void Eviction_RemovesLeastRecentlyUsedEntry()
    {
        static Geometry Circle(int seed)
        {
            var coords = new Coordinate[129];
            for (int i = 0; i < 128; i++)
            {
                var a = 2 * Math.PI * i / 128;
                coords[i] = new Coordinate(seed + Math.Cos(a), seed + Math.Sin(a));
            }
            coords[128] = coords[0];
            return Gf.CreatePolygon(coords);
        }

        List<(string, int, Geometry)> Entry(int seed) => [($"P{seed}", seed, Circle(seed))];

        var probeDir = Path.Combine(_dir, "probe2");
        var probe = new DiskPatternClipCache(probeDir, maxBytes: long.MaxValue);
        probe.GetOrCompute("x", () => Entry(1));
        var oneSize = new FileInfo(Directory.GetFiles(probeDir, "*.clip").Single()).Length;

        var capDir = Path.Combine(_dir, "cap2");
        Directory.CreateDirectory(capDir);
        // Cap holds two entries; a third write evicts the least-recently-used.
        var cache = new DiskPatternClipCache(capDir, maxBytes: oneSize * 2 + oneSize / 2);

        // Sleep > 1s between access-time-affecting operations so the ordering is
        // unambiguous even on filesystems with coarse (1s) timestamp resolution.
        cache.GetOrCompute("a", () => Entry(1));
        Thread.Sleep(1100);
        cache.GetOrCompute("b", () => Entry(2));
        Thread.Sleep(1100);
        // Re-access "a" so "b" is now the least-recently-used.
        cache.GetOrCompute("a", () => throw new InvalidOperationException("a should be a hit"));
        Thread.Sleep(1100);
        // Writing "c" exceeds the cap; the LRU entry "b" must be evicted.
        cache.GetOrCompute("c", () => Entry(3));

        Assert.Equal(2, Directory.GetFiles(capDir, "*.clip").Length);

        var bRecomputed = false;
        cache.GetOrCompute("b", () => { bRecomputed = true; return Entry(2); });
        Assert.True(bRecomputed); // "b" (the LRU) was evicted.
    }

    [Fact]
    public void GetOrCompute_NullArguments_Throw()
    {
        var cache = new DiskPatternClipCache(_dir, maxBytes: 1024);
        Assert.Throws<ArgumentNullException>(() => cache.GetOrCompute(null!, () => []));
        Assert.Throws<ArgumentNullException>(() => cache.GetOrCompute("k", null!));
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => new DiskPatternClipCache("", 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiskPatternClipCache(_dir, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiskPatternClipCache(_dir, -1));
    }
}
