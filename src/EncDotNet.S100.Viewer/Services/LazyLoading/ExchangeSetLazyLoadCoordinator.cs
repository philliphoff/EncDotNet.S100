using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>
/// Tunable parameters for <see cref="ExchangeSetLazyLoadCoordinator"/>.
/// Centralised so the whole lazy-loading heuristic can be adjusted in one
/// place. See issue #458.
/// </summary>
internal sealed record LazyLoadOptions
{
    /// <summary>
    /// Cell-count above which an opened exchange set switches from eager
    /// (load-every-cell) to lazy (register-then-load-on-demand) behaviour.
    /// A rough proxy: small sets load eagerly for simplicity, large ones defer.
    /// </summary>
    public int CellThreshold { get; init; } = 50;

    /// <summary>
    /// Maximum number of cell loads allowed to run concurrently. Defaults to
    /// half the logical processor count (min&#160;2) to leave head-room for the
    /// render subsystem, which rasterises tiles on worker threads — saturating
    /// every core with parse/translate work starves rendering and re-freezes
    /// the UI the feature set out to keep responsive.
    /// </summary>
    public int MaxConcurrency { get; init; } = Math.Max(2, Environment.ProcessorCount / 2);

    /// <summary>
    /// Maximum number of loaded cells to retain in memory. When the loaded set
    /// exceeds this budget the coldest off-screen cells are evicted (their
    /// bytes unloaded, their extent outline kept). Generous by default so
    /// normal panning never thrashes; the eviction is a safety valve against
    /// the unbounded-memory growth that motivated the feature.
    /// </summary>
    public int RetentionBudget { get; init; } = 400;

    /// <summary>
    /// Trailing-edge debounce applied to viewport changes before re-evaluating
    /// which cells to load, so a single pan/zoom gesture coalesces into one
    /// pass. <see cref="TimeSpan.Zero"/> disables debouncing (used by tests).
    /// </summary>
    public TimeSpan ViewportDebounce { get; init; } = TimeSpan.FromMilliseconds(200);
}

/// <summary>
/// Coordinates viewport-driven lazy loading of exchange-set cells. Large
/// exchange sets register every cell up front as a lightweight
/// <see cref="DatasetEntry"/> (footprint&#160;+&#160;usage band, no bytes) and
/// hand them here; this coordinator watches the map viewport and loads only the
/// cells that are both in view and appropriate for the current scale, capping
/// concurrency and evicting the coldest off-screen cells when the retention
/// budget is exceeded.
/// </summary>
/// <remarks>
/// <para>
/// The decision logic lives in the pure, unit-tested
/// <see cref="LazyCellGate"/> and <see cref="LruEvictionPolicy{TKey}"/>; this
/// class is the stateful glue: viewport subscription, debounce, a
/// bounded-concurrency load pump, and eviction. Load / unload are injected as
/// delegates so the coordinator is testable without the full loader stack.
/// </para>
/// <para>
/// The coordinator assumes it is driven on the UI thread (the map viewport
/// notifier fires there and the injected load delegate dispatches its own
/// off-thread work), but guards its shared registries with a lock for safety.
/// See issue #458.
/// </para>
/// </remarks>
internal sealed class ExchangeSetLazyLoadCoordinator : IDisposable
{
    private readonly IMapViewportNotifier _notifier;
    private readonly Func<DatasetEntry, CancellationToken, Task> _loadAsync;
    private readonly Action<DatasetEntry> _unload;
    private readonly LazyLoadOptions _options;
    private readonly ILogger _logger;

    private readonly object _lock = new();
    private readonly HashSet<DatasetEntry> _deferred = new();
    private readonly HashSet<DatasetEntry> _loading = new();
    private readonly LruEvictionPolicy<DatasetEntry> _loaded = new();
    private readonly SemaphoreSlim _gate;

    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    /// <summary>
    /// Constructs a coordinator bound to <paramref name="notifier"/>.
    /// </summary>
    /// <param name="notifier">Map-viewport change publisher.</param>
    /// <param name="loadAsync">
    /// Loads a single registered cell (typically
    /// <c>DatasetsViewModel.RequestLoadAsync</c>). Must not throw for a normal
    /// per-cell failure — the loader surfaces those itself.
    /// </param>
    /// <param name="unload">
    /// Unloads a loaded cell's bytes while keeping it registered (typically
    /// <c>IDatasetLoaderService.UnloadEntry</c>), used for LRU eviction.
    /// </param>
    /// <param name="options">Tunables; defaults when <see langword="null"/>.</param>
    /// <param name="logger">Optional logger.</param>
    public ExchangeSetLazyLoadCoordinator(
        IMapViewportNotifier notifier,
        Func<DatasetEntry, CancellationToken, Task> loadAsync,
        Action<DatasetEntry> unload,
        LazyLoadOptions? options = null,
        ILogger<ExchangeSetLazyLoadCoordinator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(loadAsync);
        ArgumentNullException.ThrowIfNull(unload);

        _notifier = notifier;
        _loadAsync = loadAsync;
        _unload = unload;
        _options = options ?? new LazyLoadOptions();
        _logger = logger ?? NullLogger<ExchangeSetLazyLoadCoordinator>.Instance;
        _gate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));

        _notifier.ViewportChanged += OnViewportChanged;
    }

    /// <summary>The active tunables.</summary>
    public LazyLoadOptions Options => _options;

    /// <summary>Test hook: the number of cells currently registered as deferred.</summary>
    internal int DeferredCount { get { lock (_lock) return _deferred.Count; } }

    /// <summary>Test hook: the number of cells currently held loaded.</summary>
    internal int LoadedCount { get { lock (_lock) return _loaded.Count; } }

    /// <summary>
    /// Registers <paramref name="entries"/> as deferred (not-yet-loaded) cells
    /// and immediately evaluates the current viewport so any that are already in
    /// view begin loading. Each entry is marked
    /// <see cref="DatasetEntry.IsDeferred"/>. Safe to call repeatedly; entries
    /// already registered are ignored.
    /// </summary>
    /// <param name="entries">The registered-not-loaded cell entries.</param>
    public void Register(IEnumerable<DatasetEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_lock)
        {
            if (_disposed) return;
            foreach (var entry in entries)
            {
                if (entry is null) continue;
                entry.IsDeferred = true;
                _deferred.Add(entry);
            }
        }

        if (_notifier.Current is { } seed)
            Evaluate(seed);
    }

    /// <summary>
    /// Drops <paramref name="entries"/> from all coordinator tracking (e.g. when
    /// their exchange set is closed). Does not unload — the caller owns removal
    /// from the map.
    /// </summary>
    /// <param name="entries">The entries to forget.</param>
    public void Unregister(IEnumerable<DatasetEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_lock)
        {
            foreach (var entry in entries)
            {
                if (entry is null) continue;
                _deferred.Remove(entry);
                _loading.Remove(entry);
                _loaded.Remove(entry);
            }
        }
    }

    private void OnViewportChanged(object? sender, MapViewportSnapshot snapshot)
    {
        if (_disposed) return;

        if (_options.ViewportDebounce <= TimeSpan.Zero)
        {
            Evaluate(snapshot);
            return;
        }

        CancellationTokenSource newCts;
        lock (_lock)
        {
            if (_disposed) return;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            newCts = new CancellationTokenSource();
            _debounceCts = newCts;
        }

        _ = Task.Delay(_options.ViewportDebounce, newCts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled || _disposed) return;
            lock (_lock)
            {
                if (ReferenceEquals(_debounceCts, newCts))
                    _debounceCts = null;
            }
            Evaluate(snapshot);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Evaluates <paramref name="snapshot"/> against every registered cell:
    /// starts gated loads for in-view, scale-eligible deferred cells; touches
    /// the LRU for in-view loaded cells; then evicts the coldest off-screen
    /// cells beyond the retention budget. Exposed to tests via the zero-debounce
    /// path.
    /// </summary>
    internal void Evaluate(MapViewportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var centreLat = (snapshot.MinLatitude + snapshot.MaxLatitude) / 2.0;
        var scaleDenominator = LazyCellGate.ScaleDenominator(
            snapshot.MercatorResolution, centreLat);

        List<DatasetEntry> toLoad = new();
        List<DatasetEntry> victims = new();
        lock (_lock)
        {
            if (_disposed) return;

            foreach (var entry in _deferred)
            {
                if (_loading.Contains(entry)) continue;
                if (LazyCellGate.ShouldBeLoaded(
                        entry.GeographicBounds, entry.UsageBand, scaleDenominator,
                        snapshot.MinLatitude, snapshot.MinLongitude,
                        snapshot.MaxLatitude, snapshot.MaxLongitude))
                {
                    toLoad.Add(entry);
                }
            }

            // Warm the LRU for cells that remain in view so eviction favours
            // off-screen cells.
            var protectedKeys = new HashSet<DatasetEntry>();
            foreach (var loadedEntry in _loadedEntries)
            {
                if (LazyCellGate.IntersectsViewport(
                        loadedEntry.GeographicBounds,
                        snapshot.MinLatitude, snapshot.MinLongitude,
                        snapshot.MaxLatitude, snapshot.MaxLongitude))
                {
                    _loaded.Touch(loadedEntry);
                    protectedKeys.Add(loadedEntry);
                }
            }

            foreach (var entry in toLoad)
                _loading.Add(entry);

            victims = _loaded
                .SelectEvictions(_options.RetentionBudget, protectedKeys)
                .ToList();
            foreach (var victim in victims)
            {
                _loaded.Remove(victim);
                _loadedEntries.Remove(victim);
                // Return the evicted cell to the deferred pool so it reloads
                // (from scratch) the next time it enters the viewport.
                _deferred.Add(victim);
            }
        }

        foreach (var victim in victims)
        {
            try { _unload(victim); }
            catch (Exception ex) { _logger.LogWarning(ex, "Lazy-load eviction unload threw."); }
        }

        foreach (var entry in toLoad)
            _ = PumpLoadAsync(entry);
    }

    // Mirror of the LRU membership for enumeration: the LRU is the authority for
    // ordering / eviction; this set answers "is this entry currently loaded?"
    // and is kept in lockstep with the LRU under _lock.
    private readonly HashSet<DatasetEntry> _loadedEntries = new();

    private async Task PumpLoadAsync(DatasetEntry entry)
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            await _loadAsync(entry, CancellationToken.None).ConfigureAwait(true);
            lock (_lock)
            {
                _deferred.Remove(entry);
                _loading.Remove(entry);
                _loaded.Touch(entry);
                _loadedEntries.Add(entry);
                entry.IsDeferred = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lazy-load cell load threw.");
            lock (_lock) _loading.Remove(entry);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _notifier.ViewportChanged -= OnViewportChanged;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
            _deferred.Clear();
            _loading.Clear();
            _loaded.Clear();
            _loadedEntries.Clear();
        }
        _gate.Dispose();
    }
}
