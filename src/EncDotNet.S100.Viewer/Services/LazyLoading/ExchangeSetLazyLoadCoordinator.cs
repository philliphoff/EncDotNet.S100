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

    /// <summary>
    /// How far beyond the viewport, as a fraction of its span on each side,
    /// cells are loaded speculatively (<see cref="LoadTier.Ahead"/>) so a short
    /// pan finds them ready. The same margin keeps a load that is already
    /// running from being cancelled by a small pan.
    /// </summary>
    public double AheadMarginFraction { get; init; } = 0.5;

    /// <summary>
    /// Maximum number of speculative loads (<see cref="LoadTier.Ahead"/> and
    /// <see cref="LoadTier.Unbounded"/>) running at once. Kept low so a cell
    /// that comes into view finds most slots free.
    /// </summary>
    public int MaxSpeculativeConcurrency { get; init; } = 1;

    /// <summary>
    /// How long to wait before trying again to start speculative loads that
    /// were held back because the tile renderer still had visible tiles to draw.
    /// </summary>
    public TimeSpan SpeculativeRetryDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Whether an in-view cell whose part of the view is covered by finer cells
    /// loads as <see cref="LoadTier.Covered"/> (after the visible and ahead
    /// cells) rather than as <see cref="LoadTier.Visible"/> (#1265).
    /// </summary>
    public bool DeferCoveredCells { get; init; } = true;

    /// <summary>
    /// Uncovered slivers narrower than this many screen pixels don't stop a
    /// cell counting as covered (<see cref="DeferCoveredCells"/>). Catalogue
    /// polygons of neighbouring cells can leave hairline gaps; the deferred cell
    /// still loads and fills them shortly after.
    /// </summary>
    public double CoverageTolerancePixels { get; init; } = 1.0;
}

/// <summary>
/// Coordinates viewport-driven lazy loading of exchange-set cells. Large
/// exchange sets register every cell up front as a lightweight
/// <see cref="DatasetEntry"/> (footprint&#160;+&#160;usage band, no bytes) and
/// hand them here; this coordinator watches the map viewport and loads the
/// cells that are appropriate for the current scale, most urgent first,
/// capping concurrency and evicting the coldest off-screen cells when the
/// retention budget is exceeded.
/// </summary>
/// <remarks>
/// <para>
/// Each evaluation ranks every deferred cell into a <see cref="LoadTier"/> and
/// rebuilds the queue, so a cell that has left the view drops out at once
/// rather than when its turn comes. Visible cells load first, nearest the
/// viewport centre first, and may use every slot. Speculative cells (ahead of
/// the viewport, or with no known footprint) load only when no visible cell is
/// waiting, at most <see cref="LazyLoadOptions.MaxSpeculativeConcurrency"/> at a
/// time, and not while the tile renderer still has visible tiles to draw. This
/// matches the tile renderer's own queue, which draws visible tiles before
/// predicted and pre-warm tiles.
/// </para>
/// <para>
/// An in-view cell whose part of the view is covered by finer cells that draw
/// at this scale (loaded, or loading as visible) is ranked
/// <see cref="LoadTier.Covered"/> instead (#1265): overlap suppression would
/// hide almost all of it, so it loads after the finer cells rather than
/// competing with them. It is deferred, not skipped, and the queue is ranked
/// again whenever a load finishes, so a finer cell that turns out not to draw
/// at this scale (or fails) promotes the coarse cell back to visible. Coverage
/// comes from the catalogue (<see cref="DatasetEntry.CatalogueCoverage"/>);
/// cells known only by a bounding box never count as covering.
/// </para>
/// <para>
/// A running load whose cell falls outside the ahead window (or out of scale)
/// is cancelled; the cell stays deferred and loads again when it comes back
/// into view. The ahead margin keeps a small pan from cancelling work that is
/// nearly in view.
/// </para>
/// <para>
/// The decision logic lives in the pure, unit-tested
/// <see cref="LazyCellGate"/>, <see cref="LoadPrioritizer"/> and
/// <see cref="LruEvictionPolicy{TKey}"/>; this class is the stateful glue:
/// viewport subscription, debounce, the prioritised load pump, and eviction.
/// Load / unload are injected as delegates so the coordinator is testable
/// without the full loader stack.
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
    private readonly Action<IReadOnlyList<DatasetEntry>>? _unloadMany;
    private readonly LazyLoadOptions _options;
    private readonly ILogger _logger;
    private readonly Action<Action>? _marshal;
    private readonly Func<bool>? _hasVisibleTileWork;

    private readonly object _lock = new();
    private readonly HashSet<DatasetEntry> _deferred = new();
    private readonly Dictionary<DatasetEntry, QueuedLoad> _queued = new();
    private readonly Dictionary<DatasetEntry, RunningLoad> _running = new();
    private readonly LruEvictionPolicy<DatasetEntry> _loaded = new();

    private CancellationTokenSource? _debounceCts;
    private MapViewportSnapshot? _lastSnapshot;
    private LoadWindow? _lastWindow;
    private double _lastScaleDenominator = double.NaN;
    private double _lastResolution = double.NaN;
    private bool _speculativeRetryScheduled;
    private bool _disposed;

    /// <summary>
    /// Constructs a coordinator bound to <paramref name="notifier"/>.
    /// </summary>
    /// <param name="notifier">Map-viewport change publisher.</param>
    /// <param name="loadAsync">
    /// Loads a single registered cell (typically
    /// <c>DatasetsViewModel.RequestLoadAsync</c>). Must not throw for a normal
    /// per-cell failure — the loader surfaces those itself. The token is
    /// cancelled when the cell leaves the ahead window; the load should then
    /// stop and leave the cell unloaded.
    /// </param>
    /// <param name="unload">
    /// Unloads a loaded cell's bytes while keeping it registered (typically
    /// <c>IDatasetLoaderService.UnloadEntry</c>), used for LRU eviction.
    /// </param>
    /// <param name="options">Tunables; defaults when <see langword="null"/>.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="marshal">
    /// Runs the debounced evaluation on the UI thread (the viewer passes the
    /// Avalonia dispatcher). Mapsui raises viewport changes from its animation
    /// callbacks, where there is no synchronisation context to capture, so
    /// without this the evaluation and every load it starts ran on the thread
    /// pool and raced on the view-models. When <see langword="null"/>, the
    /// context captured at the viewport change is used, else the evaluation
    /// runs inline.
    /// </param>
    /// <param name="hasVisibleTileWork">
    /// Reports whether the tile renderer still has visible tiles to draw.
    /// Speculative loads wait while it returns <see langword="true"/>. When
    /// <see langword="null"/>, speculative loads never wait for the renderer.
    /// </param>
    /// <param name="unloadMany">
    /// Unloads a whole eviction pass's cells at once (typically
    /// <c>IDatasetLoaderService.UnloadEntries</c>), so the map is rebuilt once
    /// rather than per cell. When <see langword="null"/>, <paramref name="unload"/>
    /// is called for each cell.
    /// </param>
    public ExchangeSetLazyLoadCoordinator(
        IMapViewportNotifier notifier,
        Func<DatasetEntry, CancellationToken, Task> loadAsync,
        Action<DatasetEntry> unload,
        LazyLoadOptions? options = null,
        ILogger<ExchangeSetLazyLoadCoordinator>? logger = null,
        Action<Action>? marshal = null,
        Func<bool>? hasVisibleTileWork = null,
        Action<IReadOnlyList<DatasetEntry>>? unloadMany = null)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(loadAsync);
        ArgumentNullException.ThrowIfNull(unload);

        _notifier = notifier;
        _loadAsync = loadAsync;
        _unload = unload;
        _unloadMany = unloadMany;
        _options = options ?? new LazyLoadOptions();
        _logger = logger ?? NullLogger<ExchangeSetLazyLoadCoordinator>.Instance;
        _marshal = marshal;
        _hasVisibleTileWork = hasVisibleTileWork;

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
                // Idempotent: skip an entry already tracked in any state
                // (deferred, loading, or loaded). Re-marking a since-loaded cell
                // IsDeferred would dim it and show a deferred outline while its
                // layers are still on the map. See issue #458.
                if (_deferred.Contains(entry)
                    || _running.ContainsKey(entry)
                    || _loadedEntries.Contains(entry))
                {
                    continue;
                }
                entry.IsDeferred = true;
                _deferred.Add(entry);
            }
        }

        if (_notifier.Current is { } seed)
            Evaluate(seed);
    }

    /// <summary>
    /// Drops <paramref name="entries"/> from all coordinator tracking (e.g. when
    /// their exchange set is closed) and cancels their running loads. Does not
    /// unload — the caller owns removal from the map.
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
                _queued.Remove(entry);
                if (_running.Remove(entry, out var running))
                    running.Cancellation.Cancel();
                _loaded.Remove(entry);
                // Keep the LRU mirror in lockstep: leaving the entry in
                // _loadedEntries would let Evaluate() re-Touch() it back into
                // the LRU and keep acting on a closed cell. See issue #458.
                _loadedEntries.Remove(entry);
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

        // The viewport notifier fires on the UI thread; capture its
        // synchronisation context so the debounced Evaluate() — which mutates
        // Mapsui layers, the loader, and DatasetEntry properties (all
        // UI-thread-affine) — is marshalled back to the UI thread rather than
        // running on the thread-pool continuation. See issue #458.
        var uiContext = SynchronizationContext.Current;

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
            // Only the most-recent debounce may fire. If a newer viewport
            // change superseded this one, OnViewportChanged already replaced
            // (and disposed) _debounceCts, so this stale continuation must
            // no-op — including when Task.Delay completed before the newer
            // change could cancel it. When still current, dispose the
            // completed CTS here so it does not leak (nothing else will).
            lock (_lock)
            {
                if (!ReferenceEquals(_debounceCts, newCts))
                    return;
                _debounceCts = null;
                newCts.Dispose();
            }

            if (t.IsCanceled || _disposed) return;

            if (_marshal is not null)
                _marshal(() => { if (!_disposed) Evaluate(snapshot); });
            else if (uiContext is not null)
                uiContext.Post(_ => { if (!_disposed) Evaluate(snapshot); }, null);
            else
                Evaluate(snapshot);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Evaluates <paramref name="snapshot"/> against every registered cell:
    /// rebuilds the load queue from the deferred cells' tiers, cancels running
    /// loads whose cells no longer have a tier, touches the LRU for in-view
    /// loaded cells, evicts the coldest off-screen cells beyond the retention
    /// budget, and starts as many queued loads as the slots allow. Exposed to
    /// tests via the zero-debounce path.
    /// </summary>
    internal void Evaluate(MapViewportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var centreLat = (snapshot.MinLatitude + snapshot.MaxLatitude) / 2.0;
        var scaleDenominator = LazyCellGate.ScaleDenominator(
            snapshot.MercatorResolution, centreLat);

        List<DatasetEntry> victims;
        lock (_lock)
        {
            if (_disposed) return;

            var window = LoadWindow.From(snapshot, _lastSnapshot, _options.AheadMarginFraction);
            _lastSnapshot = snapshot;
            _lastWindow = window;
            _lastScaleDenominator = scaleDenominator;
            _lastResolution = snapshot.MercatorResolution;

            // Rebuild the queue: a cell that left the view drops out now, and
            // the rest are re-ranked for this view.
            RebuildQueue(window, scaleDenominator, snapshot.MercatorResolution);

            // Cancel running loads this view no longer wants. The ahead margin
            // keeps a small pan from cancelling a cell that is nearly in view.
            foreach (var (entry, running) in _running)
            {
                if (Classify(entry, scaleDenominator, window) is null)
                    running.Cancellation.Cancel();
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

        if (victims.Count > 0 && _unloadMany is not null)
        {
            try { _unloadMany(victims); }
            catch (Exception ex) { _logger.LogWarning(ex, "Lazy-load eviction unload threw."); }
        }
        else
        {
            foreach (var victim in victims)
            {
                try { _unload(victim); }
                catch (Exception ex) { _logger.LogWarning(ex, "Lazy-load eviction unload threw."); }
            }
        }

        StartQueuedLoads();
    }

    /// <summary>
    /// Rebuilds <see cref="_queued"/> from the deferred cells for a view. Called
    /// under <see cref="_lock"/>.
    /// </summary>
    private void RebuildQueue(LoadWindow window, double scaleDenominator, double resolution)
    {
        _queued.Clear();
        List<DatasetEntry>? inView = null;
        foreach (var entry in _deferred)
        {
            if (_running.ContainsKey(entry)) continue;
            if (Classify(entry, scaleDenominator, window) is not { } tier) continue;
            if (tier == LoadTier.Visible && _options.DeferCoveredCells)
                (inView ??= new()).Add(entry);
            else
                _queued[entry] = new QueuedLoad(tier, LoadPrioritizer.Rank(entry.GeographicBounds, tier, window));
        }

        if (inView is null)
            return;

        List<string>? covered = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (entry, tier) in RankCoverage(inView, window, resolution))
        {
            if (tier == LoadTier.Covered)
                (covered ??= new()).Add(entry.DisplayName);
            _queued[entry] = new QueuedLoad(tier, LoadPrioritizer.Rank(entry.GeographicBounds, tier, window));
        }

        if (covered is not null && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Lazy load deferred {Deferred} of {InView} in-view cells covered by finer cells in {Elapsed} ms: {Cells}.",
                covered.Count, inView.Count, watch.ElapsedMilliseconds, string.Join(", ", covered));
        }
    }

    /// <summary>
    /// Splits in-view deferred cells into <see cref="LoadTier.Visible"/> and
    /// <see cref="LoadTier.Covered"/>, finest first: a cell is covered when the
    /// coverage of strictly finer cells that draw now (loaded, visible, opaque
    /// and inside their display window) or will draw soon (loading or queued as
    /// visible) fills its part of the view. Called under <see cref="_lock"/>.
    /// </summary>
    private IEnumerable<(DatasetEntry Entry, LoadTier Tier)> RankCoverage(
        List<DatasetEntry> inView, LoadWindow window, double resolution)
    {
        // Covering cells: finer coverage that is, or soon will be, on the map.
        var coverers = new List<(int? Rank, NetTopologySuite.Geometries.Geometry Coverage)>();
        foreach (var loaded in _loadedEntries)
        {
            if (loaded.CatalogueCoverage is { } coverage && DrawsNow(loaded, resolution)
                && LazyCellGate.IntersectsViewport(loaded.GeographicBounds, window.South, window.West, window.North, window.East))
            {
                coverers.Add((RankingScale(loaded), coverage));
            }
        }
        foreach (var (running, load) in _running)
        {
            if (load.Tier == LoadTier.Visible && running.CatalogueCoverage is { } coverage && IsShown(running))
                coverers.Add((RankingScale(running), coverage));
        }

        var tolerance = CoveredCellGate.ToleranceDegrees(
            resolution, window.CenterLatitude, _options.CoverageTolerancePixels);

        // Finest first, so a cell's own tier is settled before coarser cells
        // decide whether it covers them.
        inView.Sort((a, b) => DataModel.CellScaleRanking.Compare(RankingScale(a), RankingScale(b)));
        foreach (var entry in inView)
        {
            var rank = RankingScale(entry);
            var finer = coverers
                .Where(c => DataModel.CellScaleRanking.IsFiner(c.Rank, rank))
                .Select(c => c.Coverage)
                .ToList();
            if (finer.Count > 0
                && CoveredCellGate.IsCoveredInView(entry.CatalogueCoverage, entry.GeographicBounds, finer, window, tolerance))
            {
                yield return (entry, LoadTier.Covered);
                continue;
            }

            if (entry.CatalogueCoverage is { } own && IsShown(entry))
                coverers.Add((rank, own));
            yield return (entry, LoadTier.Visible);
        }
    }

    private static int? RankingScale(DatasetEntry entry) =>
        DataModel.CellScaleRanking.RankingScale(entry.CompilationScale, entry.MinimumDisplayScale);

    /// <summary>
    /// Whether a loaded cell draws, fully opaque, at <paramref name="resolution"/>:
    /// loaded, shown (<see cref="IsShown"/>), and not zoomed out past its display window.
    /// </summary>
    private static bool DrawsNow(DatasetEntry entry, double resolution) =>
        entry.IsLoaded
        && IsShown(entry)
        && (entry.ContentMaxVisibleResolution is not { } max || double.IsNaN(resolution) || resolution <= max);

    /// <summary>Whether the user has the cell switched on at full opacity.</summary>
    private static bool IsShown(DatasetEntry entry) => entry.IsVisible && entry.Opacity >= 1.0;

    private static LoadTier? Classify(DatasetEntry entry, double scaleDenominator, LoadWindow window) =>
        LoadPrioritizer.Classify(
            entry.GeographicBounds, entry.UsageBand, entry.MinimumDisplayScale, scaleDenominator, window);

    // Mirror of the LRU membership for enumeration: the LRU is the authority for
    // ordering / eviction; this set answers "is this entry currently loaded?"
    // and is kept in lockstep with the LRU under _lock.
    private readonly HashSet<DatasetEntry> _loadedEntries = new();

    /// <summary>
    /// Starts queued loads, best first, while slots are free: visible cells may
    /// take every slot; speculative cells start only when no visible cell is
    /// queued, within their own cap, and not while the tile renderer has
    /// visible tiles to draw. Covered and unbounded cells also wait until no
    /// visible load is running.
    /// </summary>
    private void StartQueuedLoads()
    {
        var started = new List<(DatasetEntry Entry, RunningLoad Load)>();
        var retry = false;
        lock (_lock)
        {
            if (_disposed) return;

            var maxConcurrency = Math.Max(1, _options.MaxConcurrency);
            while (_running.Count < maxConcurrency && _queued.Count > 0)
            {
                var (next, queued) = _queued
                    .OrderBy(q => q.Value.Tier)
                    .ThenBy(q => q.Value.Rank)
                    .First();

                if (queued.Tier != LoadTier.Visible)
                {
                    var speculativeRunning = _running.Values.Count(r => r.Tier != LoadTier.Visible);
                    if (speculativeRunning >= Math.Max(0, _options.MaxSpeculativeConcurrency))
                        break;
                    // A covered cell waits for the visible loads too: the finer
                    // cells covering it are among them, and it would only take
                    // CPU and allocation headroom from them (#1265).
                    if (queued.Tier >= LoadTier.Covered
                        && _running.Values.Any(r => r.Tier == LoadTier.Visible))
                    {
                        break;
                    }
                    if (_hasVisibleTileWork?.Invoke() == true)
                    {
                        retry = true;
                        break;
                    }
                }

                _queued.Remove(next);
                var load = new RunningLoad(queued.Tier, new CancellationTokenSource());
                _running[next] = load;
                started.Add((next, load));
            }

            if (retry && !_speculativeRetryScheduled)
                _speculativeRetryScheduled = true;
            else
                retry = false;
        }

        foreach (var (entry, load) in started)
            _ = RunLoadAsync(entry, load);

        if (retry)
            ScheduleSpeculativeRetry();
    }

    private void ScheduleSpeculativeRetry()
    {
        var uiContext = SynchronizationContext.Current;
        _ = Task.Delay(_options.SpeculativeRetryDelay).ContinueWith(_ =>
        {
            lock (_lock)
            {
                _speculativeRetryScheduled = false;
                if (_disposed) return;
            }

            if (_marshal is not null)
                _marshal(StartQueuedLoads);
            else if (uiContext is not null)
                uiContext.Post(_ => StartQueuedLoads(), null);
            else
                StartQueuedLoads();
        }, TaskScheduler.Default);
    }

    private async Task RunLoadAsync(DatasetEntry entry, RunningLoad load)
    {
        var token = load.Cancellation.Token;
        var failed = false;
        try
        {
            await _loadAsync(entry, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lazy-load cell load threw.");
            failed = true;
        }

        bool aborted;
        bool disposed;
        lock (_lock)
        {
            // If Unregister()/Dispose() ran while this cell was loading, the
            // entry is no longer tracked; treat the finished load as stale
            // rather than re-adding layers for a closed exchange set (zombie
            // layers) or resurrecting an entry the UI has already dropped.
            // See issue #458.
            disposed = _disposed;
            aborted = disposed
                || !_running.TryGetValue(entry, out var current)
                || !ReferenceEquals(current, load);
            if (!aborted)
            {
                _running.Remove(entry);

                // A cancelled load that did not finish leaves the cell deferred,
                // so it loads again when it comes back into view. A failed one
                // also stays deferred and is retried by a later evaluation.
                var cancelled = token.IsCancellationRequested && !entry.IsLoaded;
                if (!cancelled && !failed)
                {
                    _deferred.Remove(entry);
                    _loaded.Touch(entry);
                    _loadedEntries.Add(entry);
                    entry.IsDeferred = false;
                }

                // Rank the queue again: a finished finer cell may now cover a
                // coarse one, or turn out not to draw at this scale (or have
                // failed) and so promote a covered cell back to visible. A
                // cancelled cell the view came back to while its load was
                // unwinding is queued again here too, since the evaluation that
                // brought it back skipped it while it was still running.
                // A failed cell isn't queued again at once; the next viewport
                // evaluation retries it.
                if (_lastWindow is { } window)
                {
                    RebuildQueue(window, _lastScaleDenominator, _lastResolution);
                    if (failed)
                        _queued.Remove(entry);
                }

            }
        }
        load.Cancellation.Dispose();

        // Unwind a load the viewport still wanted but whose exchange set was
        // closed mid-flight. When the whole coordinator is disposing, skip
        // the unload — teardown removes the layers wholesale and we must not
        // touch the map during shutdown.
        if (aborted && !disposed)
        {
            try { _unload(entry); }
            catch (Exception ex) { _logger.LogWarning(ex, "Lazy-load abort unload threw."); }
        }

        if (!disposed)
            StartQueuedLoads();
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
            foreach (var running in _running.Values)
                running.Cancellation.Cancel();
            _deferred.Clear();
            _queued.Clear();
            _running.Clear();
            _loaded.Clear();
            _loadedEntries.Clear();
        }
    }

    private readonly record struct QueuedLoad(LoadTier Tier, double Rank);

    private sealed class RunningLoad(LoadTier tier, CancellationTokenSource cancellation)
    {
        public LoadTier Tier { get; } = tier;

        public CancellationTokenSource Cancellation { get; } = cancellation;
    }
}
