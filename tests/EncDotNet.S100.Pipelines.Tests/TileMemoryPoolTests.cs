using System.Diagnostics;
using EncDotNet.S100.Rendering.Engine;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The process-wide tile budget (map rendering slice 4, #691 item 1): several
/// layers' <see cref="TileCache"/>s charge one <see cref="TileMemoryPool"/> with
/// one recency order, so the least recently drawn tiles go first whichever layer
/// holds them, pins hold only while their layer keeps drawing, and a collected
/// layer's tiles are released.
/// </summary>
public class TileMemoryPoolTests
{
    // 64×64 RGBA = 16 KB per tile.
    private const int Size = 64;
    private const long TileBytes = Size * Size * 4;

    private static SKImage MakeImage()
    {
        var info = new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Black);
        return surface.Snapshot();
    }

    private static TileKey Key(int x) => new(5, x, 0);

    private sealed class FakeClock
    {
        public long Ticks = 1;

        public void Advance(TimeSpan by) => Ticks += (long)(by.TotalSeconds * Stopwatch.Frequency);
    }

    /// <summary>A pool whose limit fits <paramref name="tiles"/> tiles (the floor is 4 MB, so scale it).</summary>
    private static (TileMemoryPool Pool, FakeClock Clock) PoolOf(long limitBytes)
    {
        var clock = new FakeClock();
        return (new TileMemoryPool(() => limitBytes, "test", () => clock.Ticks), clock);
    }

    // The floor is 4 MB = 256 of these tiles.
    private const int FloorTiles = (int)(TileCache.MinBudgetBytes / TileBytes);

    [Fact]
    public void Caches_ShareOneBudget()
    {
        var (pool, _) = PoolOf(TileCache.MinBudgetBytes);
        using var a = new TileCache(pool, owner: null);
        using var b = new TileCache(pool, owner: null);

        for (var i = 0; i < FloorTiles; i++)
        {
            a.Put(Key(i), MakeImage());
        }

        Assert.Equal(TileCache.MinBudgetBytes, pool.ResidentBytes);

        // B's insert evicts A's least recently used tile, not one of B's.
        b.Put(Key(0), MakeImage());

        Assert.Equal(TileCache.MinBudgetBytes, pool.ResidentBytes);
        Assert.Equal(FloorTiles - 1, a.Count);
        Assert.Equal(1, b.Count);
        Assert.False(a.Contains(Key(0)));
        Assert.Equal(pool.ResidentBytes, a.ResidentBytes + b.ResidentBytes);
    }

    [Fact]
    public void Eviction_TakesTheLayerNobodyDraws_First()
    {
        var (pool, _) = PoolOf(TileCache.MinBudgetBytes);
        using var drawn = new TileCache(pool, owner: null);
        using var culled = new TileCache(pool, owner: null);

        // Both layers fill half the pool; the culled layer's tiles land last,
        // so a per-insert order alone would evict the drawn layer's first.
        for (var i = 0; i < FloorTiles / 2; i++)
        {
            drawn.Put(Key(i), MakeImage());
        }

        for (var i = 0; i < FloorTiles / 2; i++)
        {
            culled.Put(Key(i), MakeImage());
        }

        // The drawn layer keeps blitting its tiles; the culled one doesn't.
        for (var i = 0; i < FloorTiles / 2; i++)
        {
            Assert.NotNull(drawn.TryGet(Key(i)));
        }

        // New tiles for the drawn layer push out the culled layer's.
        for (var i = 0; i < FloorTiles / 4; i++)
        {
            drawn.Put(Key(1000 + i), MakeImage());
        }

        Assert.Equal(FloorTiles / 2 + FloorTiles / 4, drawn.Count);
        Assert.Equal(FloorTiles / 4, culled.Count);
    }

    [Fact]
    public void PinnedTiles_SurviveAnotherLayersInserts()
    {
        var (pool, _) = PoolOf(TileCache.MinBudgetBytes);
        using var visible = new TileCache(pool, owner: null);
        using var other = new TileCache(pool, owner: null);

        visible.Put(Key(0), MakeImage());
        visible.Protect([Key(0)]);

        for (var i = 0; i < FloorTiles + 10; i++)
        {
            other.Put(Key(i), MakeImage());
        }

        Assert.True(visible.Contains(Key(0)));
    }

    [Fact]
    public void Pins_LapseWhenTheLayerStopsDrawing_WhileOthersDraw()
    {
        var (pool, clock) = PoolOf(TileCache.MinBudgetBytes);
        using var stale = new TileCache(pool, owner: null);
        using var live = new TileCache(pool, owner: null);

        stale.Put(Key(0), MakeImage());
        stale.Protect([Key(0)]);

        // The other layer keeps drawing (renewing its pins) past the lapse.
        clock.Advance(TileMemoryPool.PinLapse + TimeSpan.FromMilliseconds(1));
        live.Protect([]);

        for (var i = 0; i < FloorTiles; i++)
        {
            live.Put(Key(i), MakeImage());
        }

        Assert.False(stale.Contains(Key(0)));
    }

    [Fact]
    public void Pins_HoldOnAnIdleMap()
    {
        var (pool, clock) = PoolOf(TileCache.MinBudgetBytes);
        using var a = new TileCache(pool, owner: null);
        using var b = new TileCache(pool, owner: null);

        a.Put(Key(0), MakeImage());
        a.Protect([Key(0)]);

        // Nobody draws for a long time (no newer pin anywhere), then a
        // background insert lands: the visible tile must stay.
        clock.Advance(TimeSpan.FromMinutes(5));
        for (var i = 0; i < FloorTiles + 10; i++)
        {
            b.Put(Key(i), MakeImage());
        }

        Assert.True(a.Contains(Key(0)));
    }

    [Fact]
    public void CollectedOwner_ReleasesItsTiles()
    {
        var (pool, clock) = PoolOf(TileCache.MinBudgetBytes);
        using var survivor = new TileCache(pool, owner: null);
        var orphan = AddOrphanedCache(pool);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(2, pool.MemberCount);
        Assert.Equal(3 * TileBytes, pool.ResidentBytes);

        // The pool reaps at most once a second, on an insert.
        clock.Advance(TimeSpan.FromSeconds(2));
        survivor.Put(Key(1), MakeImage());

        Assert.Equal(1, pool.MemberCount);
        Assert.Equal(TileBytes, pool.ResidentBytes);
        Assert.Equal(0, orphan.Count);
    }

    private static TileCache AddOrphanedCache(TileMemoryPool pool)
    {
        var cache = CreateWithTransientOwner(pool);
        cache.Put(Key(0), MakeImage());
        cache.Put(Key(1), MakeImage());
        cache.Put(Key(2), MakeImage());
        return cache;
    }

    // Separate frame so the owner isn't kept alive by a local.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static TileCache CreateWithTransientOwner(TileMemoryPool pool) => new(pool, owner: new object());

    [Fact]
    public void Dispose_RemovesTheCacheFromThePool()
    {
        var (pool, _) = PoolOf(TileCache.MinBudgetBytes);
        var cache = new TileCache(pool, owner: null);
        cache.Put(Key(0), MakeImage());

        cache.Dispose();

        Assert.Equal(0, pool.MemberCount);
        Assert.Equal(0, pool.ResidentBytes);
        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public void LimitChange_AppliesOnTheNextInsert()
    {
        var limit = 2 * TileCache.MinBudgetBytes;
        var pool = new TileMemoryPool(() => limit, "test");
        using var cache = new TileCache(pool, owner: null);
        for (var i = 0; i < 2 * FloorTiles; i++)
        {
            cache.Put(Key(i), MakeImage());
        }

        limit = TileCache.MinBudgetBytes;
        cache.Put(Key(10_000), MakeImage());

        Assert.Equal(TileCache.MinBudgetBytes, pool.ResidentBytes);
    }

    [Fact]
    public void GpuEviction_FromAnotherLayer_WaitsForTheOwnersDrain()
    {
        var (pool, clock) = PoolOf(TileCache.MinBudgetBytes);
        using var owner = new TileCache(pool, owner: null, deferDisposal: true);
        using var evictor = new TileCache(pool, owner: null, deferDisposal: true);

        var texture = MakeImage();
        owner.Put(Key(0), texture);

        // Another layer's uploads in the same frame evict the owner's texture,
        // which the owner may already have drawn this frame.
        for (var i = 0; i < FloorTiles; i++)
        {
            evictor.Put(Key(i), MakeImage());
        }

        Assert.False(owner.Contains(Key(0)));
        Assert.Equal(1, pool.PendingDisposalCount);

        // The evicting layer's next frame mustn't free it…
        evictor.DrainPendingDisposals();
        Assert.Equal(1, pool.PendingDisposalCount);
        Assert.NotEqual(IntPtr.Zero, texture.Handle);

        // …but the owner's next frame does.
        owner.DrainPendingDisposals();
        Assert.Equal(0, pool.PendingDisposalCount);
    }

    [Fact]
    public void GpuEviction_OfALayerThatNeverDrawsAgain_IsFreedAfterTheDelay()
    {
        var (pool, clock) = PoolOf(TileCache.MinBudgetBytes);
        using var owner = new TileCache(pool, owner: null, deferDisposal: true);
        using var evictor = new TileCache(pool, owner: null, deferDisposal: true);

        owner.Put(Key(0), MakeImage());
        for (var i = 0; i < FloorTiles; i++)
        {
            evictor.Put(Key(i), MakeImage());
        }

        clock.Advance(TileMemoryPool.OrphanDisposalDelay);
        evictor.DrainPendingDisposals();

        Assert.Equal(0, pool.PendingDisposalCount);
    }
}
