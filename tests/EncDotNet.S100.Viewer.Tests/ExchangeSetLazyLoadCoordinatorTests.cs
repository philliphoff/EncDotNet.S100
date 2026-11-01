using System.Collections.Concurrent;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Unit tests for <see cref="ExchangeSetLazyLoadCoordinator"/> — the stateful
/// glue that turns viewport changes into gated cell loads and LRU evictions
/// (issue #458). Debounce is disabled so <c>Evaluate</c> runs synchronously.
/// </summary>
public sealed class ExchangeSetLazyLoadCoordinatorTests
{
    private static DatasetEntry Cell(string name, double south, double west, double north, double east)
        => new(
            filePath: name + ".000",
            productSpec: "S-57",
            source: null,
            relativePath: name + ".000",
            displayName: name,
            geographicBounds: new BoundingBox
            {
                WestBoundLongitude = west,
                EastBoundLongitude = east,
                SouthBoundLatitude = south,
                NorthBoundLatitude = north,
            });

    // A viewport over the US east coast, zoomed in tight (small mercator
    // resolution → large scale, so all usage bands are eligible).
    private static MapViewportSnapshot Viewport(
        double south, double west, double north, double east, double resolution = 5.0)
        => new()
        {
            MinLatitude = south,
            MinLongitude = west,
            MaxLatitude = north,
            MaxLongitude = east,
            MercatorResolution = resolution,
        };

    private sealed class FakeNotifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current { get; private set; }
        public event EventHandler<MapViewportSnapshot>? ViewportChanged;
        public void Publish(MapViewportSnapshot snapshot)
        {
            Current = snapshot;
            ViewportChanged?.Invoke(this, snapshot);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    private static ExchangeSetLazyLoadCoordinator Create(
        FakeNotifier notifier,
        ConcurrentBag<DatasetEntry> loaded,
        ConcurrentBag<DatasetEntry> unloaded,
        LazyLoadOptions? options = null)
        => new(
            notifier,
            (entry, _) => { loaded.Add(entry); return Task.CompletedTask; },
            entry => unloaded.Add(entry),
            options ?? new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero });

    [Fact]
    public async Task InViewCell_IsLoaded()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        var unloaded = new ConcurrentBag<DatasetEntry>();
        using var coordinator = Create(notifier, loaded, unloaded);

        var cell = Cell("US5A", 40, -75, 41, -74);
        coordinator.Register(new[] { cell });
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => loaded.Contains(cell)));
        Assert.False(cell.IsDeferred);
    }

    [Fact]
    public async Task OutOfViewCell_IsNotLoaded()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        var unloaded = new ConcurrentBag<DatasetEntry>();
        using var coordinator = Create(notifier, loaded, unloaded);

        var farCell = Cell("US5B", 0, 10, 1, 11); // off the coast of Africa
        coordinator.Register(new[] { farCell });
        notifier.Publish(Viewport(40, -75, 41, -74));

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(farCell, loaded);
        Assert.True(farCell.IsDeferred);
    }

    [Fact]
    public async Task Register_MarksEntriesDeferred()
    {
        var notifier = new FakeNotifier();
        var coordinator = Create(notifier, new(), new());

        var cell = Cell("US5C", 40, -75, 41, -74);
        coordinator.Register(new[] { cell });

        Assert.True(cell.IsDeferred);
        Assert.Equal(1, coordinator.DeferredCount);
        coordinator.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ZoomedOut_LargeScaleBandCells_NotLoaded()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        using var coordinator = Create(notifier, loaded, new());

        // Band 5 (Harbour) cell — only relevant at large scale. A very coarse
        // resolution (zoomed way out) should keep it deferred even though it
        // intersects the viewport.
        var harbourCell = Cell("US5HARBOR", 40, -75, 41, -74);
        coordinator.Register(new[] { harbourCell });
        notifier.Publish(Viewport(30, -85, 50, -65, resolution: 3000.0));

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(harbourCell, loaded);
    }

    [Fact]
    public async Task RetentionBudget_EvictsOffScreenColdCells()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        var unloaded = new ConcurrentBag<DatasetEntry>();
        using var coordinator = Create(notifier, loaded, unloaded,
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, RetentionBudget = 1 });

        var a = Cell("US5AA", 40, -75, 41, -74);
        var b = Cell("US5BB", 40, -74, 41, -73);
        coordinator.Register(new[] { a, b });

        // Frame both cells so both load.
        notifier.Publish(Viewport(40, -75, 41, -73));
        Assert.True(await WaitUntilAsync(() => coordinator.LoadedCount == 2));

        // Pan away entirely; both are now off-screen and over the budget of 1,
        // so eviction unloads the coldest.
        notifier.Publish(Viewport(0, 10, 1, 11));

        Assert.True(await WaitUntilAsync(() => unloaded.Count >= 1));
    }

    [Fact]
    public async Task LoadCompletingAfterUnregister_IsUnwound()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        var unloaded = new ConcurrentBag<DatasetEntry>();

        // A load delegate that blocks until released, so we can Unregister the
        // cell mid-flight and prove the finished load is unwound rather than
        // marked loaded (no zombie layers). See issue #458.
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            async (entry, _) =>
            {
                started.TrySetResult();
                await release.Task;
                loaded.Add(entry);
            },
            entry => unloaded.Add(entry),
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero });

        var cell = Cell("US5ZZ", 40, -75, 41, -74);
        coordinator.Register(new[] { cell });
        notifier.Publish(Viewport(40, -75, 41, -74));

        // Wait until the load is in-flight, then close the exchange set.
        Assert.True(await WaitUntilAsync(() => started.Task.IsCompleted));
        coordinator.Unregister(new[] { cell });

        // Let the (now-stale) load finish.
        release.SetResult();

        Assert.True(await WaitUntilAsync(() => unloaded.Contains(cell)));
        Assert.Equal(0, coordinator.LoadedCount);
    }

    [Fact]
    public async Task DisposeDuringInFlightLoad_DoesNotThrow()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        var unloaded = new ConcurrentBag<DatasetEntry>();

        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            async (entry, _) =>
            {
                started.TrySetResult();
                await release.Task;
                loaded.Add(entry);
            },
            entry => unloaded.Add(entry),
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero });

        var cell = Cell("US5DD", 40, -75, 41, -74);
        coordinator.Register(new[] { cell });
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => started.Task.IsCompleted));

        // Dispose (which disposes the internal semaphore) while the load is
        // in-flight, then let it finish; the fire-and-forget pump must not
        // surface an unobserved ObjectDisposedException on WaitAsync/Release.
        coordinator.Dispose();
        release.SetResult();

        // Give the pump time to run its finally block, then flush finalizers to
        // surface any unobserved task exception.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // A disposed coordinator that swallowed the teardown race must not have
        // re-marked the closed cell loaded.
        Assert.Equal(0, coordinator.LoadedCount);
    }

    [Fact]
    public async Task UnregisterLoadedCell_IsNotReTouchedByLaterEvaluate()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        var unloaded = new ConcurrentBag<DatasetEntry>();
        using var coordinator = Create(notifier, loaded, unloaded);

        var cell = Cell("US5EE", 40, -75, 41, -74);
        coordinator.Register(new[] { cell });

        // Frame it so it loads and enters the LRU mirror.
        notifier.Publish(Viewport(40, -75, 41, -74));
        Assert.True(await WaitUntilAsync(() => coordinator.LoadedCount == 1));

        // Close the exchange set: the entry must be forgotten completely.
        coordinator.Unregister(new[] { cell });
        Assert.Equal(0, coordinator.LoadedCount);

        // A later viewport tick that still intersects the (now-unregistered)
        // footprint must not resurrect it into the LRU.
        notifier.Publish(Viewport(40, -75, 41, -74));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(0, coordinator.LoadedCount);
    }

    [Fact]
    public async Task DebouncedRapidViewportChanges_EvaluateOnlyLatestSnapshot()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        var unloaded = new ConcurrentBag<DatasetEntry>();
        using var coordinator = Create(notifier, loaded, unloaded,
            new LazyLoadOptions { ViewportDebounce = TimeSpan.FromMilliseconds(60) });

        var cell = Cell("US5FF", 40, -75, 41, -74);
        coordinator.Register(new[] { cell });

        // Rapidly publish several viewports away from the cell, then finally
        // one that frames it. On the debounced path only the latest snapshot
        // must drive Evaluate(), so the cell loads exactly once and the
        // superseded snapshots never fire.
        notifier.Publish(Viewport(0, 10, 1, 11));
        notifier.Publish(Viewport(0, 20, 1, 21));
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => loaded.Contains(cell)));
        Assert.Equal(1, coordinator.LoadedCount);
        Assert.False(cell.IsDeferred);

        // Give any stale continuation the chance to (incorrectly) re-run.
        await Task.Delay(80, TestContext.Current.CancellationToken);
        Assert.Single(loaded);
    }

    [Fact]
    public async Task Register_AlreadyLoadedCell_IsNotReMarkedDeferred()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        var unloaded = new ConcurrentBag<DatasetEntry>();
        using var coordinator = Create(notifier, loaded, unloaded);

        var cell = Cell("US5HH", 40, -75, 41, -74);
        coordinator.Register(new[] { cell });
        notifier.Publish(Viewport(40, -75, 41, -74));
        Assert.True(await WaitUntilAsync(() => coordinator.LoadedCount == 1));
        Assert.False(cell.IsDeferred);

        // Re-registering the same (now-loaded) cell must be idempotent: it must
        // not re-mark the cell deferred (which would dim it and show a deferred
        // outline) or disturb its loaded state. See issue #458.
        coordinator.Register(new[] { cell });
        Assert.False(cell.IsDeferred);
        Assert.Equal(1, coordinator.LoadedCount);
    }

    [Fact]
    public async Task QueuedLoad_AbandonedWhenUnregisteredBeforeGate()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        var unloaded = new ConcurrentBag<DatasetEntry>();

        var firstStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var gate = new object();
        DatasetEntry? blocker = null;

        // MaxConcurrency = 1: the first cell to acquire the gate blocks it while
        // the second waits queued behind it. See issue #458.
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            async (entry, _) =>
            {
                bool isBlocker;
                lock (gate)
                {
                    isBlocker = blocker is null;
                    if (isBlocker) blocker = entry;
                }
                if (isBlocker)
                {
                    firstStarted.TrySetResult();
                    await release.Task;
                }
                loaded.Add(entry);
            },
            entry => unloaded.Add(entry),
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, MaxConcurrency = 1 });

        var cellA = Cell("US5GA", 40, -75, 41, -74);
        var cellB = Cell("US5GB", 40, -75, 41, -74);
        coordinator.Register(new[] { cellA, cellB });
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => firstStarted.Task.IsCompleted));

        // Close the cell still queued on the gate (whichever did not win it).
        var queued = ReferenceEquals(blocker, cellA) ? cellB : cellA;
        coordinator.Unregister(new[] { queued });

        // Release the blocker; the queued load now acquires the gate and must
        // abandon before running the expensive _loadAsync.
        release.SetResult();

        Assert.True(await WaitUntilAsync(() => loaded.Contains(blocker!)));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // The queued cell's load never ran (not added to loaded), and since no
        // layers were built it is not unwound either.
        Assert.DoesNotContain(queued, loaded);
        Assert.DoesNotContain(queued, unloaded);
    }

    [Fact]
    public async Task DebouncedEvaluation_RunsThroughMarshal()
    {
        // Mapsui raises viewport changes from animation callbacks that have no
        // synchronisation context; the debounced pass must still reach the UI
        // thread through the injected marshal, not run on the thread pool.
        var notifier = new FakeNotifier();
        var marshalled = 0;
        var loadedInsideMarshal = new ConcurrentBag<bool>();
        var inMarshal = new ThreadLocal<bool>();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            (entry, _) =>
            {
                loadedInsideMarshal.Add(inMarshal.Value);
                return Task.CompletedTask;
            },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.FromMilliseconds(10) },
            marshal: action =>
            {
                Interlocked.Increment(ref marshalled);
                inMarshal.Value = true;
                try { action(); }
                finally { inMarshal.Value = false; }
            });

        coordinator.Register(new[] { Cell("US5MA", 40, -75, 41, -74) });
        await Task.Run(() => notifier.Publish(Viewport(40, -75, 41, -74)), TestContext.Current.CancellationToken);

        Assert.True(await WaitUntilAsync(() => coordinator.LoadedCount == 1));
        Assert.True(marshalled >= 1);
        Assert.Equal(new[] { true }, loadedInsideMarshal.ToArray());
    }

    [Fact]
    public async Task QueuedLoad_NoLongerInView_IsSkippedAndStaysDeferred()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        var release = new TaskCompletionSource();
        var blocker = Cell("US5AA", 40, -75, 41, -74);
        var far = Cell("US5BB", 50, -5, 51, -4);
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            async (entry, _) =>
            {
                if (ReferenceEquals(entry, blocker))
                    await release.Task;
                loaded.Add(entry);
            },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, MaxConcurrency = 1 });

        coordinator.Register(new[] { blocker, far });

        // A wide view wants both; the far cell queues behind the blocker.
        notifier.Publish(Viewport(39, -76, 52, -3));
        // The view moves to the blocker only before the far cell's turn comes.
        notifier.Publish(Viewport(40, -75, 41, -74));
        release.SetResult();

        Assert.True(await WaitUntilAsync(() => loaded.Contains(blocker)));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(far, loaded);
        Assert.True(far.IsDeferred);

        // Panning back to it loads it.
        notifier.Publish(Viewport(50, -5, 51, -4));
        Assert.True(await WaitUntilAsync(() => loaded.Contains(far)));
    }

    private static DatasetEntry UnboundedCell(string name)
        => new(
            filePath: name + ".h5",
            productSpec: "S-102",
            source: null,
            relativePath: name + ".h5",
            displayName: name,
            geographicBounds: null);

    [Fact]
    public async Task VisibleCellsLoadNearestTheCentreFirst()
    {
        var notifier = new FakeNotifier();
        var order = new ConcurrentQueue<DatasetEntry>();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            (entry, _) => { order.Enqueue(entry); return Task.CompletedTask; },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, MaxConcurrency = 1 });

        var corner = Cell("US5CORNER", 40.0, -75.0, 40.1, -74.9);
        var middle = Cell("US5MIDDLE", 40.4, -74.6, 40.6, -74.4);
        coordinator.Register(new[] { corner, middle });
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => order.Count == 2));
        Assert.Equal(new[] { middle, corner }, order.ToArray());
    }

    [Fact]
    public async Task VisibleCellLoadsBeforeAheadAndUnboundedCells()
    {
        var notifier = new FakeNotifier();
        var order = new ConcurrentQueue<DatasetEntry>();
        var release = new TaskCompletionSource();
        var blocker = Cell("US5BLOCK", 40.4, -74.6, 40.6, -74.4);
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            async (entry, _) =>
            {
                if (ReferenceEquals(entry, blocker))
                    await release.Task;
                order.Enqueue(entry);
            },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, MaxConcurrency = 1 });

        // The blocker takes the only slot; the rest queue behind it.
        coordinator.Register(new[] { blocker });
        notifier.Publish(Viewport(40, -75, 41, -74));

        var unbounded = UnboundedCell("102XX00UNBOUND");
        var ahead = Cell("US5AHEAD", 40.2, -73.8, 40.4, -73.6);
        var visible = Cell("US5VISIBLE", 40.0, -75.0, 40.1, -74.9);
        coordinator.Register(new[] { unbounded, ahead, visible });
        release.SetResult();

        Assert.True(await WaitUntilAsync(() => order.Count == 4));
        Assert.Equal(new[] { blocker, visible }, order.Take(2).ToArray());
        Assert.Equal(unbounded, order.Last());
    }

    [Fact]
    public async Task SpeculativeLoadsRunOneAtATime()
    {
        var notifier = new FakeNotifier();
        var running = 0;
        var maxRunning = 0;
        var done = new ConcurrentBag<DatasetEntry>();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            async (entry, _) =>
            {
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref maxRunning, now);
                await Task.Delay(30);
                Interlocked.Decrement(ref running);
                done.Add(entry);
            },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, MaxConcurrency = 4 });

        coordinator.Register(new[]
        {
            Cell("US5E1", 40.2, -73.8, 40.4, -73.6),
            Cell("US5E2", 40.5, -73.8, 40.7, -73.6),
            Cell("US5E3", 40.2, -75.4, 40.4, -75.2),
        });
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => done.Count == 3));
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task SpeculativeLoadWaitsWhileTilesAreStillDrawing()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        var tilesBusy = true;
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            (entry, _) => { loaded.Add(entry); return Task.CompletedTask; },
            _ => { },
            new LazyLoadOptions
            {
                ViewportDebounce = TimeSpan.Zero,
                SpeculativeRetryDelay = TimeSpan.FromMilliseconds(20),
            },
            hasVisibleTileWork: () => Volatile.Read(ref tilesBusy));

        var visible = Cell("US5VIS", 40.4, -74.6, 40.6, -74.4);
        var ahead = Cell("US5AHD", 40.2, -73.8, 40.4, -73.6);
        coordinator.Register(new[] { visible, ahead });
        notifier.Publish(Viewport(40, -75, 41, -74));

        // Visible work never waits for the renderer; speculative work does.
        Assert.True(await WaitUntilAsync(() => loaded.Contains(visible)));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(ahead, loaded);

        Volatile.Write(ref tilesBusy, false);
        Assert.True(await WaitUntilAsync(() => loaded.Contains(ahead)));
    }

    [Fact]
    public async Task RunningLoadIsCancelledWhenItsCellLeavesTheAheadWindow()
    {
        var notifier = new FakeNotifier();
        var started = new TaskCompletionSource();
        var cancelled = new TaskCompletionSource();
        var attempts = 0;
        var cell = Cell("US5GONE", 40.4, -74.6, 40.6, -74.4);
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            async (entry, ct) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    started.TrySetResult();
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
                }
            },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero });

        coordinator.Register(new[] { cell });
        notifier.Publish(Viewport(40, -75, 41, -74));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // A small pan keeps the cell within the ahead margin: still loading.
        notifier.Publish(Viewport(40, -74.2, 41, -73.2));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(cancelled.Task.IsCompleted);

        // Panning well away cancels it; the cell stays deferred.
        notifier.Publish(Viewport(10, 10, 11, 11));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.True(await WaitUntilAsync(() => coordinator.LoadedCount == 0 && cell.IsDeferred));

        // Panning back loads it again.
        notifier.Publish(Viewport(40, -75, 41, -74));
        Assert.True(await WaitUntilAsync(() => coordinator.LoadedCount == 1));
        Assert.Equal(2, attempts);
        Assert.False(cell.IsDeferred);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value
            && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
