using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector.Caching;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Behaviour of <see cref="InMemoryLineLodCache"/>: hit/miss counting,
/// factory invocation on miss only, and argument validation. The disk
/// variant has its own broader test class; this focuses on the semantics
/// shared with <c>InMemoryPortrayalInstructionCache</c>.
/// </summary>
public class InMemoryLineLodCacheTests
{
    private static LineLodPyramid Sample() =>
        LineLodPyramid.Build(
            [new GeoPosition(50.0, -1.0), new(50.05, -0.95), new(50.1, -0.9)],
            LineLodTolerances.HalfOctaveDefault);

    [Fact]
    public void MissThenHit()
    {
        var cache = new InMemoryLineLodCache();

        var invocations = 0;
        _ = cache.GetOrCompute("k", () => { invocations++; return Sample(); });
        _ = cache.GetOrCompute("k", () => { invocations++; return Sample(); });

        Assert.Equal(1, invocations);
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void ClearInvalidatesEntries()
    {
        var cache = new InMemoryLineLodCache();
        _ = cache.GetOrCompute("k", Sample);
        cache.Clear();

        var invocations = 0;
        _ = cache.GetOrCompute("k", () => { invocations++; return Sample(); });

        Assert.Equal(1, invocations);
    }

    [Fact]
    public void NullKeyOrFactoryThrows()
    {
        var cache = new InMemoryLineLodCache();
        Assert.Throws<ArgumentNullException>(() => cache.GetOrCompute(null!, Sample));
        Assert.Throws<ArgumentNullException>(() => cache.GetOrCompute("k", null!));
    }
}
