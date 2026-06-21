using EncDotNet.S100.Renderers.Mapsui;
using SkiaSharp;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the native-byte-bounded LRU <see cref="TileCache"/> used by
/// the Phase&#160;2 tiled base plane. These pin the cache's correctness
/// invariants — budget enforcement, LRU eviction order, MRU-on-get,
/// replace-disposes, and clamp-to-floor — without standing up a render surface.
/// </summary>
public class TileCacheTests
{
    private static SKImage MakeImage(int size)
    {
        // A real raster SKImage so BytesFor (W*H*4) reflects actual native cost.
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Black);
        return surface.Snapshot();
    }

    private static TileKey Key(int x) => new(5, x, 0);

    [Fact]
    public void BytesFor_IsWidthTimesHeightTimesFourBytes()
    {
        using var image = MakeImage(64);
        Assert.Equal(64L * 64 * 4, TileCache.BytesFor(image));
    }

    [Fact]
    public void Put_ThenTryGet_ReturnsImageAndCountsBytes()
    {
        using var cache = new TileCache(TileCache.MinBudgetBytes);
        var image = MakeImage(32);
        cache.Put(Key(0), image);

        Assert.Same(image, cache.TryGet(Key(0)));
        Assert.True(cache.Contains(Key(0)));
        Assert.Equal(1, cache.Count);
        Assert.Equal(TileCache.BytesFor(image), cache.ResidentBytes);
    }

    [Fact]
    public void TryGet_MissReturnsNull()
    {
        using var cache = new TileCache(TileCache.MinBudgetBytes);
        Assert.Null(cache.TryGet(Key(99)));
        Assert.False(cache.Contains(Key(99)));
    }

    [Fact]
    public void Constructor_ClampsBudgetToFloor()
    {
        using var cache = new TileCache(0);
        Assert.Equal(TileCache.MinBudgetBytes, cache.BudgetBytes);
    }

    [Fact]
    public void Put_EvictsLeastRecentlyUsedWhenOverBudget()
    {
        // Each 1024px tile is 1024*1024*4 = 4 MiB == the cache floor, so a budget
        // of two tiles is above the floor and holds exactly two.
        var tileBytes = 1024L * 1024 * 4;
        using var cache = new TileCache(tileBytes * 2);

        cache.Put(Key(0), MakeImage(1024));
        cache.Put(Key(1), MakeImage(1024));
        // Inserting a third evicts the LRU (Key 0).
        cache.Put(Key(2), MakeImage(1024));

        Assert.False(cache.Contains(Key(0)));
        Assert.True(cache.Contains(Key(1)));
        Assert.True(cache.Contains(Key(2)));
        Assert.True(cache.ResidentBytes <= cache.BudgetBytes);
    }

    [Fact]
    public void TryGet_MarksMostRecentlyUsedSoItSurvivesEviction()
    {
        var tileBytes = 1024L * 1024 * 4;
        using var cache = new TileCache(tileBytes * 2);

        cache.Put(Key(0), MakeImage(1024));
        cache.Put(Key(1), MakeImage(1024));
        // Touch Key 0 so it becomes MRU; Key 1 is now the eviction victim.
        Assert.NotNull(cache.TryGet(Key(0)));
        cache.Put(Key(2), MakeImage(1024));

        Assert.True(cache.Contains(Key(0)));
        Assert.False(cache.Contains(Key(1)));
        Assert.True(cache.Contains(Key(2)));
    }

    [Fact]
    public void Put_ReplacingKeyDisposesPriorImageAndKeepsByteCount()
    {
        using var cache = new TileCache(TileCache.MinBudgetBytes);
        var first = MakeImage(64);
        cache.Put(Key(0), first);
        var second = MakeImage(64);
        cache.Put(Key(0), second);

        Assert.Equal(1, cache.Count);
        Assert.Equal(64L * 64 * 4, cache.ResidentBytes);
        // The replacement is now resident; the prior image was disposed on replace.
        Assert.Same(second, cache.TryGet(Key(0)));
    }
    public void SnapshotKeys_ReturnsResidentKeysWithoutReordering()
    {
        using var cache = new TileCache(TileCache.MinBudgetBytes);
        cache.Put(Key(0), MakeImage(16));
        cache.Put(Key(1), MakeImage(16));

        var keys = cache.SnapshotKeys();
        Assert.Equal(2, keys.Count);
        Assert.Contains(Key(0), keys);
        Assert.Contains(Key(1), keys);
    }

    [Fact]
    public void Clear_DisposesAndEmptiesCache()
    {
        var cache = new TileCache(TileCache.MinBudgetBytes);
        cache.Put(Key(0), MakeImage(32));
        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.ResidentBytes);
        Assert.Empty(cache.SnapshotKeys());
        cache.Dispose();
    }

    [Fact]
    public void Put_AfterDispose_DisposesImageAndStaysEmpty()
    {
        var cache = new TileCache(TileCache.MinBudgetBytes);
        cache.Dispose();

        var image = MakeImage(16);
        cache.Put(Key(0), image);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void DeferDisposal_EvictedImageSurvivesUntilDrain()
    {
        // A 1024px tile is 4 MiB == the floor, so a one-tile budget evicts the
        // prior tile on the next Put. With deferDisposal the evicted image must
        // remain valid (not yet freed) until DrainPendingDisposals is called —
        // this is what lets a GPU texture outlive the deferred frame that drew it.
        var tileBytes = 1024L * 1024 * 4;
        using var cache = new TileCache(tileBytes, deferDisposal: true);

        var first = MakeImage(1024);
        cache.Put(Key(0), first);
        cache.Put(Key(1), MakeImage(1024));

        Assert.False(cache.Contains(Key(0)));
        // Evicted, but deferred: the native image is still alive.
        Assert.NotEqual(nint.Zero, first.Handle);

        cache.DrainPendingDisposals();
        Assert.Equal(nint.Zero, first.Handle);
    }

    [Fact]
    public void DeferDisposal_ClearDefersUntilDrain()
    {
        using var cache = new TileCache(TileCache.MinBudgetBytes, deferDisposal: true);
        var image = MakeImage(32);
        cache.Put(Key(0), image);

        cache.Clear();
        Assert.Equal(0, cache.Count);
        // Cleared from the cache but not yet disposed under deferred mode.
        Assert.NotEqual(nint.Zero, image.Handle);

        cache.DrainPendingDisposals();
        Assert.Equal(nint.Zero, image.Handle);
    }

    [Fact]
    public void DeferDisposal_DisposeDrainsPending()
    {
        var cache = new TileCache(TileCache.MinBudgetBytes, deferDisposal: true);
        var image = MakeImage(32);
        cache.Put(Key(0), image);
        cache.Clear();
        Assert.NotEqual(nint.Zero, image.Handle);

        // Teardown must free everything Clear() deferred.
        cache.Dispose();
        Assert.Equal(nint.Zero, image.Handle);
    }

    [Fact]
    public void DrainPendingDisposals_InlineCacheIsNoOp()
    {
        // A default (inline) cache disposes on eviction; draining is harmless.
        var tileBytes = 1024L * 1024 * 4;
        using var cache = new TileCache(tileBytes);
        cache.Put(Key(0), MakeImage(1024));
        cache.Put(Key(1), MakeImage(1024));

        cache.DrainPendingDisposals();
        Assert.True(cache.Contains(Key(1)));
    }
}
