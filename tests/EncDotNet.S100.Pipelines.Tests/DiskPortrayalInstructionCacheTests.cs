using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Caching;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Behaviour of <see cref="DiskPortrayalInstructionCache"/>: binary round-trip
/// fidelity, cross-restart persistence (a second instance over the same
/// directory serves a hit), distinct-key misses, miss-on-corruption /
/// version-mismatch resilience (never throws), LRU size-cap eviction, and
/// argument validation.
/// </summary>
public class DiskPortrayalInstructionCacheTests : IDisposable
{
    private readonly string _dir;

    public DiskPortrayalInstructionCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "encdotnet-dlistcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    private static IReadOnlyList<DrawingInstruction> Sample(string feature) =>
    [
        new AreaInstruction { FeatureReference = feature, DrawingPriority = 1, AreaFillReference = "DIAMOND1", FillColor = "DEPVS", Transparency = 0.25 },
        new LineInstruction { FeatureReference = feature, DrawingPriority = 2, LineColor = "CHBLK", LineWidth = 0.32, Dashes = [(0.0, 1.0), (1.0, 2.0)] },
        new PointInstruction { FeatureReference = feature, DrawingPriority = 3, SymbolReference = "BOYLAT01", CoordinateOverride = new GeoPosition(50.5, -1.25) },
        new TextInstruction { FeatureReference = feature, DrawingPriority = 4, Text = feature, FontSize = 11.0 },
    ];

    private static void AssertSampleEqual(string feature, IReadOnlyList<DrawingInstruction> actual)
    {
        Assert.Equal(4, actual.Count);
        var area = Assert.IsType<AreaInstruction>(actual[0]);
        Assert.Equal(feature, area.FeatureReference);
        Assert.Equal("DIAMOND1", area.AreaFillReference);
        Assert.Equal(0.25, area.Transparency);
        var line = Assert.IsType<LineInstruction>(actual[1]);
        Assert.Equal([(0.0, 1.0), (1.0, 2.0)], line.Dashes);
        var point = Assert.IsType<PointInstruction>(actual[2]);
        Assert.Equal(new GeoPosition(50.5, -1.25), point.CoordinateOverride);
        var text = Assert.IsType<TextInstruction>(actual[3]);
        Assert.Equal(feature, text.Text);
    }

    [Fact]
    public void RoundTrip_PreservesFieldsAndOrder()
    {
        var cache = new DiskPortrayalInstructionCache(_dir, maxBytes: 16 * 1024 * 1024);
        var expected = Sample("f1");

        var first = cache.GetOrCompute("k", () => expected);
        var second = cache.GetOrCompute("k", () => throw new InvalidOperationException("should not recompute"));

        Assert.Equal(1, cache.Misses);
        Assert.Equal(1, cache.Hits);
        Assert.Same(expected, first);
        AssertSampleEqual("f1", second); // freshly deserialized, value-equivalent
    }

    [Fact]
    public void SecondInstance_SameDirectory_ReturnsHit_ProvingPersistence()
    {
        var writer = new DiskPortrayalInstructionCache(_dir, maxBytes: 16 * 1024 * 1024);
        writer.GetOrCompute("k", () => Sample("f1"));
        Assert.Equal(1, writer.Misses);

        // A brand-new instance over the same directory simulates a restart.
        var reader = new DiskPortrayalInstructionCache(_dir, maxBytes: 16 * 1024 * 1024);
        var hit = reader.GetOrCompute("k", () => throw new InvalidOperationException("should be served from disk"));

        Assert.Equal(1, reader.Hits);
        Assert.Equal(0, reader.Misses);
        AssertSampleEqual("f1", hit);
    }

    [Fact]
    public void DifferentKey_IsMiss()
    {
        var cache = new DiskPortrayalInstructionCache(_dir, maxBytes: 16 * 1024 * 1024);
        cache.GetOrCompute("scope-A|portrayal", () => Sample("a"));

        var recomputed = false;
        cache.GetOrCompute("scope-B|portrayal", () => { recomputed = true; return Sample("b"); });

        Assert.True(recomputed);
        Assert.Equal(2, cache.Misses);
        Assert.Equal(0, cache.Hits);
    }

    [Fact]
    public void FormatVersionMismatch_IsMiss()
    {
        var cache = new DiskPortrayalInstructionCache(_dir, maxBytes: 16 * 1024 * 1024);
        cache.GetOrCompute("k", () => Sample("f1"));

        // Corrupt the leading FormatVersion int (first 4 bytes) of the only file.
        var file = Directory.GetFiles(_dir, "*.dlist").Single();
        var bytes = File.ReadAllBytes(file);
        BitConverter.GetBytes(DrawingInstructionSerializer.FormatVersion + 999).CopyTo(bytes, 0);
        File.WriteAllBytes(file, bytes);

        var recomputed = false;
        var fresh = new DiskPortrayalInstructionCache(_dir, maxBytes: 16 * 1024 * 1024);
        fresh.GetOrCompute("k", () => { recomputed = true; return Sample("f1"); });

        Assert.True(recomputed);
        Assert.Equal(1, fresh.Misses);
        Assert.Equal(0, fresh.Hits);
    }

    [Fact]
    public void CorruptOrTruncatedFile_IsMiss_NoThrow()
    {
        var expected = Sample("f1");
        var cache = new DiskPortrayalInstructionCache(_dir, maxBytes: 16 * 1024 * 1024);
        cache.GetOrCompute("k", () => expected);

        var file = Directory.GetFiles(_dir, "*.dlist").Single();
        File.WriteAllBytes(file, [1, 2, 3]);

        var recomputed = false;
        var fresh = new DiskPortrayalInstructionCache(_dir, maxBytes: 16 * 1024 * 1024);
        var result = fresh.GetOrCompute("k", () => { recomputed = true; return expected; });

        Assert.True(recomputed);
        Assert.Equal(1, fresh.Misses);
        Assert.Same(expected, result);
    }

    [Fact]
    public void ExceedingMaxBytes_EvictsLeastRecentlyUsed()
    {
        // Each entry is the same size (same shape, only the feature id differs in
        // length-equal ways), so the cap is a predictable entry count.
        IReadOnlyList<DrawingInstruction> Entry(int seed) =>
        [
            new AreaInstruction { FeatureReference = $"feat{seed:000}", DrawingPriority = seed, AreaFillReference = "DIAMOND1" },
        ];

        var probeDir = Path.Combine(_dir, "probe");
        var probe = new DiskPortrayalInstructionCache(probeDir, maxBytes: long.MaxValue);
        probe.GetOrCompute("x", () => Entry(1));
        var oneSize = new FileInfo(Directory.GetFiles(probeDir, "*.dlist").Single()).Length;

        var capDir = Path.Combine(_dir, "cap2");
        Directory.CreateDirectory(capDir);
        // Cap holds two entries; a third write evicts the least-recently-used.
        var cache = new DiskPortrayalInstructionCache(capDir, maxBytes: oneSize * 2 + oneSize / 2);

        cache.GetOrCompute("a", () => Entry(1));
        Thread.Sleep(1100);
        cache.GetOrCompute("b", () => Entry(2));
        Thread.Sleep(1100);
        cache.GetOrCompute("a", () => throw new InvalidOperationException("a should be a hit"));
        Thread.Sleep(1100);
        cache.GetOrCompute("c", () => Entry(3));

        Assert.Equal(2, Directory.GetFiles(capDir, "*.dlist").Length);

        var bRecomputed = false;
        cache.GetOrCompute("b", () => { bRecomputed = true; return Entry(2); });
        Assert.True(bRecomputed); // "b" (the LRU) was evicted.
    }

    [Fact]
    public void GetOrCompute_NullArguments_Throw()
    {
        var cache = new DiskPortrayalInstructionCache(_dir, maxBytes: 1024);
        Assert.Throws<ArgumentNullException>(() => cache.GetOrCompute(null!, () => []));
        Assert.Throws<ArgumentNullException>(() => cache.GetOrCompute("k", null!));
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => new DiskPortrayalInstructionCache("", 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiskPortrayalInstructionCache(_dir, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiskPortrayalInstructionCache(_dir, -1));
    }
}
