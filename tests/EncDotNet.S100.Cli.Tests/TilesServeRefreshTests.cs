using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>tiles serve --refresh</c> (issue #865): datasets reopened when
/// their files change and the change has settled, the old ones serving until
/// then, and a failed reopen leaving them serving.
/// </summary>
public sealed class TilesServeRefreshTests : IDisposable
{
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiles-serve-refresh-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];
    private int _opens;
    private int _touches;

    public TilesServeRefreshTests() => Directory.CreateDirectory(_root);

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

    private string CopyOfCell()
    {
        var cell = Path.Combine(_root, Path.GetFileName(S57));
        File.Copy(S57, cell, overwrite: true);
        return cell;
    }

    private static TilesServeCommand.Settings SettingsFor(string cell, string? cacheDirectory = null) => new()
    {
        Input = cell,
        MinZoom = 14,
        MaxZoom = 14,
        Parallel = 2,
        CacheDirectory = cacheDirectory,
    };

    /// <summary>A refreshing source that only checks when the test asks, and counts reopenings.</summary>
    private RefreshingTileSource Refreshing(TilesServeCommand.Settings settings, Func<bool>? canOpen = null)
    {
        var fingerprint = TileRenderSession.Fingerprint(settings);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var initial));
        return new RefreshingTileSource(
            initial!,
            fingerprint,
            () => TileRenderSession.Fingerprint(settings),
            () =>
            {
                Interlocked.Increment(ref _opens);
                if (canOpen is not null && !canOpen())
                    return null;
                return TilesServeCommand.OpenRendered(settings, out var reopened) == 0 ? reopened : null;
            },
            Timeout.InfiniteTimeSpan,
            line =>
            {
                lock (_log)
                    _log.Add(line);
            });
    }

    /// <summary>Changes the cell's write time, as copying a new version over it would.</summary>
    private void Touch(string cell) =>
        File.SetLastWriteTimeUtc(cell, DateTime.UtcNow.AddMinutes(++_touches));

    private static async Task<(int X, int Y, byte[] Tile)> DrawnTileAsync(ITileSource source, TileLayout layout)
    {
        var (firstRow, lastRow) = XyzTileGrid.Rows(14, layout.Area.MinY, layout.Area.MaxY);
        foreach (int x in XyzTileGrid.Columns(14, layout.Area.MinX, layout.Area.MaxX))
        {
            for (int y = firstRow; y <= lastRow; y++)
            {
                if (await source.ReadAsync(14, x, y, null, null, TestContext.Current.CancellationToken) is { } tile)
                    return (x, y, tile);
            }
        }

        throw new InvalidOperationException("No tile drew anything.");
    }

    [Fact]
    public async Task Unchanged_datasets_are_not_reopened()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        await using var source = Refreshing(SettingsFor(CopyOfCell()));

        for (int i = 0; i < 3; i++)
            Assert.False(await source.CheckAsync());

        Assert.Equal(0, _opens);
        Assert.Equal(0, source.Reloads);
    }

    [Fact]
    public async Task A_settled_change_reopens_the_datasets_and_drops_their_cached_tiles()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = CopyOfCell();
        await using var source = Refreshing(SettingsFor(cell));
        var before = source.Current;
        var (x, y, tile) = await DrawnTileAsync(source, before.Layout);
        Assert.Equal(1, before.BlocksRendered);

        Touch(cell);

        // Seen, then confirmed unchanged at the next check, then reopened.
        Assert.False(await source.CheckAsync());
        Assert.Same(before, source.Current);
        Assert.True(await source.CheckAsync());

        var after = source.Current;
        Assert.NotSame(before, after);
        Assert.Equal(1, source.Reloads);
        Assert.Equal(1, _opens);
        Assert.Contains(_log, l => l.Contains("The datasets changed", StringComparison.Ordinal));

        // The tile is rendered again from the reopened datasets.
        Assert.Equal(tile, await source.ReadAsync(14, x, y, null, null, TestContext.Current.CancellationToken));
        Assert.Equal(1, after.BlocksRendered);

        // And the change is not acted on twice.
        Assert.False(await source.CheckAsync());
        Assert.Equal(1, _opens);
    }

    [Fact]
    public async Task A_file_still_changing_is_not_reopened_until_it_settles()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = CopyOfCell();
        await using var source = Refreshing(SettingsFor(cell));

        for (int i = 0; i < 3; i++)
        {
            Touch(cell);
            Assert.False(await source.CheckAsync());
        }

        Assert.Equal(0, _opens);
        Assert.True(await source.CheckAsync());
        Assert.Equal(1, _opens);
    }

    [Fact]
    public async Task A_failed_reopen_keeps_serving_and_waits_for_the_next_change()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = CopyOfCell();
        bool canOpen = false;
        await using var source = Refreshing(SettingsFor(cell), () => canOpen);
        var before = source.Current;

        Touch(cell);
        Assert.False(await source.CheckAsync());
        Assert.False(await source.CheckAsync());
        Assert.Equal(1, _opens);
        Assert.Same(before, source.Current);

        // Not retried while the files stay as they are…
        canOpen = true;
        Assert.False(await source.CheckAsync());
        Assert.False(await source.CheckAsync());
        Assert.Equal(1, _opens);

        // …but tried again when they change again.
        Touch(cell);
        Assert.False(await source.CheckAsync());
        Assert.True(await source.CheckAsync());
        Assert.Equal(2, _opens);
        Assert.NotSame(before, source.Current);
    }

    [Fact]
    public async Task A_reopen_renders_into_a_new_disk_cache_folder()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = CopyOfCell();
        var cache = Path.Combine(_root, "cache");
        await using var source = Refreshing(SettingsFor(cell, cache));
        await DrawnTileAsync(source, source.Current.Layout);

        Touch(cell);
        await source.CheckAsync();
        Assert.True(await source.CheckAsync());
        await DrawnTileAsync(source, source.Current.Layout);

        // The old tiles weren't reused; both fingerprints' tiles are on disk.
        Assert.Equal(1, source.Current.BlocksRendered);
        Assert.Equal(2, Directory.EnumerateDirectories(cache).Count(d => DiskTileCache.IsFingerprint(Path.GetFileName(d))));
    }

    [Fact]
    public async Task The_refresh_loop_picks_up_a_change_on_its_own()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var cell = CopyOfCell();
        var settings = SettingsFor(cell);
        var fingerprint = TileRenderSession.Fingerprint(settings);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var initial));
        await using var source = new RefreshingTileSource(
            initial!,
            fingerprint,
            () => TileRenderSession.Fingerprint(settings),
            () => TilesServeCommand.OpenRendered(settings, out var reopened) == 0 ? reopened : null,
            TimeSpan.FromMilliseconds(50),
            _ => { });

        Touch(cell);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (source.Reloads == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, source.Reloads);

        // One change, one reload: later checks find nothing new.
        await Task.Delay(1000, TestContext.Current.CancellationToken);
        Assert.Equal(1, source.Reloads);
    }

    [Fact]
    public void A_negative_refresh_is_rejected()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var result = new TilesServeCommand.Settings { Input = S57, RefreshSeconds = -1 }.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--refresh", result.Message);
    }
}
