using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Caching;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Behaviour of <see cref="InMemoryPortrayalInstructionCache"/>: hit / miss
/// accounting, same-instance reuse on a hit (no serialization), distinct keys
/// miss independently, list order is never disturbed, least-recently-used
/// eviction at capacity, and argument validation.
/// </summary>
public class InMemoryPortrayalInstructionCacheTests
{
    private static IReadOnlyList<DrawingInstruction> Sample(string feature) =>
    [
        new AreaInstruction { FeatureReference = feature, DrawingPriority = 1, AreaFillReference = "F" },
        new LineInstruction { FeatureReference = feature, DrawingPriority = 2, LineColor = "CHBLK" },
        new TextInstruction { FeatureReference = feature, DrawingPriority = 3, Text = feature },
    ];

    [Fact]
    public void GetOrCompute_MissThenHit_ReturnsSameInstanceAndCounts()
    {
        var cache = new InMemoryPortrayalInstructionCache();
        var value = Sample("a");

        var first = cache.GetOrCompute("k", () => value);
        var second = cache.GetOrCompute("k", () => throw new InvalidOperationException("should not recompute"));

        Assert.Same(value, first);
        Assert.Same(value, second); // in-memory cache returns the exact same list
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void GetOrCompute_PreservesListOrder()
    {
        var cache = new InMemoryPortrayalInstructionCache();
        var value = Sample("a");
        cache.GetOrCompute("k", () => value);

        var hit = cache.GetOrCompute("k", () => throw new InvalidOperationException());

        Assert.Collection(hit,
            i => Assert.IsType<AreaInstruction>(i),
            i => Assert.IsType<LineInstruction>(i),
            i => Assert.IsType<TextInstruction>(i));
    }

    [Fact]
    public void GetOrCompute_DistinctKeys_MissIndependently()
    {
        var cache = new InMemoryPortrayalInstructionCache();
        cache.GetOrCompute("a", () => Sample("a"));
        cache.GetOrCompute("b", () => Sample("b"));

        Assert.Equal(2, cache.Misses);
        Assert.Equal(0, cache.Hits);

        // Both keys remain cached (no eviction below capacity).
        cache.GetOrCompute("a", () => throw new InvalidOperationException());
        cache.GetOrCompute("b", () => throw new InvalidOperationException());
        Assert.Equal(2, cache.Hits);
    }

    [Fact]
    public void Capacity_EvictsLeastRecentlyUsed()
    {
        var cache = new InMemoryPortrayalInstructionCache(capacity: 2);

        cache.GetOrCompute("a", () => Sample("a"));
        cache.GetOrCompute("b", () => Sample("b"));
        // Re-access "a" so "b" becomes the least-recently-used.
        cache.GetOrCompute("a", () => throw new InvalidOperationException("a should be a hit"));
        // Writing "c" exceeds capacity; "b" (LRU) must be evicted.
        cache.GetOrCompute("c", () => Sample("c"));

        var bRecomputed = false;
        cache.GetOrCompute("b", () => { bRecomputed = true; return Sample("b"); });
        Assert.True(bRecomputed);
    }

    [Fact]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryPortrayalInstructionCache(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryPortrayalInstructionCache(-1));
    }

    [Fact]
    public void GetOrCompute_NullArguments_Throw()
    {
        var cache = new InMemoryPortrayalInstructionCache();
        Assert.Throws<ArgumentNullException>(() => cache.GetOrCompute(null!, () => []));
        Assert.Throws<ArgumentNullException>(() => cache.GetOrCompute("k", null!));
    }
}
