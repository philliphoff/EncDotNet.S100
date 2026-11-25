using EncDotNet.S100.Core.Scheduling;
using EncDotNet.S100.Rendering.Engine.Prediction;
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

    /// <summary>
    /// Predicted views less likely than this add no cell demand (map rendering
    /// slice&#160;9). History views (probability 0.1) are kept by the default.
    /// </summary>
    public double MinPredictedProbability { get; init; } = 0.05;

    /// <summary>
    /// A cell loaded for a prediction that hasn't come into view after this
    /// long counts as wasted (map rendering slice&#160;9).
    /// </summary>
    public TimeSpan PredictionExpiry { get; init; } = TimeSpan.FromSeconds(60);
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
    private readonly IMapViewportSet _viewports;
    private readonly Func<DatasetEntry, CancellationToken, Task> _loadAsync;
    private readonly Action<DatasetEntry> _unload;
    private readonly Action<IReadOnlyList<DatasetEntry>>? _unloadMany;
    private readonly LazyLoadOptions _options;
    private readonly ILogger _logger;
    private readonly Action<Action>? _marshal;
    private readonly Func<bool>? _hasVisibleTileWork;
    private readonly DemandPlannerSet? _planners;

    private readonly object _lock = new();
    private readonly HashSet<DatasetEntry> _deferred = new();
    private readonly Dictionary<DatasetEntry, QueuedLoad> _queued = new();
    private readonly Dictionary<DatasetEntry, RunningLoad> _running = new();
    private readonly LruEvictionPolicy<DatasetEntry> _loaded = new();

    private CancellationTokenSource? _debounceCts;
    // Each map's last viewport (for the direction it moved) and the frames of
    // the last evaluation, one per map (split map, #1730). Under _lock.
    private Dictionary<MapViewId, MapViewportSnapshot> _lastSnapshots = new();
    private ViewFrame[] _lastFrames = [];
    private bool _speculativeRetryScheduled;
    private bool _disposed;

    // Prediction v2 (map rendering slice 9), all under _lock: the predicted
    // windows of the last evaluation, the cells in view then (to count cells
    // coming into view), the cells loaded for a prediction and not yet in
    // view (with when they loaded), and the cells the planner released.
    private IReadOnlyList<PredictedWindow> _predicted = [];
    private HashSet<DatasetEntry> _inView = new();
    private readonly Dictionary<DatasetEntry, long> _speculative = new();
    private HashSet<DatasetEntry> _released = new();

    // Settle measurement (map rendering slice 1): how many viewport changes
    // arrived, how many of them an evaluation has caught up with, and when a
    // load last finished. Read by GetVisibleWork; written under _lock.
    private long _viewportChanges;
    private long _evaluatedChanges;
    private long _lastLoadFinishedTimestamp;

    /// <summary>
    /// Constructs a coordinator bound to <paramref name="viewports"/>.
    /// </summary>
    /// <param name="viewports">
    /// Every map's viewport (split map, #1730). A cell is wanted when any
    /// map wants it: each map is classified against its own view and scale,
    /// and a cell takes the strongest tier any of them gives it.
    /// </param>
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
    /// <param name="planners">
    /// The demand planners of the maps this coordinator loads cells for (map
    /// rendering slice&#160;9; one per map since the split map, #1728). The
    /// union of their predicted views a few seconds ahead adds cell demand: a known destination as
    /// <see cref="LoadTier.Destination"/> the moment the gesture starts, the
    /// motion and route as <see cref="LoadTier.Ahead"/>, and recent places as
    /// <see cref="LoadTier.History"/>. Cells outside every window are released
    /// (evicted first), every planner's arrival forecast follows the loads the
    /// views wait for, and their stats count cell hits and waste. When
    /// <see langword="null"/>, only the viewport and its ahead margin count.
    /// </param>
    /// <param name="unloadMany">
    /// Unloads a whole eviction pass's cells at once (typically
    /// <c>IDatasetLoaderService.UnloadEntries</c>), so the map is rebuilt once
    /// rather than per cell. When <see langword="null"/>, <paramref name="unload"/>
    /// is called for each cell.
    /// </param>
    public ExchangeSetLazyLoadCoordinator(
        IMapViewportSet viewports,
        Func<DatasetEntry, CancellationToken, Task> loadAsync,
        Action<DatasetEntry> unload,
        LazyLoadOptions? options,
        Action<Action>? marshal,
        Func<bool>? hasVisibleTileWork,
        Action<IReadOnlyList<DatasetEntry>>? unloadMany,
        DemandPlannerSet? planners,
        ILogger<ExchangeSetLazyLoadCoordinator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(viewports);
        ArgumentNullException.ThrowIfNull(loadAsync);
        ArgumentNullException.ThrowIfNull(unload);

        _viewports = viewports;
        _loadAsync = loadAsync;
        _unload = unload;
        _unloadMany = unloadMany;
        _options = options ?? new LazyLoadOptions();
        _logger = logger ?? NullLogger<ExchangeSetLazyLoadCoordinator>.Instance;
        _marshal = marshal;
        _hasVisibleTileWork = hasVisibleTileWork;
        _planners = planners;

        _viewports.ViewportsChanged += OnViewportsChanged;
        if (_planners is not null)
            _planners.DestinationChanged += OnDestinationChanged;
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

        if (_viewports.Current is { Count: > 0 } seed)
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

    private void OnViewportsChanged(object? sender, IReadOnlyList<MapViewViewport> snapshot)
    {
        if (_disposed) return;

        long change;
        lock (_lock)
        {
            change = ++_viewportChanges;
        }

        if (_options.ViewportDebounce <= TimeSpan.Zero)
        {
            Evaluate(snapshot, change);
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
                _marshal(() => { if (!_disposed) Evaluate(snapshot, change); });
            else if (uiContext is not null)
                uiContext.Post(_ => { if (!_disposed) Evaluate(snapshot, change); }, null);
            else
                Evaluate(snapshot, change);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// A fling or an animation started: plan for its destination now, rather
    /// than after the view settles and the debounce runs out, so its cells
    /// start loading while the view is still moving.
    /// </summary>
    private void OnDestinationChanged(object? sender, EventArgs e)
    {
        if (_disposed || _viewports.Current is not { Count: > 0 } snapshot)
            return;

        if (_marshal is not null)
            _marshal(() => { if (!_disposed) Evaluate(snapshot); });
        else
            Evaluate(snapshot);
    }

    /// <summary>
    /// Evaluates every map's viewport in <paramref name="snapshot"/> against every registered cell:
    /// rebuilds the load queue from the deferred cells' tiers, cancels running
    /// loads whose cells no longer have a tier, touches the LRU for in-view
    /// loaded cells, evicts the coldest off-screen cells beyond the retention
    /// budget, and starts as many queued loads as the slots allow. Exposed to
    /// tests via the zero-debounce path.
    /// </summary>
    internal void Evaluate(IReadOnlyList<MapViewViewport> snapshot) => Evaluate(snapshot, change: null);

    /// <summary>
    /// <see cref="Evaluate(IReadOnlyList{MapViewViewport})"/> for the viewport change
    /// numbered <paramref name="change"/>, which it marks as caught up with.
    /// </summary>
    private void Evaluate(IReadOnlyList<MapViewViewport> snapshot, long? change)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        List<DatasetEntry> victims;
        lock (_lock)
        {
            if (_disposed) return;

            if (change is { } caughtUp && caughtUp > _evaluatedChanges)
                _evaluatedChanges = caughtUp;

            // No map has a viewport (they were all removed): keep what is
            // loaded and wait for one.
            if (snapshot.Count == 0)
                return;

            var frames = new ViewFrame[snapshot.Count];
            var lastSnapshots = new Dictionary<MapViewId, MapViewportSnapshot>(snapshot.Count);
            for (var i = 0; i < snapshot.Count; i++)
            {
                var (view, viewport) = snapshot[i];
                frames[i] = ViewFrame.From(
                    viewport, _lastSnapshots.GetValueOrDefault(view), _options.AheadMarginFraction);
                lastSnapshots[view] = viewport;
            }

            _lastSnapshots = lastSnapshots;
            _lastFrames = frames;
            _predicted = PredictedWindows();

            // Rebuild the queue: a cell that left every view drops out now,
            // and the rest are re-ranked for these views.
            RebuildQueue(frames);

            // Cancel running loads this view no longer wants. The ahead margin
            // keeps a small pan from cancelling a cell that is nearly in view.
            // The rest move to their tier for this view: a cell loading ahead
            // that the view has reached becomes visible work.
            foreach (var (entry, running) in _running)
            {
                if (ClassifyAll(entry, frames) is not { } ranked)
                {
                    running.Cancellation.Cancel();
                    continue;
                }

                running.Demand.Update(PriorityFor(ranked.Tier, ranked.Rank, ranked.Resolution));

                // A cell loading ahead that the view has reached is one the
                // view now waits for.
                if (ranked.Tier is LoadTier.Visible or LoadTier.Destination)
                    ForEachPlanner(p => p.Arrivals.Expect(running.Demand));
            }

            CountCellsInView(frames);

            // Warm the LRU for cells that remain in view so eviction favours
            // off-screen cells.
            var protectedKeys = new HashSet<DatasetEntry>();
            foreach (var loadedEntry in _loadedEntries)
            {
                if (frames.Any(f => f.Intersects(loadedEntry)))
                {
                    _loaded.Touch(loadedEntry);
                    protectedKeys.Add(loadedEntry);
                }
            }

            UpdateReleased(frames);
            victims = _loaded
                .SelectEvictions(_options.RetentionBudget, protectedKeys, _released)
                .ToList();
            foreach (var victim in victims)
            {
                _loaded.Remove(victim);
                _loadedEntries.Remove(victim);
                _released.Remove(victim);
                if (_speculative.Remove(victim))
                    ForEachPlanner(p => p.Stats.RecordCellWasted());
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
    /// Rebuilds <see cref="_queued"/> from the deferred cells for every map's
    /// view. Called under <see cref="_lock"/>.
    /// </summary>
    private void RebuildQueue(IReadOnlyList<ViewFrame> frames)
    {
        _queued.Clear();
        Dictionary<ViewFrame, List<DatasetEntry>>? inView = null;
        Dictionary<PredictedWindow, List<DatasetEntry>>? atDestination = null;
        foreach (var entry in _deferred)
        {
            if (_running.ContainsKey(entry)) continue;
            if (ClassifyAll(entry, frames) is not { } ranked) continue;
            if (ranked.Tier == LoadTier.Visible && _options.DeferCoveredCells)
            {
                // Coverage is judged in each view it's visible in, against
                // that view's own window and scale; the cell keeps the best.
                inView ??= new();
                foreach (var frame in frames)
                {
                    if (Classify(entry, frame) != LoadTier.Visible)
                        continue;
                    if (!inView.TryGetValue(frame, out var cells))
                        inView[frame] = cells = new();
                    cells.Add(entry);
                }
            }
            else if (ranked.Tier == LoadTier.Destination && _options.DeferCoveredCells && ranked.Window is { } destination)
            {
                atDestination ??= new();
                if (!atDestination.TryGetValue(destination, out var cells))
                    atDestination[destination] = cells = new();
                cells.Add(entry);
            }
            else
                _queued[entry] = new QueuedLoad(ranked.Tier, ranked.Rank, ranked.Resolution);
        }

        // A coarse cell that finer cells cover at a destination is deferred
        // there too (#1265), against the destination's own view and scale.
        if (atDestination is not null)
        {
            var resolution = frames[0].Resolution;
            foreach (var (destination, cells) in atDestination)
            {
                foreach (var (entry, tier) in RankCoverage(
                             cells, destination.ToLoadWindow(), destination.Resolution, LoadTier.Destination))
                {
                    _queued[entry] = new QueuedLoad(
                        tier, LoadPrioritizer.RankPredicted(entry.GeographicBounds, destination), resolution);
                }
            }
        }

        if (inView is null)
            return;

        List<string>? covered = null;
        var inViewCount = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (frame, cells) in inView)
        {
            inViewCount += cells.Count;
            foreach (var (entry, tier) in RankCoverage(cells, frame.Window, frame.Resolution, LoadTier.Visible))
            {
                var load = new QueuedLoad(tier, LoadPrioritizer.Rank(entry.GeographicBounds, tier, frame.Window), frame.Resolution);
                if (_queued.TryGetValue(entry, out var other)
                    && (other.Tier < load.Tier || (other.Tier == load.Tier && other.Rank <= load.Rank)))
                {
                    continue;
                }

                _queued[entry] = load;
            }
        }

        foreach (var (entry, load) in _queued)
        {
            if (load.Tier == LoadTier.Covered)
                (covered ??= new()).Add(entry.DisplayName);
        }

        if (covered is not null && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Lazy load deferred {Deferred} of {InView} in-view cells covered by finer cells in {Elapsed} ms: {Cells}.",
                covered.Count, inViewCount, watch.ElapsedMilliseconds, string.Join(", ", covered));
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
        List<DatasetEntry> inView, LoadWindow window, double resolution, LoadTier inViewTier)
    {
        // Covering cells: finer coverage that is, or soon will be, on the map.
        var coverers = new List<(DataModel.CellRank? Rank, NetTopologySuite.Geometries.Geometry Coverage)>();
        foreach (var loaded in _loadedEntries)
        {
            if (loaded.CatalogueCoverage is { } coverage && DrawsNow(loaded, resolution)
                && LazyCellGate.IntersectsViewport(loaded.GeographicBounds, window.South, window.West, window.North, window.East))
            {
                coverers.Add((Rank(loaded), coverage));
            }
        }
        foreach (var (running, load) in _running)
        {
            // A cell loading for a destination the view has reached covers
            // just as one loading as visible does.
            if (load.Tier is LoadTier.Visible or LoadTier.Destination
                && running.CatalogueCoverage is { } coverage && IsShown(running)
                && LazyCellGate.IntersectsViewport(running.GeographicBounds, window.South, window.West, window.North, window.East))
            {
                coverers.Add((Rank(running), coverage));
            }
        }

        var tolerance = CoveredCellGate.ToleranceDegrees(
            resolution, window.CenterLatitude, _options.CoverageTolerancePixels);

        // Finest first, so a cell's own tier is settled before coarser cells
        // decide whether it covers them.
        inView.Sort((a, b) => DataModel.CellScaleRanking.Compare(Rank(a), Rank(b)));
        foreach (var entry in inView)
        {
            var rank = Rank(entry);
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
            yield return (entry, inViewTier);
        }
    }

    private static DataModel.CellRank? Rank(DatasetEntry entry) =>
        DataModel.CellScaleRanking.Rank(entry.CompilationScale, entry.MinimumDisplayScale, entry.MaximumDisplayScale);

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

    private static LoadTier? Classify(DatasetEntry entry, ViewFrame frame) =>
        LoadPrioritizer.Classify(
            entry.GeographicBounds, entry.UsageBand, entry.MinimumDisplayScale, frame.ScaleDenominator, frame.Window);

    /// <summary>Whether <paramref name="entry"/> is visible in any of <paramref name="frames"/>.</summary>
    private static bool IsVisibleInAny(DatasetEntry entry, IReadOnlyList<ViewFrame> frames)
    {
        foreach (var frame in frames)
        {
            if (Classify(entry, frame) == LoadTier.Visible)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The tier and rank of <paramref name="entry"/> for every map's view and
    /// the planners' predicted windows: visible in any view beats everything;
    /// otherwise the strongest of the views' ahead margins and the predicted
    /// windows (a destination before ahead, ahead before history), nearest
    /// first within a tier. <c>Resolution</c> is the resolution of the view
    /// that ranked it, or of the first view for a predicted window. Called
    /// under <see cref="_lock"/>.
    /// </summary>
    private (LoadTier Tier, double Rank, PredictedWindow? Window, double Resolution)? ClassifyAll(
        DatasetEntry entry, IReadOnlyList<ViewFrame> frames)
    {
        (LoadTier Tier, double Rank, ViewFrame Frame)? best = null;
        foreach (var frame in frames)
        {
            if (Classify(entry, frame) is not { } t)
                continue;
            var rank = LoadPrioritizer.Rank(entry.GeographicBounds, t, frame.Window);
            if (best is not { } b || t < b.Tier || (t == b.Tier && rank < b.Rank))
                best = (t, rank, frame);
        }

        if (best is { Tier: LoadTier.Visible } visible)
            return (LoadTier.Visible, visible.Rank, null, visible.Frame.Resolution);

        if (_predicted.Count > 0
            && LoadPrioritizer.ClassifyPredicted(
                entry.GeographicBounds, entry.UsageBand, entry.MinimumDisplayScale, _predicted) is { } predicted
            && (best is null || predicted.Tier < best.Value.Tier))
        {
            return (predicted.Tier, LoadPrioritizer.RankPredicted(entry.GeographicBounds, predicted.Window),
                predicted.Window, frames.Count > 0 ? frames[0].Resolution : double.NaN);
        }

        return best is { } found ? (found.Tier, found.Rank, null, found.Frame.Resolution) : null;
    }

    /// <summary>
    /// Every map's cell views as windows, strongest first. Called under
    /// <see cref="_lock"/>.
    /// </summary>
    private IReadOnlyList<PredictedWindow> PredictedWindows()
    {
        if (_planners is null)
            return [];

        var windows = new List<PredictedWindow>();
        foreach (var view in _planners.CellViews())
        {
            if (view.Probability >= _options.MinPredictedProbability && PredictedWindow.From(view) is { } w)
                windows.Add(w);
        }

        windows.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : b.Probability.CompareTo(a.Probability));
        return windows;
    }

    /// <summary>
    /// Counts the cells that came into view since the last evaluation: a hit
    /// when loaded already, and a use when a prediction loaded it. Only cells
    /// that draw at this scale and that the view loads count (covered ones
    /// wait by design). Called under <see cref="_lock"/>.
    /// </summary>
    private void CountCellsInView(IReadOnlyList<ViewFrame> frames)
    {
        var inView = new HashSet<DatasetEntry>();
        foreach (var entry in _loadedEntries)
        {
            foreach (var frame in frames)
            {
                if (LazyCellGate.IsScaleEligible(entry.UsageBand, entry.MinimumDisplayScale, frame.ScaleDenominator)
                    && frame.Intersects(entry))
                {
                    inView.Add(entry);
                    break;
                }
            }
        }

        foreach (var (entry, queued) in _queued)
        {
            if (queued.Tier == LoadTier.Visible)
                inView.Add(entry);
        }

        foreach (var (entry, running) in _running)
        {
            if (running.Tier == LoadTier.Visible || IsVisibleInAny(entry, frames))
            {
                inView.Add(entry);
            }
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var entry in inView)
        {
            if (_inView.Contains(entry))
                continue;

            var loaded = _loadedEntries.Contains(entry);
            ForEachPlanner(p => p.Stats.RecordCellEntered(loaded));
            if (loaded && _speculative.Remove(entry))
                ForEachPlanner(p => p.Stats.RecordCellUsed());
        }

        _inView = inView;

        // A prediction that never came into view is waste.
        List<DatasetEntry>? expired = null;
        foreach (var (entry, loadedAt) in _speculative)
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(loadedAt, now) > _options.PredictionExpiry)
                (expired ??= new()).Add(entry);
        }

        if (expired is not null)
        {
            foreach (var entry in expired)
            {
                _speculative.Remove(entry);
                ForEachPlanner(p => p.Stats.RecordCellWasted());
            }
        }
    }

    /// <summary>
    /// Release hints (map rendering slice&#160;9): loaded cells outside the
    /// ahead window and every predicted window (including recent places) drop
    /// out of demand and are evicted first. Called under <see cref="_lock"/>.
    /// </summary>
    private void UpdateReleased(IReadOnlyList<ViewFrame> frames)
    {
        if (_planners is null)
            return;

        var released = new HashSet<DatasetEntry>();
        var newly = 0;
        foreach (var entry in _loadedEntries)
        {
            if (ClassifyAll(entry, frames) is not null)
                continue;
            if (entry.GeographicBounds is null)
                continue;
            released.Add(entry);
            if (!_released.Contains(entry))
                newly++;
        }

        _released = released;
        if (newly > 0)
            ForEachPlanner(p => p.Stats.RecordCellsReleased(newly));
    }

    /// <summary>
    /// Tells the planners how many cells the views still wait for that haven't
    /// started loading. Called under <see cref="_lock"/>.
    /// </summary>
    private void ReportQueuedArrivals()
    {
        if (_planners is null)
            return;

        var queued = 0;
        foreach (var load in _queued.Values)
        {
            if (load.Tier is LoadTier.Visible or LoadTier.Destination)
                queued++;
        }

        ForEachPlanner(p => p.Arrivals.SetQueued(queued));
    }

    /// <summary>
    /// Applies <paramref name="action"/> to every map's planner. The loads are
    /// shared, so each map's arrival forecast and stats follow all of them:
    /// a cell one map waits for holds the other map's stack for at most the
    /// load, and the stats describe the shared loader.
    /// </summary>
    private void ForEachPlanner(Action<DemandPlanner> action)
    {
        if (_planners is null)
            return;

        foreach (var planner in _planners.Planners)
            action(planner);
    }

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

                // A destination's cells load like visible ones: the view is
                // on its way there (map rendering slice 9).
                if (queued.Tier is not (LoadTier.Visible or LoadTier.Destination))
                {
                    var speculativeRunning = _running.Values.Count(r => r.Tier is not (LoadTier.Visible or LoadTier.Destination));
                    if (speculativeRunning >= Math.Max(0, _options.MaxSpeculativeConcurrency))
                        break;
                    // A covered cell waits for the visible loads too: the finer
                    // cells covering it are among them, and it would only take
                    // CPU and allocation headroom from them (#1265).
                    if (queued.Tier >= LoadTier.Covered
                        && _running.Values.Any(r => r.Tier is LoadTier.Visible or LoadTier.Destination))
                    {
                        break;
                    }
                    // Nor while the view travels to a known destination:
                    // the destination's own cells and tiles come first, and a
                    // speculative load started now would still be running
                    // when the view arrives (map rendering slice 9).
                    if (_hasVisibleTileWork?.Invoke() == true || _planners?.HasTarget == true)
                    {
                        retry = true;
                        break;
                    }
                }

                _queued.Remove(next);
                var load = new RunningLoad(
                    queued.Tier,
                    new CancellationTokenSource(),
                    new WorkDemand(PriorityFor(queued.Tier, queued.Rank, queued.Resolution)));
                _running[next] = load;
                started.Add((next, load));
                if (queued.Tier is LoadTier.Visible or LoadTier.Destination)
                    ForEachPlanner(p => p.Arrivals.Expect(load.Demand));
            }

            ReportQueuedArrivals();

            if (retry && !_speculativeRetryScheduled)
                _speculativeRetryScheduled = true;
            else
                retry = false;
        }

        foreach (var (entry, load) in started)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Lazy load started {Cell} as {Tier}.", entry.DisplayName, load.Tier);
            _ = RunLoadAsync(entry, load);
        }

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
            // The load's parse and portrayal jobs run on the work scheduler at
            // the cell's tier, and follow it if the view moves (map rendering
            // slice 5).
            Task loading;
            using (WorkDemand.Enter(load.Demand))
            {
                loading = _loadAsync(entry, token);
            }

            await loading.ConfigureAwait(true);
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
                _lastLoadFinishedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

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

                    // A cell loaded for a prediction, not yet in view, is
                    // used when it comes into view and wasted if it doesn't.
                    // A cell that loaded in view (a covered one, say) is
                    // already counted as in view, not as coming into it.
                    if (IsVisibleInAny(entry, _lastFrames))
                    {
                        _inView.Add(entry);
                    }
                    else if (_planners is not null && !_inView.Contains(entry))
                    {
                        _speculative[entry] = System.Diagnostics.Stopwatch.GetTimestamp();
                        ForEachPlanner(p => p.Stats.RecordCellPredicted());
                    }
                }

                // Rank the queue again: a finished finer cell may now cover a
                // coarse one, or turn out not to draw at this scale (or have
                // failed) and so promote a covered cell back to visible. A
                // cancelled cell the view came back to while its load was
                // unwinding is queued again here too, since the evaluation that
                // brought it back skipped it while it was still running.
                // A failed cell isn't queued again at once; the next viewport
                // evaluation retries it.
                if (_lastFrames.Length > 0)
                {
                    RebuildQueue(_lastFrames);
                    if (failed)
                        _queued.Remove(entry);
                }

            }
        }
        load.Cancellation.Dispose();
        ForEachPlanner(p => p.Arrivals.Resolve(load.Demand));

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

    /// <summary>
    /// What the coordinator still has to load for the current view, for the
    /// viewer's settled signal (map rendering slice 1). Only cells in view
    /// count: queued or running as <see cref="LoadTier.Visible"/>, or a cell
    /// that started loading as <see cref="LoadTier.Ahead"/> and that the view
    /// has since moved onto. Covered, ahead and unbounded work doesn't count.
    /// Cheap: it takes the coordinator's lock and walks the queue and running
    /// loads. Safe to call from any thread.
    /// </summary>
    /// <returns>The work.</returns>
    public LazyLoadVisibleWork GetVisibleWork()
    {
        lock (_lock)
        {
            var queuedVisible = 0;
            var queuedOther = 0;
            foreach (var queued in _queued.Values)
            {
                if (queued.Tier == LoadTier.Visible)
                    queuedVisible++;
                else
                    queuedOther++;
            }

            var runningVisible = 0;
            var runningOther = 0;
            foreach (var (entry, running) in _running)
            {
                var nowInView = running.Tier is LoadTier.Ahead or LoadTier.Destination or LoadTier.History
                    && IsVisibleInAny(entry, _lastFrames);
                if (running.Tier == LoadTier.Visible || nowInView)
                    runningVisible++;
                else
                    runningOther++;
            }

            return new LazyLoadVisibleWork(
                EvaluationPending: _evaluatedChanges < _viewportChanges,
                QueuedVisible: queuedVisible,
                RunningVisible: runningVisible,
                QueuedOther: queuedOther,
                RunningOther: runningOther,
                Loaded: _loadedEntries.Count,
                Deferred: _deferred.Count,
                LastLoadFinishedTimestamp: _lastLoadFinishedTimestamp);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _viewports.ViewportsChanged -= OnViewportsChanged;
            if (_planners is not null)
            {
                _planners.DestinationChanged -= OnDestinationChanged;
                foreach (var planner in _planners.Planners)
                    planner.Arrivals.Clear();
            }
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

    /// <summary>
    /// The work-scheduler priority of a cell's load: visible cells as visible
    /// work, cells ahead of the view as predicted work, and covered or
    /// unbounded cells as background work. Within a class, the cell's
    /// distance from the view (<see cref="LoadPrioritizer.Rank"/>, in degrees)
    /// is put in screen pixels at <paramref name="resolution"/>, the same
    /// measure the tile renderer ranks its tiles by.
    /// </summary>
    internal static WorkPriority PriorityFor(LoadTier tier, double rank, double resolution)
    {
        const double metresPerDegree = 111_320;
        var urgency = double.IsFinite(resolution) && resolution > 0
            ? rank * metresPerDegree / resolution
            : rank;
        var workClass = tier switch
        {
            LoadTier.Visible => WorkClass.Visible,
            LoadTier.Destination or LoadTier.Ahead => WorkClass.Predicted,
            _ => WorkClass.Background,
        };
        return new WorkPriority(workClass, urgency);
    }

    private readonly record struct QueuedLoad(LoadTier Tier, double Rank, double Resolution);

    /// <summary>
    /// One map's view for an evaluation: its viewport, the load window around
    /// it (ahead in the direction it moved) and its scale.
    /// </summary>
    private sealed record ViewFrame(MapViewportSnapshot Snapshot, LoadWindow Window, double ScaleDenominator)
    {
        public double Resolution => Snapshot.MercatorResolution;

        public static ViewFrame From(MapViewportSnapshot snapshot, MapViewportSnapshot? previous, double aheadMarginFraction)
        {
            var centreLat = (snapshot.MinLatitude + snapshot.MaxLatitude) / 2.0;
            return new ViewFrame(
                snapshot,
                LoadWindow.From(snapshot, previous, aheadMarginFraction),
                LazyCellGate.ScaleDenominator(snapshot.MercatorResolution, centreLat));
        }

        public bool Intersects(DatasetEntry entry) => LazyCellGate.IntersectsViewport(
            entry.GeographicBounds,
            Snapshot.MinLatitude, Snapshot.MinLongitude,
            Snapshot.MaxLatitude, Snapshot.MaxLongitude);
    }

    private sealed class RunningLoad(LoadTier tier, CancellationTokenSource cancellation, WorkDemand demand)
    {
        public LoadTier Tier { get; } = tier;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public WorkDemand Demand { get; } = demand;
    }
}

/// <summary>
/// A snapshot of <see cref="ExchangeSetLazyLoadCoordinator.GetVisibleWork"/>.
/// </summary>
/// <param name="EvaluationPending">
/// A viewport change hasn't been evaluated yet (it's within the debounce), so
/// the queue doesn't reflect the current view.
/// </param>
/// <param name="QueuedVisible">Cells in view waiting to load.</param>
/// <param name="RunningVisible">Cells in view loading now.</param>
/// <param name="QueuedOther">Covered, ahead and unbounded cells waiting to load.</param>
/// <param name="RunningOther">Covered, ahead and unbounded cells loading now.</param>
/// <param name="Loaded">Cells held loaded.</param>
/// <param name="Deferred">Registered cells not loaded.</param>
/// <param name="LastLoadFinishedTimestamp">
/// The <see cref="System.Diagnostics.Stopwatch"/> timestamp at which a load
/// last finished (loaded, failed or cancelled), or 0.
/// </param>
internal readonly record struct LazyLoadVisibleWork(
    bool EvaluationPending,
    int QueuedVisible,
    int RunningVisible,
    int QueuedOther,
    int RunningOther,
    int Loaded,
    int Deferred,
    long LastLoadFinishedTimestamp)
{
    /// <summary>No viewport change is waiting and no cell in view is queued or loading.</summary>
    public bool VisibleLoaded => !EvaluationPending && QueuedVisible == 0 && RunningVisible == 0;
}
