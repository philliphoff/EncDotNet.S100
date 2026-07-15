using System.Collections.Concurrent;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

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

        await Task.Delay(100);
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

        await Task.Delay(100);
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
        await Task.Delay(100);
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
        await Task.Delay(50);
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
        await Task.Delay(80);
        Assert.Single(loaded);
    }
}
