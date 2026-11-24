using System.Diagnostics;
using EncDotNet.S100.Rendering.Engine;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Caches whose keys are content-addressed (chart tiles) share tiles through
/// their pool: one adopts a tile another holds instead of rasterising it, an
/// equal tile put twice is kept once, the pool charges a shared tile once, and
/// its image lives until the last holder lets it go. Caches share only within
/// one scope (texture caches: one graphics context), and a texture another
/// cache held waits out the orphan delay before it is freed.
/// </summary>
public class SharedTileCacheTests
{
    private const int Size = 64;
    private const long TileBytes = Size * Size * 4;

    private static SKImage MakeImage(int size = Size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Black);
        return surface.Snapshot();
    }

    private static TileKey Key(int x) => new(5, x, 0, Slice: 42);

    private static TileMemoryPool Pool(long limitBytes = TileCache.MinBudgetBytes)
    {
        long ticks = 1;
        return new TileMemoryPool(() => limitBytes, "test", () => ticks += Stopwatch.Frequency);
    }

    private static readonly object Scope = new();

    private static TileCache Sharing(TileMemoryPool pool, object? scope = null) =>
        new(pool, owner: null) { ShareScope = scope ?? Scope };

    [Fact]
    public void ACacheAdoptsATileAnotherSharingCacheHolds()
    {
        var pool = Pool();
        using var first = Sharing(pool);
        using var second = Sharing(pool);
        var image = MakeImage();
        first.Put(Key(0), image);

        Assert.True(second.Contains(Key(0), Size));
        Assert.Same(image, second.TryGet(Key(0)));
        Assert.Equal(1, second.Count);
        Assert.Equal(TileBytes, pool.ResidentBytes);
    }

    [Fact]
    public void ACacheWithoutAShareScopeAdoptsNothing()
    {
        var pool = Pool();
        using var first = Sharing(pool);
        using var layer = new TileCache(pool, owner: null);
        first.Put(Key(0), MakeImage());

        Assert.False(layer.Contains(Key(0)));
        Assert.Null(layer.TryGet(Key(0)));

        // Nor does its own tile become one sharing caches adopt.
        layer.Put(Key(1), MakeImage());
        Assert.False(first.Contains(Key(1)));
        Assert.Equal(1, pool.SharedCount);
    }

    [Fact]
    public void AnEqualTilePutTwiceIsKeptOnce()
    {
        var pool = Pool();
        using var first = Sharing(pool);
        using var second = Sharing(pool);
        var kept = MakeImage();
        var duplicate = MakeImage();

        first.Put(Key(0), kept);
        second.Put(Key(0), duplicate);

        Assert.Same(kept, second.TryGet(Key(0)));
        Assert.Equal(nint.Zero, duplicate.Handle);
        Assert.Equal(TileBytes, pool.ResidentBytes);
    }

    [Fact]
    public void ASharedImageLivesUntilItsLastHolderLetsGo()
    {
        var pool = Pool();
        using var first = Sharing(pool);
        using var second = Sharing(pool);
        var image = MakeImage();
        first.Put(Key(0), image);
        Assert.True(second.Contains(Key(0)));

        first.Clear();
        Assert.NotEqual(nint.Zero, image.Handle);
        Assert.Equal(TileBytes, pool.ResidentBytes);
        Assert.Same(image, second.TryGet(Key(0)));

        second.Clear();
        Assert.Equal(nint.Zero, image.Handle);
        Assert.Equal(0, pool.ResidentBytes);
        Assert.Equal(0, pool.SharedCount);
    }

    [Fact]
    public void AWarmCheckAdoptsOnlyATileOfItsRasterScale()
    {
        var pool = Pool();
        using var first = Sharing(pool);
        using var second = Sharing(pool);
        first.Put(Key(0), MakeImage(Size));

        Assert.False(second.Contains(Key(0), Size * 2));
        Assert.Equal(0, second.Count);

        // The drawable check takes a tile of any raster scale.
        Assert.True(second.Contains(Key(0)));
    }

    [Fact]
    public void AnAdoptedTileReplacesOneOfAnotherRasterScale()
    {
        var pool = Pool();
        using var first = Sharing(pool);
        using var second = Sharing(pool);
        var soft = MakeImage(Size);
        second.Put(Key(0), soft);
        var sharp = MakeImage(Size * 2);
        first.Put(Key(0), sharp);

        Assert.True(second.Contains(Key(0), Size * 2));
        Assert.Same(sharp, second.TryGet(Key(0)));
        Assert.Equal(nint.Zero, soft.Handle);
        Assert.Equal(TileCache.BytesFor(sharp), pool.ResidentBytes);
    }

    [Fact]
    public void EvictingOneHolderFreesNothingUntilTheOthersGoToo()
    {
        // Room for two tiles (scaled past the pool's floor).
        var size = 1024;
        var bytes = (long)size * size * 4;
        var pool = Pool(2 * bytes);
        using var first = Sharing(pool);
        using var second = Sharing(pool);
        var shared = MakeImage(size);
        first.Put(Key(0), shared);
        Assert.True(second.Contains(Key(0)));

        // A third tile overflows the pool: both holds on the least recently
        // used tile go, and its image with them.
        first.Put(Key(1), MakeImage(size));
        second.Put(Key(2), MakeImage(size));

        Assert.False(first.Contains(Key(0)));
        Assert.False(second.Contains(Key(0)));
        Assert.Equal(nint.Zero, shared.Handle);
        Assert.Equal(2 * bytes, pool.ResidentBytes);
    }

    [Fact]
    public void CachesOfDifferentScopesShareNothing()
    {
        // Texture caches are scoped to their graphics context.
        var pool = Pool();
        using var first = Sharing(pool, scope: new object());
        using var second = Sharing(pool, scope: new object());
        first.Put(Key(0), MakeImage());

        Assert.False(second.Contains(Key(0)));
        second.Put(Key(0), MakeImage());
        Assert.Equal(2 * TileBytes, pool.ResidentBytes);
    }

    [Fact]
    public void ADeferredCacheFreesItsOwnTextureAtItsNextDrain()
    {
        var clock = new FakeClock();
        var pool = new TileMemoryPool(() => TileCache.MinBudgetBytes, "test", () => clock.Ticks);
        using var cache = new TileCache(pool, owner: null, deferDisposal: true) { ShareScope = Scope };
        var texture = MakeImage();
        cache.Put(Key(0), texture);

        cache.Clear();
        Assert.NotEqual(nint.Zero, texture.Handle);
        cache.DrainPendingDisposals();
        Assert.Equal(nint.Zero, texture.Handle);
    }

    [Fact]
    public void ADeferredSharedTextureWaitsOutTheOrphanDelay()
    {
        // The other cache may have drawn it in the frame still in flight, so
        // the last holder's next frame is too soon to free it.
        var clock = new FakeClock();
        var pool = new TileMemoryPool(() => TileCache.MinBudgetBytes, "test", () => clock.Ticks);
        using var first = new TileCache(pool, owner: null, deferDisposal: true) { ShareScope = Scope };
        using var second = new TileCache(pool, owner: null, deferDisposal: true) { ShareScope = Scope };
        var texture = MakeImage();
        first.Put(Key(0), texture);
        Assert.Same(texture, second.TryGet(Key(0)));

        second.Clear();
        first.Clear();
        first.DrainPendingDisposals();
        Assert.NotEqual(nint.Zero, texture.Handle);

        clock.Advance(TileMemoryPool.OrphanDisposalDelay);
        second.DrainPendingDisposals();
        Assert.Equal(nint.Zero, texture.Handle);
    }

    private sealed class FakeClock
    {
        public long Ticks = 1;

        public void Advance(TimeSpan by) => Ticks += (long)(by.TotalSeconds * Stopwatch.Frequency);
    }
}
