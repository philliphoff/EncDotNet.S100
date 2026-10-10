using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the <c>tiles serve --cache-dir</c> disk cache (issue #865): tiles
/// kept across runs, reused only for the same data and settings, evicted least
/// recently used first across fingerprints, and a clear that touches only the
/// cache's own folders.
/// </summary>
public sealed class TilesServeDiskCacheTests : IDisposable
{
    private const string FingerprintA = "0123456789abcdef0123456789abcdef";
    private const string FingerprintB = "fedcba9876543210fedcba9876543210";

    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiles-disk-cache-" + Guid.NewGuid().ToString("N"));

    public TilesServeDiskCacheTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CacheRoot => Path.Combine(_root, "cache");

    private static TileKey Key(int x, string palette = "day") => new(palette, 3, x, 1);

    private static byte[] Tile(int length, byte fill = 7) => Enumerable.Repeat(fill, length).ToArray();

    [Fact]
    public void Tiles_are_kept_across_runs()
    {
        var first = new DiskTileCache(CacheRoot, FingerprintA, TileImageFormat.Png, 1 << 20);
        first.Add(Key(0), Tile(100));
        first.Add(Key(1), []);
        first.Add(Key(0, "night"), Tile(50, 9));

        Assert.True(File.Exists(Path.Combine(CacheRoot, FingerprintA, "day", "3", "0", "1.png")));

        var second = new DiskTileCache(CacheRoot, FingerprintA, TileImageFormat.Png, 1 << 20);
        Assert.True(second.TryGet(Key(0), out var tile));
        Assert.Equal(Tile(100), tile);
        Assert.True(second.TryGet(Key(1), out var empty));
        Assert.Empty(empty);
        Assert.True(second.TryGet(Key(0, "night"), out var night));
        Assert.Equal(Tile(50, 9), night);
        Assert.False(second.TryGet(Key(2), out _));

        // Another fingerprint's tiles aren't served, though they are counted.
        var other = new DiskTileCache(CacheRoot, FingerprintB, TileImageFormat.Png, 1 << 20);
        Assert.False(other.TryGet(Key(0), out _));
        Assert.Equal(second.Size, other.Size);
    }

    [Fact]
    public void The_least_recently_used_tiles_are_evicted_across_fingerprints()
    {
        // Room for three 1000-byte tiles (each counted with its overhead).
        long budget = 3 * (1000 + 512);
        var stale = new DiskTileCache(CacheRoot, FingerprintB, TileImageFormat.Png, budget);
        stale.Add(Key(0), Tile(1000));
        var staleFile = Path.Combine(CacheRoot, FingerprintB, "day", "3", "0", "1.png");
        File.SetLastWriteTimeUtc(staleFile, DateTime.UtcNow.AddHours(-1));

        var cache = new DiskTileCache(CacheRoot, FingerprintA, TileImageFormat.Png, budget);
        cache.Add(Key(0), Tile(1000));
        cache.Add(Key(1), Tile(1000));
        Assert.True(cache.TryGet(Key(0), out _)); // now more recent than Key(1)

        cache.Add(Key(2), Tile(1000));

        // The stale fingerprint's tile went first.
        Assert.False(File.Exists(staleFile));
        Assert.True(cache.TryGet(Key(1), out _));

        cache.Add(Key(3), Tile(1000));

        // Then the least recently used of this fingerprint's own.
        Assert.False(cache.TryGet(Key(0), out _));
        Assert.True(cache.TryGet(Key(1), out _));
        Assert.True(cache.TryGet(Key(2), out _));
        Assert.True(cache.TryGet(Key(3), out _));
        Assert.True(cache.Size <= budget);

        // A tile larger than the whole budget isn't kept.
        cache.Add(Key(4), Tile(10_000));
        Assert.False(cache.TryGet(Key(4), out _));
    }

    [Fact]
    public void Clearing_deletes_only_the_cache_folders()
    {
        var cache = new DiskTileCache(CacheRoot, FingerprintA, TileImageFormat.Png, 1 << 20);
        cache.Add(Key(0), Tile(10));
        var unrelatedFile = Path.Combine(CacheRoot, "notes.txt");
        var unrelatedFolder = Path.Combine(CacheRoot, "photos");
        File.WriteAllText(unrelatedFile, "keep me");
        Directory.CreateDirectory(unrelatedFolder);

        DiskTileCache.Clear(CacheRoot);

        Assert.False(Directory.Exists(Path.Combine(CacheRoot, FingerprintA)));
        Assert.True(File.Exists(unrelatedFile));
        Assert.True(Directory.Exists(unrelatedFolder));
        Assert.Throws<ArgumentException>(() => new DiskTileCache(CacheRoot, "photos", TileImageFormat.Png, 1 << 20));
    }

    /// <summary>A copy of the S-57 cell in its own folder, so its files can be changed.</summary>
    private string CopyOfCell()
    {
        var folder = Path.Combine(_root, "data");
        Directory.CreateDirectory(folder);
        var cell = Path.Combine(folder, Path.GetFileName(S57));
        File.Copy(S57, cell, overwrite: true);
        return cell;
    }

    private static string FingerprintOf(TilesServeCommand.Settings settings)
    {
        Assert.Equal(0, TileRenderSession.TryOpen(settings, out var session));
        using (session)
            return session!.Fingerprint();
    }

    [Fact]
    public void The_fingerprint_follows_the_data_and_the_settings()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = CopyOfCell();

        var fingerprint = FingerprintOf(new TilesServeCommand.Settings { Input = cell });
        Assert.True(DiskTileCache.IsFingerprint(fingerprint));
        Assert.Equal(fingerprint, FingerprintOf(new TilesServeCommand.Settings { Input = cell }));

        // The palette is part of each tile's key, not the fingerprint.
        Assert.Equal(fingerprint, FingerprintOf(new TilesServeCommand.Settings { Input = cell, Palette = "night" }));

        // Settings that change pixels change it.
        Assert.NotEqual(fingerprint, FingerprintOf(new TilesServeCommand.Settings { Input = cell, TextScale = 1.5 }));
        Assert.NotEqual(fingerprint, FingerprintOf(new TilesServeCommand.Settings { Input = cell, Format = "webp" }));

        // So does the data: an update file beside the cell, or the cell itself changing.
        File.WriteAllBytes(Path.ChangeExtension(cell, ".001"), [1, 2, 3]);
        var withUpdate = FingerprintOf(new TilesServeCommand.Settings { Input = cell, NoUpdates = true });
        File.Delete(Path.ChangeExtension(cell, ".001"));
        Assert.NotEqual(FingerprintOf(new TilesServeCommand.Settings { Input = cell, NoUpdates = true }), withUpdate);

        File.SetLastWriteTimeUtc(cell, DateTime.UtcNow.AddMinutes(5));
        Assert.NotEqual(fingerprint, FingerprintOf(new TilesServeCommand.Settings { Input = cell }));
    }

    [Fact]
    public async Task A_restarted_server_serves_cached_tiles_without_rendering()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = CopyOfCell();
        var settings = new TilesServeCommand.Settings
        {
            Input = cell,
            MinZoom = 14,
            MaxZoom = 14,
            Parallel = 2,
            CacheDirectory = CacheRoot,
        };
        Assert.True(settings.Validate().Successful);

        var tiles = new Dictionary<(int X, int Y), byte[]?>();
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var first));
        using (first)
        {
            var area = first!.Layout.Area;
            var (firstRow, lastRow) = XyzTileGrid.Rows(14, area.MinY, area.MaxY);
            foreach (int x in XyzTileGrid.Columns(14, area.MinX, area.MaxX))
            {
                for (int y = firstRow; y <= lastRow; y++)
                    tiles[(x, y)] = await first.ReadAsync(14, x, y, "night", TestContext.Current.CancellationToken);
            }

            Assert.True(first.BlocksRendered > 0);
            Assert.Contains(tiles.Values, t => t is not null);
        }

        // A new run with the same data and settings renders nothing.
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var second));
        using (second)
        {
            foreach (var ((x, y), tile) in tiles)
                Assert.Equal(tile, await second!.ReadAsync(14, x, y, "night", TestContext.Current.CancellationToken));
            Assert.Equal(0, second!.BlocksRendered);
        }

        // Different settings render afresh, beside the old tiles.
        var larger = new TilesServeCommand.Settings
        {
            Input = cell,
            MinZoom = 14,
            MaxZoom = 14,
            TextScale = 1.5,
            CacheDirectory = CacheRoot,
        };
        Assert.Equal(0, TilesServeCommand.OpenRendered(larger, out var third));
        using (third)
        {
            var (x, y) = tiles.First(t => t.Value is not null).Key;
            Assert.NotNull(await third!.ReadAsync(14, x, y, "night", TestContext.Current.CancellationToken));
            Assert.Equal(1, third.BlocksRendered);
        }

        Assert.Equal(2, Directory.EnumerateDirectories(CacheRoot).Count(d => DiskTileCache.IsFingerprint(Path.GetFileName(d))));

        // --clear-cache starts from nothing.
        var cleared = new TilesServeCommand.Settings
        {
            Input = cell,
            MinZoom = 14,
            MaxZoom = 14,
            CacheDirectory = CacheRoot,
            ClearCache = true,
        };
        Assert.Equal(0, TilesServeCommand.OpenRendered(cleared, out var fourth));
        using (fourth)
        {
            var (x, y) = tiles.First(t => t.Value is not null).Key;
            await fourth!.ReadAsync(14, x, y, "night", TestContext.Current.CancellationToken);
            Assert.Equal(1, fourth.BlocksRendered);
        }
    }

    [Theory]
    [InlineData(null, true, 1024, "--clear-cache needs --cache-dir")]
    [InlineData("cache", false, 0, "--cache-dir-mb must be at least 1")]
    public void Cache_options_are_validated(string? cacheDirectory, bool clear, int megabytes, string error)
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var settings = new TilesServeCommand.Settings
        {
            Input = S57,
            CacheDirectory = cacheDirectory is null ? null : Path.Combine(_root, cacheDirectory),
            ClearCache = clear,
            CacheDirectoryMegabytes = megabytes,
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains(error, result.Message);
    }

    [Fact]
    public void A_built_tile_set_takes_no_disk_cache()
    {
        var result = new TilesServeCommand.Settings { Input = _root, CacheDirectory = CacheRoot }.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--cache-dir applies only when rendering datasets", result.Message);
    }
}
