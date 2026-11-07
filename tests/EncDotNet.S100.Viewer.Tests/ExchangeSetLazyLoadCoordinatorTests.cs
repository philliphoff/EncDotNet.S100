using System.Collections.Concurrent;
using EncDotNet.S100.Core.Scheduling;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Engine.Prediction;
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

    [Theory]
    [InlineData((int)LoadTier.Visible, WorkClass.Visible)]
    [InlineData((int)LoadTier.Ahead, WorkClass.Predicted)]
    [InlineData((int)LoadTier.Covered, WorkClass.Background)]
    [InlineData((int)LoadTier.Unbounded, WorkClass.Background)]
    public void PriorityFor_MapsTiersToWorkClassesWithUrgencyInPixels(int tier, WorkClass expected)
    {
        // 0.01° at 10 m/px is about 111 px.
        var priority = ExchangeSetLazyLoadCoordinator.PriorityFor((LoadTier)tier, 0.01, 10);

        Assert.Equal(expected, priority.Class);
        Assert.Equal(111.32, priority.Urgency, 2);
    }

    [Fact]
    public async Task LoadRunsUnderItsDemand_WhichFollowsTheCellIntoView()
    {
        var notifier = new FakeNotifier();
        WorkDemand? seen = null;
        var release = new TaskCompletionSource();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            async (entry, _) =>
            {
                seen = WorkDemand.Current;
                await release.Task;
                entry.IsLoaded = true;
            },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero });

        var ahead = Cell("US5AHD", 40.2, -73.8, 40.4, -73.6);
        coordinator.Register(new[] { ahead });
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => seen is not null));
        Assert.Equal(WorkClass.Predicted, seen!.Priority.Class);
        Assert.Null(WorkDemand.Current);

        // The view reaches the cell while it loads: its work becomes visible.
        notifier.Publish(Viewport(40, -74, 41, -73));
        Assert.Equal(WorkClass.Visible, seen.Priority.Class);
        release.SetResult();
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

    [Fact]
    public async Task AnEvictionPassUnloadsItsCellsInOneCall()
    {
        var notifier = new FakeNotifier();
        var batches = new ConcurrentQueue<DatasetEntry[]>();
        var single = new ConcurrentBag<DatasetEntry>();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            (_, _) => Task.CompletedTask,
            single.Add,
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, RetentionBudget = 0 },
            unloadMany: entries => batches.Enqueue(entries.ToArray()));

        var cells = new[]
        {
            Cell("US5EV1", 40.1, -74.9, 40.2, -74.8),
            Cell("US5EV2", 40.4, -74.6, 40.5, -74.5),
            Cell("US5EV3", 40.7, -74.3, 40.8, -74.2),
        };
        coordinator.Register(cells);
        notifier.Publish(Viewport(40, -75, 41, -74));
        Assert.True(await WaitUntilAsync(() => coordinator.LoadedCount == 3));

        // Panning far away evicts all three in one pass.
        notifier.Publish(Viewport(10, 10, 11, 11));

        var batch = Assert.Single(batches);
        Assert.Equal(cells.OrderBy(c => c.DisplayName), batch.OrderBy(c => c.DisplayName));
        Assert.Empty(single);
        Assert.Equal(0, coordinator.LoadedCount);
    }

    // A cell with catalogue coverage (its whole bounding box) and a
    // compilation scale, as the NOAA product catalogue supplies (#1265).
    private static DatasetEntry RankedCell(
        string name, int compilationScale, double south, double west, double north, double east, bool withCoverage = true)
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
            },
            compilationScale: compilationScale,
            catalogueCoverage: withCoverage
                ? CoveredCellGate.FromCatalogueCoverage(new Collections.GeoCoverage(
                [
                    new Collections.GeoPolygon(
                    [
                        new DataModel.GeoPosition(south, west),
                        new DataModel.GeoPosition(south, east),
                        new DataModel.GeoPosition(north, east),
                        new DataModel.GeoPosition(north, west),
                    ]),
                ]))
                : null);

    // Two finer cells that cover the 40..41 N, 75..74 W view between them, and
    // a coarse cell under them that reaches well beyond it.
    private static (DatasetEntry West, DatasetEntry East, DatasetEntry Coarse) CoveredView(bool finerCoverage = true) =>
        (RankedCell("US5WEST", 20_000, 39.5, -75.5, 41.5, -74.5, finerCoverage),
         RankedCell("US5EAST", 20_000, 39.5, -74.5, 41.5, -73.5, finerCoverage),
         RankedCell("US3COARSE", 80_000, 35, -80, 45, -70));

    private static ExchangeSetLazyLoadCoordinator CreateLoading(
        FakeNotifier notifier,
        ConcurrentQueue<DatasetEntry> order,
        Func<bool> tilesBusy,
        Action<DatasetEntry>? onLoaded = null,
        LazyLoadOptions? options = null)
        => new(
            notifier,
            (entry, _) =>
            {
                entry.IsLoaded = true;
                onLoaded?.Invoke(entry);
                order.Enqueue(entry);
                return Task.CompletedTask;
            },
            _ => { },
            options ?? new LazyLoadOptions
            {
                ViewportDebounce = TimeSpan.Zero,
                SpeculativeRetryDelay = TimeSpan.FromMilliseconds(20),
            },
            hasVisibleTileWork: tilesBusy);

    [Fact]
    public async Task CoarseCellCoveredByFinerCells_WaitsForThem()
    {
        var notifier = new FakeNotifier();
        var order = new ConcurrentQueue<DatasetEntry>();
        var tilesBusy = true;
        using var coordinator = CreateLoading(notifier, order, () => Volatile.Read(ref tilesBusy));

        var (west, east, coarse) = CoveredView();
        coordinator.Register(new[] { coarse, west, east });
        notifier.Publish(Viewport(40, -75, 41, -74));

        // The finer cells load at once; the covered coarse cell waits like
        // speculative work while their tiles draw.
        Assert.True(await WaitUntilAsync(() => order.Count == 2));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(coarse, order);
        Assert.True(coarse.IsDeferred);

        // Deferred, not skipped: it loads once the renderer is idle.
        Volatile.Write(ref tilesBusy, false);
        Assert.True(await WaitUntilAsync(() => order.Contains(coarse)));
        Assert.Equal(coarse, order.Last());
    }

    [Fact]
    public async Task CoarseCellWaitsWhileFinerCellsAreStillLoading()
    {
        var notifier = new FakeNotifier();
        var order = new ConcurrentQueue<DatasetEntry>();
        var release = new TaskCompletionSource();
        var (west, east, coarse) = CoveredView();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            async (entry, _) =>
            {
                if (!ReferenceEquals(entry, coarse))
                    await release.Task;
                entry.IsLoaded = true;
                order.Enqueue(entry);
            },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, MaxConcurrency = 4 });

        coordinator.Register(new[] { coarse, west, east });
        notifier.Publish(Viewport(40, -75, 41, -74));

        // Free slots and an idle renderer, but the finer cells are still loading.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(order);

        release.SetResult();
        Assert.True(await WaitUntilAsync(() => order.Count == 3));
        Assert.Equal(coarse, order.Last());
    }

    [Fact]
    public async Task CoarseCellWithAnUncoveredPartOfTheView_LoadsAsVisible()
    {
        var notifier = new FakeNotifier();
        var order = new ConcurrentQueue<DatasetEntry>();
        using var coordinator = CreateLoading(notifier, order, () => true);

        // Only the west half of the view has a finer cell.
        var (west, _, coarse) = CoveredView();
        coordinator.Register(new[] { coarse, west });
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => order.Count == 2));
        Assert.Contains(coarse, order);
    }

    [Fact]
    public async Task FinerCellsKnownOnlyByBoundingBox_NeverCover()
    {
        var notifier = new FakeNotifier();
        var order = new ConcurrentQueue<DatasetEntry>();
        using var coordinator = CreateLoading(notifier, order, () => true);

        var (west, east, coarse) = CoveredView(finerCoverage: false);
        coordinator.Register(new[] { coarse, west, east });
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => order.Count == 3));
    }

    [Fact]
    public async Task FinerCellThatDoesNotDrawAtThisScale_PromotesTheCoarseCell()
    {
        var notifier = new FakeNotifier();
        var order = new ConcurrentQueue<DatasetEntry>();
        var (west, east, coarse) = CoveredView();

        // Once loaded, the east cell turns out to stop drawing well before this
        // zoom level (its display window ends at 1 m/px; the view is at 5).
        using var coordinator = CreateLoading(
            notifier,
            order,
            () => true,
            onLoaded: entry =>
            {
                if (ReferenceEquals(entry, east))
                    entry.ContentMaxVisibleResolution = 1.0;
            });

        coordinator.Register(new[] { coarse, west, east });
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => order.Count == 3));
        Assert.Equal(coarse, order.Last());
    }

    [Fact]
    public async Task HiddenFinerCell_DoesNotCover()
    {
        var notifier = new FakeNotifier();
        var order = new ConcurrentQueue<DatasetEntry>();
        var (west, east, coarse) = CoveredView();
        using var coordinator = CreateLoading(notifier, order, () => true);

        // The user has switched the west cell off, so only the east half of the
        // view has finer chart on it.
        west.IsVisible = false;
        coordinator.Register(new[] { coarse, west, east });
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => order.Count == 3));
    }

    [Fact]
    public async Task DeferCoveredCellsOff_LoadsCoarseCellAsVisible()
    {
        var notifier = new FakeNotifier();
        var order = new ConcurrentQueue<DatasetEntry>();
        using var coordinator = CreateLoading(
            notifier,
            order,
            () => true,
            options: new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, DeferCoveredCells = false });

        var (west, east, coarse) = CoveredView();
        coordinator.Register(new[] { coarse, west, east });
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => order.Count == 3));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value
            && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    // A ViewState (EPSG:3857) centred on a WGS-84 point, about 0.2° across at 47.6°N.
    private static ViewState ViewAt(double latitude, double longitude, double resolution = 20.0)
    {
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(longitude, latitude);
        return new ViewState(x, y, resolution, 0, 1000, 800);
    }

    [Fact]
    public async Task Destination_LoadsItsCellsAsPredictedWorkWhenTheGestureStarts()
    {
        var notifier = new FakeNotifier();
        var planner = new DemandPlanner();
        var classes = new ConcurrentDictionary<DatasetEntry, WorkClass>();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            (entry, _) =>
            {
                classes[entry] = WorkDemand.Current!.Priority.Class;
                return Task.CompletedTask;
            },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero },
            planner: planner);

        var here = Cell("HERE", 47.55, -122.45, 47.65, -122.25);
        var there = Cell("THERE", 47.20, -122.55, 47.30, -122.35);
        coordinator.Register(new[] { here, there });
        notifier.Publish(Viewport(47.55, -122.45, 47.65, -122.25));
        Assert.True(await WaitUntilAsync(() => classes.ContainsKey(here)));
        Assert.False(classes.ContainsKey(there));

        // An animated "go to" Tacoma: its cells start loading now, before the
        // view gets there, as predicted work.
        planner.Observe(ViewAt(47.60, -122.35), System.Diagnostics.Stopwatch.GetTimestamp());
        planner.BeginAnimation(ViewAt(47.25, -122.45), TimeSpan.FromSeconds(2), System.Diagnostics.Stopwatch.GetTimestamp());

        Assert.True(await WaitUntilAsync(() => classes.ContainsKey(there)));
        Assert.Equal(WorkClass.Visible, classes[here]);
        Assert.Equal(WorkClass.Predicted, classes[there]);

        // Arriving there counts a cell that was ready: a hit, and a used prediction.
        notifier.Publish(Viewport(47.20, -122.55, 47.30, -122.35));
        var stats = planner.Stats.Snapshot();
        Assert.Equal(1, stats.CellsPredicted);
        Assert.Equal(1, stats.CellsUsed);
        Assert.Equal(1, stats.CellHits);
        Assert.Equal(1, stats.CellMisses);
    }

    [Fact]
    public async Task Arrivals_FollowTheViewsLoads()
    {
        var notifier = new FakeNotifier();
        var planner = new DemandPlanner();
        var gate = new TaskCompletionSource();
        var started = 0;
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            async (_, _) =>
            {
                Interlocked.Increment(ref started);
                await gate.Task;
            },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, MaxConcurrency = 1 },
            planner: planner);

        coordinator.Register(new[]
        {
            Cell("A", 40.0, -75.0, 40.5, -74.5),
            Cell("B", 40.5, -74.5, 41.0, -74.0),
        });
        notifier.Publish(Viewport(40, -75, 41, -74));

        // One loading, one queued: the stack waits for both.
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref started) == 1));
        Assert.Equal(2, planner.Arrivals.Pending);

        gate.SetResult();
        Assert.True(await WaitUntilAsync(() => planner.Arrivals.Pending == 0));
    }

    [Fact]
    public async Task Release_CellsOutsideEveryWindowAreReleased()
    {
        var notifier = new FakeNotifier();
        var planner = new DemandPlanner();
        var loaded = new ConcurrentBag<DatasetEntry>();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            (entry, _) => { loaded.Add(entry); return Task.CompletedTask; },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero },
            planner: planner);

        var cell = Cell("A", 40.0, -75.0, 40.5, -74.5);
        coordinator.Register(new[] { cell });
        notifier.Publish(Viewport(40, -75, 41, -74));
        Assert.True(await WaitUntilAsync(() => loaded.Contains(cell)));

        // Far away, with no history to come back to (the planner has seen no views).
        notifier.Publish(Viewport(10, 10, 11, 11));

        Assert.Equal(1, planner.Stats.Snapshot().CellsReleased);
    }

    [Fact]
    public async Task Destination_CellsStillLoadingThereCoverCoarseCellsOnArrival()
    {
        var notifier = new FakeNotifier();
        var planner = new DemandPlanner();
        var started = new ConcurrentQueue<DatasetEntry>();
        var release = new TaskCompletionSource();
        var (west, east, coarse) = CoveredView();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            async (entry, _) =>
            {
                started.Enqueue(entry);
                await release.Task;
                entry.IsLoaded = true;
            },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero },
            hasVisibleTileWork: () => false,
            planner: planner);
        coordinator.Register(new[] { coarse, west, east });
        notifier.Publish(Viewport(20, -60, 21, -59));

        // An animation to the covered view: the finer cells start loading for it.
        var (minX, minY) = Mapsui.Projections.SphericalMercator.FromLonLat(-75, 40);
        var (maxX, maxY) = Mapsui.Projections.SphericalMercator.FromLonLat(-74, 41);
        const double resolution = 5;
        var target = new ViewState(
            (minX + maxX) / 2, (minY + maxY) / 2, resolution, 0, (maxX - minX) / resolution, (maxY - minY) / resolution);
        planner.Observe(target with { CenterX = target.CenterX + 2_000_000 }, System.Diagnostics.Stopwatch.GetTimestamp());
        planner.BeginAnimation(target, TimeSpan.FromSeconds(1), System.Diagnostics.Stopwatch.GetTimestamp());
        Assert.True(await WaitUntilAsync(() => started.Count == 2));
        Assert.DoesNotContain(coarse, started);

        // The view arrives while they still load: they cover the coarse cell,
        // as cells loading as visible would.
        notifier.Publish(Viewport(40, -75, 41, -74));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(coarse, started);
        Assert.Equal(0, coordinator.GetVisibleWork().QueuedVisible);

        release.SetResult();
    }
}
