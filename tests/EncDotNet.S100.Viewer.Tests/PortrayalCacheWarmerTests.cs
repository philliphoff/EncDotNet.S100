using System.Collections.Concurrent;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="PortrayalCacheWarmer"/> (#1266, map rendering slice 3): portrays
/// registered cells while the map is idle, several at once and nearest the view
/// first; gives way when the map gets busy; stops short of the cache cap; and
/// resumes from its ledger.
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
        public int Concurrency = 1;
        public int? InitialConcurrency;
        public Func<TimeSpan> GcPause = () => TimeSpan.Zero;
        public TimeSpan ConcurrencyWindow = TimeSpan.FromSeconds(30);

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
            concurrency: new PrewarmConcurrency(
                new PrewarmConcurrencyOptions
                {
                    MaxConcurrency = Concurrency,
                    InitialConcurrency = InitialConcurrency ?? Concurrency,
                    Window = ConcurrencyWindow,
                    BackOffPauseShare = 0.10,
                    GrowPauseShare = 0.04,
                },
                () => GcPause(),
                TimeProvider.System),
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
    public async Task PortraysUpToTheLimitAtOnce_NearestTheViewFirst()
    {
        var host = new Host { View = ViewAt(47.6, -122.4), Concurrency = 3 };
        for (var i = 0; i < 6; i++)
            host.Cells.Add(Cell("c" + i, 47.6 + (i * 0.5), -122.4));
        var running = 0;
        var peak = 0;
        var started = new ConcurrentQueue<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Portray = async (entry, ct) =>
        {
            started.Enqueue(entry.DisplayName);
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            try { await release.Task.WaitAsync(ct); }
            finally { Interlocked.Decrement(ref running); }
            return await host.Default(entry);
        };
        using var warmer = host.Create(new PortrayalWarmLedger(null));

        warmer.Start();

        // Three start together, nearest first, and no more while they run.
        Assert.True(await WaitUntilAsync(() => started.Count == 3));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(["c0", "c1", "c2"], started.ToArray());
        Assert.Equal(new PortrayalWarmStatus(PortrayalWarmState.Working, 0, 6), warmer.Status);

        release.SetResult();
        Assert.True(await WaitUntilAsync(() => warmer.Status.State == PortrayalWarmState.Complete));
        Assert.Equal(new PortrayalWarmStatus(PortrayalWarmState.Complete, 6, 6), warmer.Status);
        Assert.Equal(3, Volatile.Read(ref peak));
        Assert.Equal(6, host.Portrayed.Count);
    }

    [Fact]
    public async Task GivesWayWithEveryRunningCell_WhenTheMapGetsBusy()
    {
        var host = new Host { Concurrency = 4 };
        for (var i = 0; i < 4; i++)
            host.Cells.Add(Cell("c" + i, 47.6 + i, -122.4));
        var attempts = 0;
        var cancelled = 0;
        host.Portray = async (entry, ct) =>
        {
            if (Interlocked.Increment(ref attempts) <= 4)
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
            }
            return await host.Default(entry);
        };
        using var warmer = host.Create(new PortrayalWarmLedger(null));

        warmer.Start();
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref attempts) == 4));
        var busyAt = System.Diagnostics.Stopwatch.StartNew();
        host.Busy = true;

        // All four are cancelled within a few poll intervals, and none restart while busy.
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref cancelled) == 4));
        Assert.True(busyAt.ElapsedMilliseconds < 2000, $"gave way after {busyAt.ElapsedMilliseconds} ms");
        Assert.True(await WaitUntilAsync(() => warmer.Status.State == PortrayalWarmState.Paused));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(4, Volatile.Read(ref attempts));
        Assert.Empty(host.Portrayed);

        host.Busy = false;
        Assert.True(await WaitUntilAsync(() => warmer.Status.State == PortrayalWarmState.Complete));
        Assert.Equal(8, Volatile.Read(ref attempts));
        Assert.Equal(4, host.Portrayed.Count);
    }

    [Fact]
    public async Task RunsMoreCellsAtOnce_WhileGcPausesStayLow_EvenWhenCellsFinishQuickly()
    {
        var host = new Host
        {
            Concurrency = 4,
            InitialConcurrency = 1,
            ConcurrencyWindow = TimeSpan.FromMilliseconds(40),
        };
        for (var i = 0; i < 400; i++)
            host.Cells.Add(Cell("c" + i, 47.6 + (i * 0.001), -122.4));
        var running = 0;
        var peak = 0;
        host.Portray = async (entry, ct) =>
        {
            InterlockedMax(ref peak, Interlocked.Increment(ref running));
            try { await Task.Delay(10, ct); }
            finally { Interlocked.Decrement(ref running); }
            return await host.Default(entry);
        };
        using var warmer = host.Create(new PortrayalWarmLedger(null));

        warmer.Start();

        // Slots refill as cells finish, so every window counts as saturated.
        Assert.True(await WaitUntilAsync(() => warmer.ConcurrencyLimit == 4));
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref peak) == 4));
        Assert.True(Volatile.Read(ref peak) <= 4);
    }

    [Fact]
    public async Task RunsFewerCellsAtOnce_WhenGcPausesClimb()
    {
        // GC pauses take half of wall time from the start: the limit halves each window.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var host = new Host
        {
            Concurrency = 8,
            ConcurrencyWindow = TimeSpan.FromMilliseconds(30),
            GcPause = () => clock.Elapsed / 2,
        };
        for (var i = 0; i < 40; i++)
            host.Cells.Add(Cell("c" + i, 47.6 + (i * 0.01), -122.4));
        var running = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Portray = async (entry, ct) =>
        {
            Interlocked.Increment(ref running);
            try { await gate.Task.WaitAsync(ct); }
            finally { Interlocked.Decrement(ref running); }
            return await host.Default(entry);
        };
        using var warmer = host.Create(new PortrayalWarmLedger(null));

        warmer.Start();
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref running) == 8));
        Assert.True(await WaitUntilAsync(() => warmer.ConcurrencyLimit == 1));

        // Running cells finish; only one at a time replaces them.
        gate.SetResult();
        Assert.True(await WaitUntilAsync(() => host.Portrayed.Count >= 12));
        Assert.True(Volatile.Read(ref running) <= 1);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current)
                return;
            current = seen;
        }
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

/// <summary>
/// <see cref="PrewarmConcurrency"/> (map rendering slice 3): additive increase
/// while GC pauses stay low and every slot is busy, halving when they climb.
/// </summary>
public sealed class PrewarmConcurrencyTests
{
    private static readonly PrewarmConcurrencyOptions Options = new()
    {
        MaxConcurrency = 6,
        InitialConcurrency = 2,
        Window = TimeSpan.FromSeconds(1),
        BackOffPauseShare = 0.10,
        GrowPauseShare = 0.04,
    };

    private sealed class Rig
    {
        public Microsoft.Extensions.Time.Testing.FakeTimeProvider Time { get; } = new();
        public TimeSpan Pause;
        public PrewarmConcurrency Create(PrewarmConcurrencyOptions? options = null) =>
            new(options ?? Options, () => Pause, Time);

        /// <summary>Advances one window with <paramref name="share"/> of it spent in GC pauses.</summary>
        public bool Window(PrewarmConcurrency c, double share, int running)
        {
            Time.Advance(TimeSpan.FromSeconds(1));
            Pause += TimeSpan.FromSeconds(share);
            return c.Observe(running);
        }
    }

    [Fact]
    public void GrowsByOne_PerQuietSaturatedWindow_UpToTheMaximum()
    {
        var rig = new Rig();
        var c = rig.Create();
        Assert.Equal(2, c.Limit);
        c.Observe(running: 2);

        for (var expected = 3; expected <= 6; expected++)
        {
            Assert.True(rig.Window(c, 0.01, running: c.Limit));
            Assert.Equal(expected, c.Limit);
        }

        Assert.False(rig.Window(c, 0.0, running: 6));
        Assert.Equal(6, c.Limit);
        Assert.Equal(0.0, c.LastPauseShare);
    }

    [Fact]
    public void Holds_WhenSlotsWereFree_OrThePauseShareIsBetweenTheThresholds()
    {
        var rig = new Rig();
        var c = rig.Create();
        c.Observe(running: 1);

        Assert.False(rig.Window(c, 0.01, running: 1));
        Assert.Equal(2, c.Limit);

        c.Observe(running: 2);
        Assert.False(rig.Window(c, 0.07, running: 2));
        Assert.Equal(2, c.Limit);
        Assert.Equal(0.07, c.LastPauseShare!.Value, 6);
    }

    [Fact]
    public void Halves_WhenThePauseShareClimbs_DownToOne()
    {
        var rig = new Rig();
        var c = rig.Create(Options with { InitialConcurrency = 6 });
        c.Observe(running: 6);

        Assert.True(rig.Window(c, 0.30, running: 6));
        Assert.Equal(3, c.Limit);
        Assert.True(rig.Window(c, 0.30, running: 3));
        Assert.Equal(1, c.Limit);
        Assert.False(rig.Window(c, 0.30, running: 1));
        Assert.Equal(1, c.Limit);
    }

    [Fact]
    public void Restart_DropsTheOpenWindow_SoPausesWhileTheMapWorkedDontCount()
    {
        var rig = new Rig();
        var c = rig.Create();
        c.Observe(running: 2);

        // The map loads and pauses heavily; the warmer is paused meanwhile.
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        rig.Pause += TimeSpan.FromSeconds(3);
        c.Restart();

        Assert.False(c.Observe(running: 2));
        Assert.True(rig.Window(c, 0.0, running: 2));
        Assert.Equal(3, c.Limit);
    }

    [Theory]
    [InlineData(16, null, 14, 7)]
    [InlineData(4, null, 2, 1)]
    [InlineData(1, null, 1, 1)]
    [InlineData(16, "1", 1, 1)]
    [InlineData(16, "5", 5, 5)]
    [InlineData(16, "0", 14, 7)]
    [InlineData(16, "lots", 14, 7)]
    public void Resolve_BoundsByCores_AndHonoursAFixedLimit(int processors, string? variable, int max, int initial)
    {
        var options = PrewarmConcurrencyOptions.Resolve(processors, variable);

        Assert.Equal((max, initial), (options.MaxConcurrency, options.InitialConcurrency));
    }

    [Fact]
    public void AFixedLimit_DoesNotBackOff()
    {
        var rig = new Rig();
        var c = rig.Create(PrewarmConcurrencyOptions.Resolve(16, "8") with { Window = TimeSpan.FromSeconds(1) });
        c.Observe(running: 8);

        Assert.False(rig.Window(c, 1.0, running: 8));
        Assert.Equal(8, c.Limit);
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
