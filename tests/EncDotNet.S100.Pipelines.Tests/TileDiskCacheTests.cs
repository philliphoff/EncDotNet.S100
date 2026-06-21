using System;
using System.IO;
using System.Linq;
using EncDotNet.S100.Renderers.Mapsui;
using SkiaSharp;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the persistent <see cref="TileDiskCache"/> warm tier used by
/// the Phase&#160;4 tiled base plane. These pin its correctness invariants —
/// round-trip read/write, namespace isolation (the styleStateHash safety
/// property), miss handling, and byte-budget eviction — against a throwaway
/// temp directory, without standing up a render surface.
/// </summary>
public class TileDiskCacheTests : IDisposable
{
    private readonly string _root;

    public TileDiskCacheTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "encdotnet-s100-tile-tests", Path.GetRandomFileName());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private static SKImage NoiseImage(int size, int seed)
    {
        // PNG-encodable noise so encoded size is non-trivial (a solid colour
        // compresses to almost nothing, defeating the eviction test). Alpha is
        // forced opaque (255) so premultiply round-trips losslessly through PNG.
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bytes = new byte[info.BytesSize];
        new Random(seed).NextBytes(bytes);
        for (var i = 3; i < bytes.Length; i += 4)
        {
            bytes[i] = 255;
        }

        return SKImage.FromPixelCopy(info, bytes);
    }

    private static byte[] Pixels(SKImage image)
    {
        // Normalise both images into one canonical layout so the comparison is
        // immune to the decoder choosing a different colour type / alpha type.
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var ok = image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
        Assert.True(ok, "ReadPixels failed");
        return bitmap.Bytes;
    }

    private static TileKey Key(int band, int x, int y) => new(band, x, y);

    [Fact]
    public void NamespaceFor_IsStableForSameInputs()
    {
        var a = TileDiskCache.NamespaceFor("101AU005", "stylehash-1");
        var b = TileDiskCache.NamespaceFor("101AU005", "stylehash-1");
        Assert.Equal(a, b);
    }

    [Fact]
    public void NamespaceFor_DiffersWhenStyleStateHashDiffers()
    {
        var day = TileDiskCache.NamespaceFor("101AU005", "day");
        var night = TileDiskCache.NamespaceFor("101AU005", "night");
        Assert.NotEqual(day, night);
    }

    [Fact]
    public void NamespaceFor_DiffersWhenProductLayerSetDiffers()
    {
        var a = TileDiskCache.NamespaceFor("cellA", "style");
        var b = TileDiskCache.NamespaceFor("cellB", "style");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Write_ThenTryRead_RoundTripsPixels()
    {
        var cache = new TileDiskCache(_root, 64L * 1024 * 1024);
        var ns = TileDiskCache.NamespaceFor("cell", "style");
        using var original = NoiseImage(32, seed: 1);

        cache.Write(ns, Key(5, 1, 2), original);
        using var read = cache.TryRead(ns, Key(5, 1, 2));

        Assert.NotNull(read);
        Assert.Equal(Pixels(original), Pixels(read!));
    }

    [Fact]
    public void TryRead_AbsentKey_ReturnsNull()
    {
        var cache = new TileDiskCache(_root, 64L * 1024 * 1024);
        var ns = TileDiskCache.NamespaceFor("cell", "style");

        Assert.Null(cache.TryRead(ns, Key(0, 0, 0)));
    }

    [Fact]
    public void TryRead_DifferentNamespace_DoesNotSeeTile()
    {
        // The safety property: a tile written under one style state is never
        // served for a different one.
        var cache = new TileDiskCache(_root, 64L * 1024 * 1024);
        var day = TileDiskCache.NamespaceFor("cell", "day");
        var night = TileDiskCache.NamespaceFor("cell", "night");
        using var image = NoiseImage(32, seed: 7);

        cache.Write(day, Key(5, 1, 1), image);

        Assert.NotNull(cache.TryRead(day, Key(5, 1, 1)));
        Assert.Null(cache.TryRead(night, Key(5, 1, 1)));
    }

    [Fact]
    public void TryRead_NullOrEmptyNamespace_ReturnsNull()
    {
        var cache = new TileDiskCache(_root, 64L * 1024 * 1024);
        Assert.Null(cache.TryRead("", Key(0, 0, 0)));
    }

    [Fact]
    public void Write_EnforcesByteBudget_EvictingLeastRecentlyUsed()
    {
        // Size the budget to a few tiles, then write well past it (a multiple of
        // the internal sweep interval so a sweep runs on the final write).
        var ns = TileDiskCache.NamespaceFor("cell", "style");

        // Measure one encoded tile to derive a budget of ~4 tiles.
        long oneTileBytes;
        {
            var probe = new TileDiskCache(Path.Combine(_root, "probe"), 64L * 1024 * 1024);
            using var img = NoiseImage(48, seed: 99);
            probe.Write(ns, Key(0, 0, 0), img);
            var file = Directory.GetFiles(probe.RootDirectory, "*.png", SearchOption.AllDirectories).Single();
            oneTileBytes = new FileInfo(file).Length;
        }

        var budget = oneTileBytes * 4;
        var cache = new TileDiskCache(_root, budget);

        const int count = 64; // 2 × CapSweepInterval, so a sweep runs on write 64.
        for (var i = 0; i < count; i++)
        {
            using var img = NoiseImage(48, seed: 1000 + i);
            cache.Write(ns, Key(5, i, 0), img);
        }

        var total = Directory
            .GetFiles(cache.RootDirectory, "*.png", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);

        Assert.True(total <= budget, $"on-disk total {total} should be within budget {budget}");

        // The most-recently-written tile survives eviction.
        using var newest = cache.TryRead(ns, Key(5, count - 1, 0));
        Assert.NotNull(newest);
    }

    [Fact]
    public void Constructor_RejectsEmptyRootAndNonPositiveBudget()
    {
        Assert.Throws<ArgumentException>(() => new TileDiskCache("", 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileDiskCache(_root, 0));
    }
}
