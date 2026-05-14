using EncDotNet.S100.Core;

namespace EncDotNet.S100.Core.Tests;

public class CachingAssetSourceTests
{
    [Fact]
    public async Task GetAsync_ReturnsSameBytesOnRepeatedReads()
    {
        var inner = new InMemoryAssetSource(new()
        {
            ["a.txt"] = [1, 2, 3],
        });
        using var cache = new CachingAssetSource(inner);

        AssetBytes first = await cache.GetAsync("a.txt");
        AssetBytes second = await cache.GetAsync("a.txt");

        Assert.Equal(new byte[] { 1, 2, 3 }, first.Bytes.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, second.Bytes.ToArray());
        Assert.Equal(1, inner.OpenCount("a.txt"));
    }

    [Fact]
    public async Task GetAsync_DifferentPathsAreIndependent()
    {
        var inner = new InMemoryAssetSource(new()
        {
            ["a.txt"] = [1],
            ["b.txt"] = [2],
        });
        using var cache = new CachingAssetSource(inner);

        AssetBytes a = await cache.GetAsync("a.txt");
        AssetBytes b = await cache.GetAsync("b.txt");
        AssetBytes a2 = await cache.GetAsync("a.txt");

        Assert.Equal(new byte[] { 1 }, a.Bytes.ToArray());
        Assert.Equal(new byte[] { 2 }, b.Bytes.ToArray());
        Assert.Equal(new byte[] { 1 }, a2.Bytes.ToArray());
        Assert.Equal(1, inner.OpenCount("a.txt"));
        Assert.Equal(1, inner.OpenCount("b.txt"));
    }

    [Fact]
    public async Task GetAsync_KeyIsCaseSensitive()
    {
        // Ordinal keys: "A.txt" and "a.txt" should be distinct cache
        // entries. The underlying source decides case semantics.
        var inner = new InMemoryAssetSource(new()
        {
            ["a.txt"] = [1],
            ["A.txt"] = [2],
        });
        using var cache = new CachingAssetSource(inner);

        AssetBytes lower = await cache.GetAsync("a.txt");
        AssetBytes upper = await cache.GetAsync("A.txt");

        Assert.Equal(new byte[] { 1 }, lower.Bytes.ToArray());
        Assert.Equal(new byte[] { 2 }, upper.Bytes.ToArray());
        Assert.Equal(1, inner.OpenCount("a.txt"));
        Assert.Equal(1, inner.OpenCount("A.txt"));
    }

    [Fact]
    public async Task ConcurrentFirstReads_OpenInnerExactlyOnce()
    {
        var inner = new InMemoryAssetSource(
            new() { ["hot.txt"] = [9, 9, 9] },
            openDelay: TimeSpan.FromMilliseconds(50));
        using var cache = new CachingAssetSource(inner);

        Task<AssetBytes>[] tasks = Enumerable
            .Range(0, 32)
            .Select(_ => Task.Run(() => cache.GetAsync("hot.txt")))
            .ToArray();

        AssetBytes[] results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(new byte[] { 9, 9, 9 }, r.Bytes.ToArray()));
        Assert.Equal(1, inner.OpenCount("hot.txt"));
    }

    [Fact]
    public async Task OpenAsync_ReturnsFreshStreamPerCall()
    {
        var inner = new InMemoryAssetSource(new()
        {
            ["a.txt"] = [1, 2, 3, 4, 5],
        });
        using var cache = new CachingAssetSource(inner);

        await using Stream first = await cache.OpenAsync("a.txt");
        await using Stream second = await cache.OpenAsync("a.txt");

        // Consuming `first` should not affect `second`.
        Assert.Equal(1, first.ReadByte());
        Assert.Equal(1, second.ReadByte());
        Assert.Equal(2, first.ReadByte());
        Assert.Equal(2, second.ReadByte());

        // And the inner source was opened exactly once.
        Assert.Equal(1, inner.OpenCount("a.txt"));
    }

    [Fact]
    public async Task OpenAsync_ServesBytesIdenticalToReadAllBytesAsync()
    {
        var inner = new InMemoryAssetSource(new()
        {
            ["a.txt"] = [10, 20, 30, 40],
        });
        using var cache = new CachingAssetSource(inner);

        AssetBytes bytes = await cache.GetAsync("a.txt");
        await using Stream stream = await cache.OpenAsync("a.txt");
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);

        Assert.Equal(bytes.Bytes.ToArray(), copy.ToArray());
    }

    [Fact]
    public void Dispose_ForwardsToInner()
    {
        var inner = new InMemoryAssetSource(new());
        var cache = new CachingAssetSource(inner);

        cache.Dispose();

        Assert.True(inner.Disposed);
    }

    [Fact]
    public async Task GetAsync_PropagatesFileNotFoundOnMiss()
    {
        var inner = new InMemoryAssetSource(new());
        using var cache = new CachingAssetSource(inner);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => cache.GetAsync("missing"));
    }

    [Fact]
    public void Constructor_ThrowsOnNullInner()
    {
        Assert.Throws<ArgumentNullException>(() => new CachingAssetSource(null!));
    }

    [Fact]
    public async Task GetAsync_ThrowsOnEmptyPath()
    {
        var inner = new InMemoryAssetSource(new());
        using var cache = new CachingAssetSource(inner);

        await Assert.ThrowsAsync<ArgumentException>(() => cache.GetAsync(""));
    }
}
