using EncDotNet.S100.Renderers.Mapsui.Simplification;
using Mapsui;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests.Simplification;

/// <summary>
/// Tests <see cref="SimplificationCache"/> bucketing, reuse, and
/// eviction behaviour. The simplifier itself is exercised separately
/// — these tests use a stub that returns predictable clones so the
/// cache logic can be observed in isolation.
/// </summary>
public sealed class SimplificationCacheTests
{
    private static readonly GeometryFactory _gf = new();

    private static GeometryFeature MakeLine(int coordCount, int seed = 0)
    {
        var coords = new Coordinate[coordCount];
        for (int i = 0; i < coordCount; i++)
            coords[i] = new Coordinate(i + seed, (i + seed) % 7 * 0.3);
        return new GeometryFeature(_gf.CreateLineString(coords));
    }

    [Fact]
    public void RepeatLookup_ReturnsSameInstance()
    {
        var cache = new SimplificationCache(DouglasPeuckerLineSimplifier.Instance, new SimplificationOptions());
        var f = MakeLine(500);
        var first = cache.GetOrSimplify(f, resolution: 1.0);
        var second = cache.GetOrSimplify(f, resolution: 1.0);
        Assert.Same(first, second);
    }

    [Fact]
    public void DifferentBuckets_GiveDifferentInstances()
    {
        var cache = new SimplificationCache(DouglasPeuckerLineSimplifier.Instance, new SimplificationOptions());
        var f = MakeLine(500);
        var coarse = cache.GetOrSimplify(f, resolution: 100.0);
        var fine = cache.GetOrSimplify(f, resolution: 0.1);
        Assert.NotSame(coarse, fine);
    }

    [Fact]
    public void Bucket_HalfOctave_Computation()
    {
        // 1.0 → log2 = 0 → bucket 0
        Assert.Equal(0, SimplificationCache.BucketFor(1.0));
        // 2.0 → log2 = 1 → bucket 2
        Assert.Equal(2, SimplificationCache.BucketFor(2.0));
        // sqrt(2) → log2 = 0.5 → round(1) = 1
        Assert.Equal(1, SimplificationCache.BucketFor(System.Math.Sqrt(2.0)));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Bucket_HandlesDegenerateResolution(double res)
    {
        // No throw; clamps to MinResolution (1e-3).
        var bucket = SimplificationCache.BucketFor(res);
        Assert.Equal(SimplificationCache.BucketFor(1e-3), bucket);
    }

    [Fact]
    public void BucketShift_DropsNonAdjacentBuckets()
    {
        var cache = new SimplificationCache(DouglasPeuckerLineSimplifier.Instance, new SimplificationOptions());
        // Populate at bucket 0 (resolution 1.0).
        var f = MakeLine(500);
        cache.GetOrSimplify(f, resolution: 1.0);
        Assert.True(cache.CachedEntryCount >= 1);

        // Jump to a bucket far away — should drop bucket 0.
        var far = MakeLine(500, seed: 100_000);
        cache.GetOrSimplify(far, resolution: 1024.0); // bucket = 20

        // Original bucket 0 should be gone (only the far entry remains
        // around the new active bucket).
        Assert.True(cache.CachedEntryCount <= 1);
    }

    [Fact]
    public void BucketShift_KeepsAdjacentBuckets()
    {
        var cache = new SimplificationCache(DouglasPeuckerLineSimplifier.Instance, new SimplificationOptions());
        var f = MakeLine(500);
        // Bucket 0 (res 1.0)
        cache.GetOrSimplify(f, resolution: 1.0);
        var beforeCount = cache.CachedEntryCount;
        // Bucket 1 (res √2 ≈ 1.414) — adjacent to 0
        cache.GetOrSimplify(f, resolution: System.Math.Sqrt(2.0));
        // Both buckets should be retained.
        Assert.True(cache.CachedEntryCount >= beforeCount + 1);
    }

    [Fact]
    public void CoordinateBudget_ForcesEviction()
    {
        // Budget so small that two ~500-coord features can't both fit.
        var opts = new SimplificationOptions(MaxCachedCoordinates: 100);
        var cache = new SimplificationCache(DouglasPeuckerLineSimplifier.Instance, opts);

        // Active bucket 0 with several features.
        for (int i = 0; i < 5; i++)
            cache.GetOrSimplify(MakeLine(500, seed: i * 1000), resolution: 1.0);
        var bucket0 = cache.CachedCoordinateCount;

        // Far bucket — populating it should evict bucket 0.
        cache.GetOrSimplify(MakeLine(500, seed: 999_000), resolution: 1024.0);

        Assert.True(cache.CachedCoordinateCount <= opts.MaxCachedCoordinates + 500,
            $"Coord budget overrun: tracking {cache.CachedCoordinateCount} > {opts.MaxCachedCoordinates}");
        Assert.True(cache.CachedCoordinateCount < bucket0,
            "Adding far-bucket entries should have evicted bucket-0 entries.");
    }

    [Fact]
    public void Clear_EmptiesCache()
    {
        var cache = new SimplificationCache(DouglasPeuckerLineSimplifier.Instance, new SimplificationOptions());
        cache.GetOrSimplify(MakeLine(500), resolution: 1.0);
        cache.GetOrSimplify(MakeLine(500, seed: 5000), resolution: 1.0);
        Assert.True(cache.CachedEntryCount >= 2);
        cache.Clear();
        Assert.Equal(0, cache.CachedEntryCount);
        Assert.Equal(0, cache.CachedCoordinateCount);
    }

    [Fact]
    public void Passthrough_CachedAsIdentity_DoesNotReSimplify()
    {
        // A short feature passes through; the cache must still record
        // the identity so we don't re-test on every paint.
        var cache = new SimplificationCache(DouglasPeuckerLineSimplifier.Instance, new SimplificationOptions());
        var f = MakeLine(10); // below MinVertexCount default 64
        var first = cache.GetOrSimplify(f, resolution: 1.0);
        var second = cache.GetOrSimplify(f, resolution: 1.0);
        Assert.Same(f, first);
        Assert.Same(f, second);
    }

    [Fact]
    public void GetOriginal_RecoversBackReference()
    {
        var cache = new SimplificationCache(DouglasPeuckerLineSimplifier.Instance, new SimplificationOptions());
        var f = MakeLine(500);
        var simplified = cache.GetOrSimplify(f, resolution: 1.0);
        // For this dense line at this tolerance, simplification should
        // have produced a different instance carrying the back-ref.
        Assert.NotSame(f, simplified);
        Assert.Same(f, EncDotNet.S100.Renderers.Mapsui.Simplification.Simplification.GetOriginal(simplified));
        // GetOriginal on a non-simplified feature returns it unchanged.
        Assert.Same(f, EncDotNet.S100.Renderers.Mapsui.Simplification.Simplification.GetOriginal(f));
    }
}
