using System.Collections.Concurrent;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="PortrayalCacheWarmer"/> (#1266): portrays registered cells one at
/// a time while the map is idle, nearest the view first; gives way when the map
/// gets busy; stops short of the cache cap; and resumes from its ledger.
/// </summary>
public sealed class PortrayalCacheWarmerTests : IDisposable
{
    private static readonly PortrayalWarmOptions FastOptions = new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(20),
        PollInterval = TimeSpan.FromMilliseconds(5),
        IdleInterval = TimeSpan.FromMilliseconds(20),
        SaveInterval = TimeSpan.Zero,
    };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "encdotnet-warm-" + Guid.NewGuid().ToString("N"));

    public PortrayalCacheWarmerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    private static DatasetEntry Cell(string name, double lat, double lon, EncDotNet.S100.Core.IAssetSource? source = null)
        => new(
            filePath: name + ".000",
            productSpec: "S-57",
            source: source,
            relativePath: name + ".000",
            displayName: name,
            geographicBounds: new BoundingBox
            {
                WestBoundLongitude = lon - 0.1,
                EastBoundLongitude = lon + 0.1,
                SouthBoundLatitude = lat - 0.1,
                NorthBoundLatitude = lat + 0.1,
            });

    private static MapViewportSnapshot ViewAt(double lat, double lon) => new()
    {
        MinLatitude = lat - 0.05,
        MaxLatitude = lat + 0.05,
        MinLongitude = lon - 0.05,
        MaxLongitude = lon + 0.05,
        MercatorResolution = 5,
    };

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(5);
        }
        return condition();
    }

    /// <summary>A test double for everything the warmer reads from the viewer.</summary>
    private sealed class Host
    {
        public List<DatasetEntry> Cells { get; } = [];
        public ConcurrentQueue<string> Portrayed { get; } = new();
        public HashSet<string> Cached { get; } = new(StringComparer.Ordinal);
        public volatile bool Busy;
        public volatile bool Enabled = true;
        public volatile bool Room = true;
        public string Signature = "sig-1";
        public MapViewportSnapshot? View;
        public Func<DatasetEntry, CancellationToken, Task<IReadOnlyList<string>?>>? Portray;

        public PortrayalCacheWarmer Create(PortrayalWarmLedger ledger) => new(
            candidates: () => { lock (Cells) return Cells.ToList(); },
            portrayAsync: (entry, ct) => Portray is { } p ? p(entry, ct) : Default(entry),
            isBusy: () => Busy,
            view: () => View,
            isEnabled: () => Enabled,
            hasCacheRoom: () => Room,
            isCached: key => { lock (Cached) return Cached.Contains(key); },
            signature: () => Volatile.Read(ref Signature),
            ledger: ledger,
            options: FastOptions,
            time: TimeProvider.System,
            logger: NullLogger<PortrayalCacheWarmer>.Instance);

        public Task<IReadOnlyList<string>?> Default(DatasetEntry entry)
        {
            var key = "key:" + entry.DisplayName + ":" + Volatile.Read(ref Signature);
            lock (Cached) Cached.Add(key);
            Portrayed.Enqueue(entry.DisplayName);
            return Task.FromResult<IReadOnlyList<string>?>([key]);
        }
    }

    [Fact]
    public async Task PortraysEveryCell_NearestTheViewFirst_ThenReportsComplete()
    {
        var host = new Host { View = ViewAt(47.6, -122.4) };
        host.Cells.AddRange([Cell("far", 46.0, -124.0), Cell("near", 47.6, -122.4), Cell("mid", 47.0, -122.8)]);
        using var warmer = host.Create(new PortrayalWarmLedger(null));
        var statuses = new ConcurrentQueue<PortrayalWarmStatus>();
        warmer.StatusChanged += (_, s) => statuses.Enqueue(s);

        warmer.Start();

        Assert.True(await WaitUntilAsync(() => warmer.Status.State == PortrayalWarmState.Complete));
        Assert.Equal(["near", "mid", "far"], host.Portrayed.ToArray());
        Assert.Equal(new PortrayalWarmStatus(PortrayalWarmState.Complete, 3, 3), warmer.Status);
        Assert.Contains(statuses, s => s.State == PortrayalWarmState.Working);
    }

    [Fact]
    public async Task WaitsWhileTheMapIsBusy_ThenStartsOnceItIsQuiet()
    {
        var host = new Host { Busy = true };
        host.Cells.Add(Cell("a", 47.6, -122.4));
        using var warmer = host.Create(new PortrayalWarmLedger(null));

        warmer.Start();
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Empty(host.Portrayed);

        host.Busy = false;
        Assert.True(await WaitUntilAsync(() => host.Portrayed.Count == 1));
    }

    [Fact]
    public async Task GivesWayWhenTheMapGetsBusy_AndRetriesTheCellLater()
    {
        var host = new Host();
        host.Cells.Add(Cell("big", 47.6, -122.4));
        var attempts = 0;
        var firstCancelled = new TaskCompletionSource();
        host.Portray = async (entry, ct) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                // A long portrayal that honours cancellation.
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { firstCancelled.TrySetResult(); throw; }
            }
            return await host.Default(entry);
        };
        using var warmer = host.Create(new PortrayalWarmLedger(null));

        warmer.Start();
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref attempts) == 1));
        host.Busy = true;
        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(await WaitUntilAsync(() => warmer.Status.State == PortrayalWarmState.Paused));
        Assert.Empty(host.Portrayed);

        host.Busy = false;
        Assert.True(await WaitUntilAsync(() => warmer.Status.State == PortrayalWarmState.Complete));
        Assert.Equal(2, attempts);
        Assert.Equal(["big"], host.Portrayed.ToArray());
    }

    [Fact]
    public async Task CancelsTheRunningCell_WhenItStartsLoadingForReal()
    {
        var host = new Host();
        var cell = Cell("a", 47.6, -122.4);
        host.Cells.Add(cell);
        var cancelled = new TaskCompletionSource();
        host.Portray = async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return null;
        };
        using var warmer = host.Create(new PortrayalWarmLedger(null));

        warmer.Start();
        Assert.True(await WaitUntilAsync(() => warmer.Status.State == PortrayalWarmState.Working));
        cell.IsLoading = true;

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopsWhenTheCacheHasNoRoom()
    {
        var host = new Host { Room = false };
        host.Cells.Add(Cell("a", 47.6, -122.4));
        using var warmer = host.Create(new PortrayalWarmLedger(null));

        warmer.Start();

        Assert.True(await WaitUntilAsync(() => warmer.Status.State == PortrayalWarmState.CacheFull));
        Assert.Equal(new PortrayalWarmStatus(PortrayalWarmState.CacheFull, 0, 1), warmer.Status);
        Assert.Empty(host.Portrayed);
    }

    [Fact]
    public async Task DoesNothingWhileSwitchedOff()
    {
        var host = new Host { Enabled = false };
        host.Cells.Add(Cell("a", 47.6, -122.4));
        using var warmer = host.Create(new PortrayalWarmLedger(null));

        warmer.Start();

        Assert.True(await WaitUntilAsync(() => warmer.Status.State == PortrayalWarmState.Off));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(host.Portrayed);

        host.Enabled = true;
        Assert.True(await WaitUntilAsync(() => host.Portrayed.Count == 1));
    }

    [Fact]
    public async Task IgnoresLoadedCells_AndSkipsCellsThatFailToPortray()
    {
        var host = new Host();
        var loaded = Cell("loaded", 47.6, -122.4);
        loaded.IsLoaded = true;
        host.Cells.AddRange([loaded, Cell("broken", 47.6, -122.4), Cell("raster", 47.6, -122.4)]);
        var calls = new ConcurrentQueue<string>();
        host.Portray = (entry, _) =>
        {
            calls.Enqueue(entry.DisplayName);
            return entry.DisplayName == "broken"
                ? Task.FromException<IReadOnlyList<string>?>(new InvalidDataException("bad cell"))
                : Task.FromResult<IReadOnlyList<string>?>(null);
        };
        using var warmer = host.Create(new PortrayalWarmLedger(null));

        warmer.Start();

        // Neither the failed cell nor the inapplicable one is retried; the loaded one is never tried.
        Assert.True(await WaitUntilAsync(() => warmer.Status.State == PortrayalWarmState.Idle));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(["broken", "raster"], calls.Order().ToArray());
    }

    [Fact]
    public async Task ResumesFromTheLedger_AndRedoesCellsWhenTheSignatureChanges()
    {
        using var source = EncDotNet.S100.Core.FileSystemAssetSource.Create(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "a.000"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_dir, "b.000"), [4, 5, 6]);
        var ledgerPath = Path.Combine(_dir, "ledger.json");

        // First session portrays both cells and saves the ledger.
        var host = new Host();
        host.Cells.AddRange([Cell("a", 47.6, -122.4, source), Cell("b", 47.7, -122.4, source)]);
        using (var first = host.Create(new PortrayalWarmLedger(ledgerPath)))
        {
            first.Start();
            Assert.True(await WaitUntilAsync(() => first.Status.State == PortrayalWarmState.Complete));
        }
        Assert.Equal(2, host.Portrayed.Count);
        Assert.True(File.Exists(ledgerPath));

        // A restart with the same signature and cached keys portrays nothing.
        var restarted = new Host();
        restarted.Cached.UnionWith(host.Cached);
        restarted.Cells.AddRange([Cell("a", 47.6, -122.4, source), Cell("b", 47.7, -122.4, source)]);
        using (var second = restarted.Create(new PortrayalWarmLedger(ledgerPath)))
        {
            second.Start();
            Assert.True(await WaitUntilAsync(() => second.Status.State == PortrayalWarmState.Complete));
            Assert.Equal(new PortrayalWarmStatus(PortrayalWarmState.Complete, 2, 2), second.Status);
            Assert.Empty(restarted.Portrayed);

            // A settings change that alters the portrayal sends both back to the queue.
            Volatile.Write(ref restarted.Signature, "sig-2");
            Assert.True(await WaitUntilAsync(() => restarted.Portrayed.Count == 2));
        }
    }

    [Fact]
    public async Task RedoesACell_WhoseCacheEntryWasEvicted()
    {
        using var source = EncDotNet.S100.Core.FileSystemAssetSource.Create(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "a.000"), [1, 2, 3]);
        var ledgerPath = Path.Combine(_dir, "ledger.json");
        var host = new Host();
        host.Cells.Add(Cell("a", 47.6, -122.4, source));
        using (var first = host.Create(new PortrayalWarmLedger(ledgerPath)))
        {
            first.Start();
            Assert.True(await WaitUntilAsync(() => first.Status.State == PortrayalWarmState.Complete));
        }

        // The ledger vouches for the cell, but its cache entry is gone.
        var restarted = new Host();
        restarted.Cells.Add(Cell("a", 47.6, -122.4, source));
        using var second = restarted.Create(new PortrayalWarmLedger(ledgerPath));
        second.Start();

        Assert.True(await WaitUntilAsync(() => restarted.Portrayed.Count == 1));
    }

    [Fact]
    public void CellKey_ChangesWhenTheCellFileChanges_AndIsNullForArchives()
    {
        using var source = EncDotNet.S100.Core.FileSystemAssetSource.Create(_dir);
        var path = Path.Combine(_dir, "a.000");
        File.WriteAllBytes(path, [1, 2, 3]);
        var before = PortrayalWarmLedger.CellKey(Cell("a", 0, 0, source));

        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var after = PortrayalWarmLedger.CellKey(Cell("a", 0, 0, source));

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotEqual(before, after);
        // A cell whose file is missing, or that is not a plain file, is not recorded.
        Assert.Null(PortrayalWarmLedger.CellKey(Cell("missing", 0, 0, source)));
        Assert.Null(PortrayalWarmLedger.CellKey(Cell("a", 0, 0, source: null)));
    }

    [Fact]
    public void Ledger_IgnoresACorruptFile()
    {
        var ledgerPath = Path.Combine(_dir, "ledger.json");
        File.WriteAllText(ledgerPath, "{ not json");

        var ledger = new PortrayalWarmLedger(ledgerPath);

        Assert.Equal(0, ledger.Count);
        Assert.False(ledger.IsWarm("cell", "sig", _ => true));
    }

    [Fact]
    public void StatusLine_ShowsWhileCellsArePending()
    {
        Assert.Equal(string.Empty, PortrayalWarmStatusViewModel.Describe(new(PortrayalWarmState.Idle, 0, 0)));
        Assert.Contains("34", PortrayalWarmStatusViewModel.Describe(new(PortrayalWarmState.Working, 34, 120)), StringComparison.Ordinal);
        Assert.Contains("120", PortrayalWarmStatusViewModel.Describe(new(PortrayalWarmState.Paused, 34, 120)), StringComparison.Ordinal);
    }
}

/// <summary><see cref="ViewportActivity"/>: the map counts as moving for a while after each viewport change.</summary>
public sealed class ViewportActivityTests
{
    private sealed class Notifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current { get; private set; }
        public event EventHandler<MapViewportSnapshot>? ViewportChanged;
        public void Move()
        {
            Current = new MapViewportSnapshot { MinLatitude = 0, MaxLatitude = 1, MinLongitude = 0, MaxLongitude = 1 };
            ViewportChanged?.Invoke(this, Current);
        }
    }

    [Fact]
    public void MovedWithin_FollowsTheLastViewportChange()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var notifier = new Notifier();
        using var activity = new ViewportActivity(notifier, time);
        var window = TimeSpan.FromMilliseconds(750);

        Assert.False(activity.MovedWithin(window));

        notifier.Move();
        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(activity.MovedWithin(window));

        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.False(activity.MovedWithin(window));
    }
}
