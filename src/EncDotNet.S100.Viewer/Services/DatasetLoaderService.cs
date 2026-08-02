using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Input.Platform;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IDatasetLoaderService"/> implementation. Owns the
/// dataset pipeline factory, the per-entry processor + layer maps, and
/// drives map mutations through focused layer and viewport capabilities.
/// </summary>
internal sealed class DatasetLoaderService : IDatasetLoaderService, IMapPresentationController
{
    private readonly ViewerSettings _settings;
    private readonly PortrayalCatalogueManager _catalogueManager;
    private readonly PortrayalCatalogueSeeder _catalogueSeeder;
    private readonly FeatureCatalogueOverrides _fcOverrides;
    private readonly DatasetPipelineFactory _pipelineFactory;
    private readonly IRecentFilesService _recentFiles;
    private readonly S128DatasetCatalogSource _s128CatalogSource;
    private readonly GlobalTimeService _globalTime;
    private readonly INotificationService _notifications;
    /// <summary>
    /// Resolves the <em>currently active</em> cross-dataset paint-order
    /// policy on each consult. Hosts can swap the authority at runtime
    /// (e.g. flip from S-98 to strict load-order) and we re-sort the
    /// stack in response to <see cref="IInteroperabilityAuthorityProvider.CurrentChanged"/>.
    /// </summary>
    private readonly IInteroperabilityAuthorityProvider _authorityProvider;
    private readonly EncDotNet.S100.Renderers.Mapsui.MapsuiDatasetRenderer _mapsuiRenderer;
    /// <summary>
    /// Lets a standalone dataset load hold its progress notification in the
    /// "loading" state until the map has actually painted the new dataset,
    /// so the terminal success only appears once the data is visible rather
    /// than merely parsed. Optional: <see langword="null"/> (e.g. in tests,
    /// or before the map control exists) disables the wait.
    /// </summary>
    private readonly IRenderActivityMonitor? _renderActivityMonitor;

    private readonly Dictionary<DatasetEntry, IDatasetProcessor> _processors = new();
    private readonly Dictionary<DatasetEntry, IReadOnlyList<ILayer>> _entryLayers = new();
    /// <summary>
    /// Per-entry S-98 layer-stack entries produced by the processor's
    /// most recent render. Each entry's <see cref="LayerStackEntry.Layer"/>
    /// also appears in <see cref="_entryLayers"/>. Populated from
    /// <see cref="MapsuiDatasetResult.StackEntries"/> when available; otherwise
    /// synthesised through the active <see cref="IInteroperabilityAuthority"/>.
    /// </summary>
    private readonly Dictionary<DatasetEntry, IReadOnlyList<LayerStackEntry>> _entryStackEntries = new();
    /// <summary>
    /// Snapshot of the most recently computed S-98 layer stack
    /// (bottom-of-paint-stack first; index 0 = drawn first / under
    /// everything else). Mirrors what was just handed to
    /// <see cref="IMapLayerCollection.ReplaceDatasetLayers"/>. Refreshed whenever
    /// the layer order changes so <see cref="PickService"/> can rank
    /// multi-hit picks top-of-stack first.
    /// </summary>
    private IReadOnlyList<ILayer> _currentStackedLayers = Array.Empty<ILayer>();
    /// <summary>
    /// Snapshot of the most recently computed S-98 layer stack as
    /// <see cref="LayerStackEntry"/> records (bottom-of-paint-stack
    /// first). Same order as <see cref="_currentStackedLayers"/>;
    /// the Layer Stack panel
    /// (<see cref="ViewModels.LayerStackViewModel"/>) groups these
    /// by <see cref="S98DisplayPlane"/> for the tree view.
    /// </summary>
    private IReadOnlyList<LayerStackEntry> _currentStackEntries = Array.Empty<LayerStackEntry>();
    /// <summary>
    /// Per-entry sub-layer keys, parallel by index to
    /// <see cref="_entryLayers"/>. Null when the processor did not
    /// supply per-layer names (single-layer products).
    /// </summary>
    private readonly Dictionary<DatasetEntry, IReadOnlyList<string>?> _entryLayerKeys = new();
    /// <summary>
    /// Per-entry data-coverage footprint (EPSG:3857) and scale-band denominator
    /// used for cross-cell scale-band overlap suppression (issue #438 Phase 2).
    /// Populated from <see cref="MapsuiDatasetResult.CoverageGeometry"/> and the
    /// entry's coarsest display scale on each render; consumed by
    /// <see cref="ApplyOverlapSuppression"/>.
    /// </summary>
    private readonly Dictionary<DatasetEntry, (NetTopologySuite.Geometries.Geometry Coverage, int ScaleDenominator)> _entryCoverage = new();
    private readonly HashSet<DatasetEntry> _subscribedEntries = new();
    /// <summary>
    /// Canonical paint-order of dataset entries. Mirrors the order the
    /// user sees in the Datasets panel; index 0 is the TOP of the
    /// paint stack (drawn last, on top of every other dataset) — the
    /// Photoshop/QGIS convention. Mutated only by the loader so
    /// palette/time re-renders don't disturb user-driven ordering.
    /// </summary>
    private readonly List<DatasetEntry> _entryOrder = new();
    private readonly ReadOnlyDictionary<DatasetEntry, IDatasetProcessor> _processorsView;
    private readonly ReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> _entryLayersView;
    private readonly SemaphoreSlim _layerRenderGate = new(1, 1);
    private readonly object _presentationSync = new();

    private IMapLayerCollection? _layerCollection;
    private IMapViewportController? _viewport;
    private MapPresentationState _presentation;
    private CancellationTokenSource? _presentationCts;

    private MapPresentationState CurrentPresentation =>
        Volatile.Read(ref _presentation);

    /// <inheritdoc />
    public bool IsInitialized => _layerCollection is not null && _viewport is not null;

    // Coalesce slider scrubs into a single render pass after the user has
    // paused for ~100 ms. Each new SetCurrentTime cancels the in-flight
    // debounce + render so we never queue dozens of stale renders behind
    // the latest mouse position.
    private static readonly TimeSpan ScrubDebounceWindow = TimeSpan.FromMilliseconds(100);
    private CancellationTokenSource? _scrubCts;

    public DatasetLoaderService(
        ViewerSettings settings,
        PortrayalCatalogueManager catalogueManager,
        PortrayalCatalogueSeeder catalogueSeeder,
        FeatureCatalogueOverrides fcOverrides,
        DatasetPipelineFactory pipelineFactory,
        IRecentFilesService recentFiles,
        S128DatasetCatalogSource s128CatalogSource,
        MapPresentationState presentation,
        GlobalTimeService globalTime,
        INotificationService notifications,
        IInteroperabilityAuthorityProvider authorityProvider,
        EncDotNet.S100.Renderers.Mapsui.MapsuiDatasetRenderer mapsuiRenderer,
        IRenderActivityMonitor? renderActivityMonitor = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogueManager);
        ArgumentNullException.ThrowIfNull(catalogueSeeder);
        ArgumentNullException.ThrowIfNull(fcOverrides);
        ArgumentNullException.ThrowIfNull(pipelineFactory);
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(s128CatalogSource);
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(globalTime);
        ArgumentNullException.ThrowIfNull(notifications);

        _settings = settings;
        _catalogueManager = catalogueManager;
        _catalogueSeeder = catalogueSeeder;
        _fcOverrides = fcOverrides;
        _pipelineFactory = pipelineFactory;
        _recentFiles = recentFiles;
        _s128CatalogSource = s128CatalogSource;
        _presentation = presentation;
        _globalTime = globalTime;
        _notifications = notifications;
        ArgumentNullException.ThrowIfNull(authorityProvider);
        _authorityProvider = authorityProvider;
        ArgumentNullException.ThrowIfNull(mapsuiRenderer);
        _mapsuiRenderer = mapsuiRenderer;
        _renderActivityMonitor = renderActivityMonitor;
        // Re-sort the live layer stack whenever the host swaps the
        // active authority. Cheap when no datasets are loaded.
        _authorityProvider.CurrentChanged += OnAuthorityChanged;

        _processorsView = new ReadOnlyDictionary<DatasetEntry, IDatasetProcessor>(_processors);
        _entryLayersView = new ReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>>(_entryLayers);

        _globalTime.CurrentTimeChanged += t => _ = ReRenderAtTimeAsync(t, CancellationToken.None);
        _globalTime.RangeChanged += OnGlobalRangeChanged;
    }

    /// <summary>
    /// Re-applies the time gate when the aggregate timeline shifts (a
    /// dataset registered or unregistered). Datasets that finish loading
    /// before the global clock exists are rendered in full; once
    /// registration establishes (or moves) the clock this snaps every
    /// registered dataset to it and hides those outside their covered
    /// window. The work is delegated to <see cref="ReRenderAtTimeAsync"/>,
    /// whose debounce collapses a bulk exchange-set load (many rapid
    /// registrations) into a single gate pass.
    /// </summary>
    private void OnGlobalRangeChanged()
    {
        if (_globalTime.CurrentTime is { } now && _globalTime.Adapters.Count > 0)
            _ = ReRenderAtTimeAsync(now, CancellationToken.None);
    }

    public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors => _processorsView;
    public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers => _entryLayersView;

    public IReadOnlyList<ILayer> CurrentStackedLayers => _currentStackedLayers;

    public IReadOnlyList<LayerStackEntry> CurrentStackEntries => _currentStackEntries;

    public event Action? LayerStackChanged;

    public event Action<string>? ActiveChanged;

    public bool GetActive(string datasetId)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetId);
        return FindEntry(datasetId)?.IsActive ?? true;
    }

    public void SetActive(string datasetId, bool active)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetId);
        var entry = FindEntry(datasetId);
        if (entry is null || entry.IsActive == active) return;
        entry.IsActive = active;
        // Recompute the cross-product stack so R-101-102-B (and any
        // future Active-aware rules) re-evaluates with the new
        // flag, and rebroadcast it through the map host so PickService
        // / Layer Stack panel see the change.
        if (_layerCollection is not null)
            _layerCollection.ReplaceDatasetLayers(FlattenLayerOrder());
        ActiveChanged?.Invoke(datasetId);
    }

    public event Action<DatasetEntry>? DatasetLoaded;

    public event Action<DatasetEntry>? DatasetRemoved;

    /// <inheritdoc />
    public bool SuppressAutoZoom { get; set; }

    /// <summary>
    /// Surfaces problems encountered while applying S-101 sequential updates
    /// as a non-blocking warning toast. Successful update application is a
    /// routine, internal part of loading and is not surfaced — only a partial
    /// or failed apply (which may leave the chart missing corrections) warrants
    /// the user's attention. Updates are applied best-effort, so a partial
    /// result never prevents the dataset from rendering. S-101 / S-100 Part 10a.
    /// </summary>
    private void SurfaceUpdateReport(DatasetEntry entry, S101UpdateReport report)
    {
        var problem = report.Messages
            .FirstOrDefault(m => m.Severity >= S101UpdateSeverity.Warning);

        if (problem.Severity >= S101UpdateSeverity.Warning)
        {
            var appliedCount = report.AppliedThroughUpdateNumber - report.BaseUpdateNumber;
            var msg = string.Format(
                Strings.Status_ExchangeSetUpdatesPartial,
                appliedCount, entry.DisplayName, problem.Text);
            _notifications.Create(Strings.Toast_Warning)
                .WithSeverity(NotificationSeverity.Warning)
                .WithContent(msg)
                .Show();
        }
    }

    public void Initialize(
        IMapLayerCollection layerCollection,
        IMapViewportController viewport,
        ViewerCommandSettings? options)
    {
        ArgumentNullException.ThrowIfNull(layerCollection);
        ArgumentNullException.ThrowIfNull(viewport);
        if (_layerCollection is not null)
            throw new InvalidOperationException("DatasetLoaderService has already been initialized.");

        _layerCollection = layerCollection;
        _viewport = viewport;

        var transientFcPaths = _catalogueSeeder.Seed(options);
        _fcOverrides.SetTransientPaths(transientFcPaths);

    }

    public async Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureInitialized();

        using var __cmd = ViewerObservability.BeginCommand("dataset.open");

        // Create a linked CTS so the caller's token and the toast's
        // Cancel button both feed into a single token.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = cts.Token;

        // Exchange-set entries carry an explicit ProductSpec from the
        // catalogue and never require path-based detection or recent-
        // files updates (the relative path inside a ZIP is not
        // meaningful as a recent-file entry).
        var fromExchangeSet = entry.IsFromExchangeSet;

        string? spec;
        if (fromExchangeSet)
        {
            spec = entry.ProductSpec;
        }
        else
        {
            spec = DatasetPipelineFactory.DetectProductSpec(entry.FilePath);
            if (spec is null)
            {
                _notifications.Create(Strings.Toast_Warning)
                    .WithSeverity(NotificationSeverity.Warning)
                    .WithContent(string.Format(Strings.Status_UnrecognizedFileType, Path.GetExtension(entry.FilePath)))
                    .Show();
                return;
            }
        }

        // S-104 ships a built-in portrayal catalogue.
        // S-57 datasets are portrayed with the S-101 catalogue (see SpecConventions).
        var requiredCatalogue = SpecConventions.PortrayalSpecName(spec);
        if (spec != "S-104" && !_catalogueManager.HasCatalogue(requiredCatalogue))
        {
            _notifications.Create(Strings.Toast_Warning)
                .WithSeverity(NotificationSeverity.Warning)
                .WithContent(string.Format(Strings.Status_SelectPortrayalCatalogue, requiredCatalogue))
                .Show();
            return;
        }

        // Hold a single progress notification across the whole load for
        // standalone dataset loads: indeterminate while loading with a
        // Cancel action, mutated in place to its terminal state (success /
        // cancelled / error) instead of dismissing and re-creating toasts.
        // Exchange-set entries are covered by the aggregate exchange-set
        // progress notification, so they skip the per-dataset one.
        INotificationHandle? loadNotification = null;
        if (!fromExchangeSet)
        {
            loadNotification = _notifications.Create(Strings.Toast_Loading)
                .WithSeverity(NotificationSeverity.Info)
                .WithContent(string.Format(Strings.Status_LoadingFile, entry.DisplayName))
                .AsProgress(indeterminate: true)
                .Persistent()
                .WithAction(Strings.Toast_Cancel, () => cts.Cancel(), dismissOnInvoke: false)
                .Show();
        }

        try
        {
            var processor = await Task.Run(() =>
            {
                if (!fromExchangeSet)
                    return _pipelineFactory.CreateProcessorWithFilesystemUpdates(entry.FilePath);

                // Collapse a base cell and its in-set sequential updates into a
                // single up-to-date dataset. S-101 / S-57 / S-100 Part 10a;
                // S-57 Part 3. The update-application path differs per product,
                // so dispatch on the declared spec.
                if (!entry.HasUpdates)
                    return _pipelineFactory.CreateProcessor(entry.Source!, entry.RelativePath!, spec);

                return spec switch
                {
                    "S-57" => _pipelineFactory.CreateS57ProcessorWithUpdates(
                        entry.Source!, entry.RelativePath!, entry.UpdateRelativePaths),
                    _ => _pipelineFactory.CreateS101ProcessorWithUpdates(
                        entry.Source!, entry.RelativePath!, entry.UpdateRelativePaths),
                };
            }, token);
            _processors[entry] = processor;

            // Collapse duplicate coverage products: S-111/S-104 exchange sets
            // routinely bundle several variants of the same cell (e.g. neap /
            // spring tidal regime, depth bands) — often shipped as separate
            // exchange sets, each with its own CATALOG.XML. They share the same
            // dataset name, cover the same area and time, so the purely-temporal
            // gate lets them all draw and their arrows stack on the same
            // locations — looking like several time-steps at once. Keep the
            // first-loaded variant visible and default the rest to hidden; the
            // user can re-enable any of them from the Datasets list (the row
            // dims and the eye icon reflects the hidden state).
            if (fromExchangeSet && DuplicateCoverageDetector.IsCollapsibleSpec(spec))
            {
                foreach (var other in _processors.Keys)
                {
                    if (ReferenceEquals(other, entry) || !other.IsFromExchangeSet)
                        continue;
                    if (!string.Equals(other.ProductSpec, spec, StringComparison.Ordinal))
                        continue;
                    if (DuplicateCoverageDetector.IsSameCoverage(
                            entry.RelativePath, other.RelativePath))
                    {
                        entry.IsVisible = false;
                        break;
                    }
                }
            }

            // S-104 gridded (dcf2) water-level surfaces are a synthesised,
            // non-normative colour-band heatmap (S-104 Edition 2.0.0 defines no
            // official portrayal catalogue and treats water level as ECDIS
            // vertical-adjustment input, not a chart layer). Default the surface
            // to hidden so it never dominates the display uninvited; the user
            // opts in via the eye icon in the Datasets list (issue #483).
            // Fixed-station (dcf8) glyphs are discrete symbols at genuine
            // stations and stay visible.
            // Default the surface to hidden only on the entry's first load
            // (not yet tracked in _entryOrder). On an evict → reload cycle the
            // entry is preserved in _entryOrder (see UnloadEntry), so re-hiding
            // here would silently reset a surface the user had opted into
            // (issue #483).
            if (processor is S104DatasetProcessor { IsGriddedSurface: true }
                && !_entryOrder.Contains(entry))
            {
                entry.IsVisible = false;
            }

            // Surface any S-101 update-application diagnostics. Updates are
            // applied best-effort: a partial/failed apply never blocks the
            // load, but the user is warned so stale or skipped updates are
            // visible. S-101 / S-100 Part 10a.
            if (processor is S101DatasetProcessor { UpdateReport: { } updateReport })
            {
                SurfaceUpdateReport(entry, updateReport);
            }

            // Surface S-128 catalogues into the Dataset Catalog panel.
            if (processor is S128DatasetProcessor s128)
            {
                _s128CatalogSource.AddDataset(entry.DisplayName, s128.Dataset);
            }

            // Discover time samples from the processor (S-104, S-111, S-411).
            // The adapter wraps the processor in a spec-agnostic view used
            // by the global time slider.
            var adapter = TimeAwareDatasetAdapter.TryCreate(processor, () => entry.CurrentTime);
            if (adapter is not null)
            {
                entry.AvailableTimes = adapter.AvailableTimes;
            }

            // Pick the initial render time. If the global slider already
            // has a clock, snap this dataset to it; otherwise let the
            // processor pick its default (typically the first sample).
            // A time-aware adapter that returns null for an existing clock
            // means the dataset is outside its covered window and should
            // load hidden (no arrows drawn) until the slider enters range.
            DateTime? initialTime = null;
            bool gatedHidden = false;
            if (adapter is not null && _globalTime.CurrentTime is { } globalNow)
            {
                initialTime = adapter.SnapTo(globalNow);
                gatedHidden = initialTime is null;
            }

            entry.SetVersionAssessment(processor.VersionAssessment);
            entry.CurrentTime = gatedHidden ? null : (initialTime ?? adapter?.AvailableTimes.FirstOrDefault());
            entry.SetLoadedState(processor.Metadata);

            // Snapshot the paint counter *before* the layers are swapped in,
            // so the post-load wait can confirm the map actually painted the
            // new dataset (PaintCount increased) rather than merely settling
            // on the minimum-quiet floor before any paint occurred.
            var paintsBeforeRender = _renderActivityMonitor?.PaintCount ?? 0;

            MapsuiDatasetResult? result = null;
            if (gatedHidden)
            {
                // Present but empty: registers in the panel / timeline and
                // draws nothing until a scrub brings it into range.
                await ReplaceLayersAsync(entry, processor, token).ConfigureAwait(true);
            }
            else
            {
                result = await RenderAndReplaceAsync(
                    entry,
                    processor,
                    initialTime,
                    presentation: null,
                    token).ConfigureAwait(true);
                if (result is null)
                    return;
                // Record the dataset's mercator extent so the panel can zoom to
                // it (double-click reveal) and the out-of-scale extent indicator
                // can outline it, even for exchange-set entries that opt out of
                // the auto-zoom below (issue #446).
                entry.MercatorExtent = result.Extent;
                // Exchange-set entries opt out of the per-dataset auto-zoom so
                // the union-extent zoom from `IExchangeSetService` (or the
                // user's manual Zoom-to-Extent toolbar action) wins. Without
                // this, the last-completed dataset would race with the bulk
                // load and "win" the viewport.
                if (!fromExchangeSet && !SuppressAutoZoom)
                {
                    _viewport!.ZoomToExtent(result.Extent);
                }
            }

            entry.IsLoaded = true;
            entry.Info = result?.Info;
            // Run the spec's normative validation rule pack against
            // the parsed dataset. Validation is a pure function of the
            // parsed model so we only do this once per load; ECDIS
            // / palette / time-step changes never re-run it. A null
            // return means the spec has no rule pack defined yet —
            // distinct from an empty report — and the Validation tab
            // surfaces those two states with different empty-state
            // messages.
            var validation = await Task.Run(() => SafeValidate(processor), token);
            entry.SetValidationReport(validation);

            // Hold the progress notification in its indeterminate "loading"
            // state until the map has actually painted the new dataset — not
            // merely parsed it. The layers were added (ReplaceLayers) and the
            // viewport framed (ZoomToExtent) above; both only *schedule* an
            // asynchronous paint. Waiting for the render to settle here means
            // the terminal success appears once the dataset is visible, which
            // matches the user's mental model of "loaded". Skipped for hidden
            // (out-of-range) entries that draw nothing, for exchange-set
            // entries (covered by the aggregate notification), for an already
            // dismissed/absent notification, and when no monitor is wired
            // (e.g. tests) — each has nothing to wait for.
            if (!fromExchangeSet
                && !gatedHidden
                && loadNotification is { IsDismissed: false }
                && _renderActivityMonitor is not null)
            {
                await WaitForDatasetPaintedAsync(paintsBeforeRender, token).ConfigureAwait(true);
            }

            // Drive the held progress notification to its terminal Success
            // state (auto-dismissing) instead of dismissing + re-creating a
            // separate toast. Exchange-set entries are covered by the
            // aggregate notification, so they have no per-dataset one.
            if (!fromExchangeSet)
            {
                if (!string.IsNullOrWhiteSpace(result?.Info))
                {
                    DriveTerminal(
                        loadNotification, NotificationSeverity.Success, Strings.Toast_Success, result.Info);
                }
                else
                {
                    loadNotification?.Dismiss();
                }
            }

            // Recent files only makes sense for plain file loads. An
            // exchange-set entry's FilePath is a relative path inside
            // a folder/ZIP source and not openable on its own.
            if (!fromExchangeSet)
            {
                _recentFiles.Add(entry.FilePath);
            }

            // Register with the global time service after the entry's
            // CurrentTime has been set so the first slider snap reflects
            // the actual rendered state.
            if (adapter is not null && adapter.AvailableTimes.Count > 0)
            {
                _globalTime.Register(entry, adapter);
            }

            DatasetLoaded?.Invoke(entry);
        }
        catch (OperationCanceledException)
        {
            if (!fromExchangeSet)
            {
                DriveTerminal(
                    loadNotification, NotificationSeverity.Info, Strings.Toast_DatasetCancelled, entry.DisplayName);
            }
        }
        catch (Exception ex)
        {
            if (!fromExchangeSet)
            {
                // Shape the notification around the innermost structured
                // S-100 exception (when present) so the user sees a friendly
                // one-liner instead of a raw stack trace. The full
                // ToString() is still available via the "Copy details"
                // action button; the notification stays persistent until
                // explicitly dismissed.
                var failure = LoadFailureViewModel.FromException(
                    entry.DisplayName, entry.FilePath, ex);
                var errorTitle = string.Format(Strings.Toast_DatasetErrorTitle, entry.DisplayName);
                var copyDetails = new NotificationActionDescriptor(
                    Strings.LoadFailureToast_CopyDetails,
                    () => CopyTextToClipboard(failure.Details),
                    IsPrimary: false,
                    DismissOnInvoke: false);

                if (loadNotification is not null && !loadNotification.IsDismissed)
                {
                    loadNotification.CancelAutoDismiss();
                    loadNotification.ClearProgress();
                    loadNotification.Update(
                        title: errorTitle,
                        message: failure.PrimaryMessage,
                        severity: NotificationSeverity.Error);
                    loadNotification.SetActions(copyDetails);
                }
                else
                {
                    _notifications.Create(errorTitle)
                        .WithSeverity(NotificationSeverity.Error)
                        .WithContent(failure.PrimaryMessage)
                        .Persistent()
                        .WithAction(
                            copyDetails.Label,
                            copyDetails.Invoke,
                            isPrimary: false,
                            dismissOnInvoke: false)
                        .Show();
                }
            }
        }
    }

    /// <summary>
    /// Drives a held progress notification to a non-error terminal state:
    /// clears the progress bar and any actions, applies the terminal
    /// severity/title/message, and schedules a severity-derived auto-dismiss.
    /// A no-op when no handle was created or it was already dismissed.
    /// </summary>
    private static void DriveTerminal(
        INotificationHandle? handle,
        NotificationSeverity severity,
        string title,
        string? message)
    {
        if (handle is null || handle.IsDismissed)
            return;

        handle.ClearProgress();
        handle.SetActions();
        handle.Update(title: title, message: message, severity: severity);
        handle.ScheduleAutoDismiss(NotificationService.DefaultDelayFor(severity));
    }

    /// <summary>
    /// Blocks until the live map has actually painted the newly loaded
    /// dataset, so the terminal "loaded" notification never precedes the
    /// chart becoming visible. <see cref="IRenderActivityMonitor.WaitForIdleAsync"/>
    /// only guarantees a <em>minimum</em> quiet wait — it can report idle on
    /// the floor before the dataset's first paint lands (or the paint may
    /// occur during the preceding validation await, before the wait even
    /// begins). This method therefore gates on the monotonic
    /// <see cref="IRenderActivityMonitor.PaintCount"/> rising above the value
    /// captured before the layers were swapped in, then lets the view settle.
    /// </summary>
    /// <param name="paintsBeforeRender">
    /// The monitor's <see cref="IRenderActivityMonitor.PaintCount"/> sampled
    /// immediately before the new layers were added to the map.
    /// </param>
    /// <param name="token">Cancels the wait when the load is cancelled.</param>
    private async Task WaitForDatasetPaintedAsync(long paintsBeforeRender, CancellationToken token)
    {
        var monitor = _renderActivityMonitor;
        if (monitor is null)
            return;

        var quiet = TimeSpan.FromMilliseconds(150);
        var deadline = Environment.TickCount64 + (long)TimeSpan.FromSeconds(5).TotalMilliseconds;

        // Phase 1: ensure at least one paint has landed since the layer swap.
        // If the paint already occurred (e.g. during the validation await),
        // PaintCount is already ahead and this loop is skipped entirely.
        while (monitor.PaintCount <= paintsBeforeRender)
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
                return;

            var result = await monitor
                .WaitForIdleAsync(quiet, TimeSpan.FromMilliseconds(remaining), token)
                .ConfigureAwait(true);

            // A paint landed during the wait — the dataset is on screen.
            if (result.PaintsObserved > 0)
                break;

            // The map settled with no paint and the budget elapsed: give up
            // waiting rather than hold the progress notification indefinitely.
            if (result.TimedOut)
                return;
        }

        // Phase 2: let any fetch-driven follow-up repaints settle so the
        // success appears once the view is stable, not mid-paint.
        var settleBudget = deadline - Environment.TickCount64;
        if (settleBudget > 0)
        {
            await monitor
                .WaitForIdleAsync(quiet, TimeSpan.FromMilliseconds(settleBudget), token)
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Copies <paramref name="text"/> to the system clipboard via the
    /// active main window. Used by the load-failure toast's
    /// "Copy details" action. Best-effort: any failure is swallowed so
    /// a flaky clipboard backend never crashes the dataset open path.
    /// </summary>
    private static void CopyTextToClipboard(string text)
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { } mainWindow
                && Avalonia.Controls.TopLevel.GetTopLevel(mainWindow)?.Clipboard is { } clipboard)
            {
                _ = clipboard.SetTextAsync(text);
            }
        }
        catch
        {
            // Best-effort; clipboard access can fail on some Linux WMs.
        }
    }

    public async Task ReRenderAtTimeAsync(DateTime t, CancellationToken cancellationToken)
    {
        using var __cmd = ViewerObservability.BeginCommand("timeline.scrub");

        // Cancel any in-flight scrub render and start a fresh debounce
        // window. The token passed in is honoured in addition to the
        // internal debounce token so callers can cancel from outside
        // (e.g. on shutdown).
        _scrubCts?.Cancel();
        var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _scrubCts = localCts;
        var token = localCts.Token;

        try
        {
            await Task.Delay(ScrubDebounceWindow, token).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        foreach (var (entry, adapter) in _globalTime.Adapters.ToArray())
        {
            if (token.IsCancellationRequested) return;
            if (!_processors.TryGetValue(entry, out var proc)) continue;

            var snapped = adapter.SnapTo(t);
            if (snapped == entry.CurrentTime && entry.IsLoaded) continue;

            try
            {
                if (snapped is null)
                {
                    // Out of covered range: hide the dataset (drop its
                    // layers) rather than draw stale endpoint-clamped
                    // arrows. Cheap when already hidden.
                    if (_entryLayers.TryGetValue(entry, out var current) && current.Count > 0)
                    {
                        await ReplaceLayersAsync(
                            entry,
                            proc,
                            token,
                            () => entry.CurrentTime = null).ConfigureAwait(true);
                    }
                    else
                    {
                        entry.CurrentTime = null;
                    }
                    continue;
                }

                await RenderAndReplaceAsync(
                    entry,
                    proc,
                    snapped,
                    presentation: null,
                    token,
                    result =>
                    {
                        entry.Info = result.Info;
                        entry.CurrentTime = snapped;
                    }).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to re-render {entry.FilePath} at {t:u}:\n{ex}");
            }
        }
    }

    /// <inheritdoc />
    public async Task SetPresentationAsync(
        MapPresentationState presentation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        cancellationToken.ThrowIfCancellationRequested();

        CancellationTokenSource localCts;
        lock (_presentationSync)
        {
            _presentationCts?.Cancel();
            localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _presentationCts = localCts;
        }

        var enteredGate = false;
        try
        {
            await _layerRenderGate.WaitAsync(localCts.Token).ConfigureAwait(true);
            enteredGate = true;
            localCts.Token.ThrowIfCancellationRequested();
            Volatile.Write(ref _presentation, presentation);
            await ReRenderAllAsync(presentation, localCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A newer presentation superseded this application.
        }
        finally
        {
            if (enteredGate)
                _layerRenderGate.Release();

            lock (_presentationSync)
            {
                if (ReferenceEquals(_presentationCts, localCts))
                    _presentationCts = null;
            }
            localCts.Dispose();
        }
    }

    private async Task ReRenderAllAsync(
        MapPresentationState presentation,
        CancellationToken cancellationToken)
    {
        using var __cmd = ViewerObservability.BeginCommand("presentation.apply");

        var palette = presentation.Palette;

        foreach (var (entry, proc) in _processors.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.IsLoaded) continue;
            if (!OwnsProcessor(entry, proc)) continue;

            // Keep time-gated datasets hidden across palette / display
            // re-renders: if the entry's adapter snaps the current global
            // time to null it is outside its covered window and must not
            // be re-materialized here.
            if (_globalTime.Adapters.TryGetValue(entry, out var gateAdapter)
                && _globalTime.CurrentTime is { } gateNow
                && gateAdapter.SnapTo(gateNow) is null)
            {
                if (_entryLayers.TryGetValue(entry, out var cur) && cur.Count > 0)
                    ReplaceLayers(entry, Array.Empty<ILayer>(), null, null);
                continue;
            }

            try
            {
                await RenderAndReplaceCoreAsync(
                    entry,
                    proc,
                    entry.CurrentTime,
                    presentation,
                    cancellationToken,
                    result => entry.Info = result.Info).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to re-render {entry.FilePath} with {palette} palette:\n{ex}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        _notifications.Create(Strings.Toast_Success)
            .WithSeverity(NotificationSeverity.Success)
            .WithContent(Strings.Toast_SettingsApplied)
            .Show();
    }

    private async Task<MapsuiDatasetResult?> RenderAndReplaceAsync(
        DatasetEntry entry,
        IDatasetProcessor processor,
        DateTime? timeStep,
        MapPresentationState? presentation,
        CancellationToken cancellationToken,
        Action<MapsuiDatasetResult>? onApplied = null)
    {
        await _layerRenderGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            return await RenderAndReplaceCoreAsync(
                entry,
                processor,
                timeStep,
                presentation,
                cancellationToken,
                onApplied).ConfigureAwait(true);
        }
        finally
        {
            _layerRenderGate.Release();
        }
    }

    private async Task ReplaceLayersAsync(
        DatasetEntry entry,
        IDatasetProcessor processor,
        CancellationToken cancellationToken,
        Action? onApplied = null)
    {
        await _layerRenderGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OwnsProcessor(entry, processor))
                return;

            ReplaceLayers(entry, Array.Empty<ILayer>(), null, null);
            onApplied?.Invoke();
        }
        finally
        {
            _layerRenderGate.Release();
        }
    }

    private async Task<MapsuiDatasetResult?> RenderAndReplaceCoreAsync(
        DatasetEntry entry,
        IDatasetProcessor processor,
        DateTime? timeStep,
        MapPresentationState? presentation,
        CancellationToken cancellationToken,
        Action<MapsuiDatasetResult>? onApplied = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OwnsProcessor(entry, processor))
            return null;

        var context = CreateRenderContext(processor, timeStep, presentation);
        var result = await Task.Run(
            () => _mapsuiRenderer.RenderAsync(processor, context, cancellationToken),
            cancellationToken).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        if (!OwnsProcessor(entry, processor))
            return null;

        ReplaceLayers(
            entry,
            result.Layers.ToList(),
            result.LayerNames,
            result.StackEntries,
            result.CellMinimumDisplayScale,
            result.CoverageGeometry);
        onApplied?.Invoke(result);
        return result;
    }

    private bool OwnsProcessor(DatasetEntry entry, IDatasetProcessor processor) =>
        _processors.TryGetValue(entry, out var current)
        && ReferenceEquals(current, processor);

    public void RemoveEntry(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        RemoveEntryLayers(entry);
        UnsubscribeSubLayers(entry);
        entry.SubLayers.Clear();
        _entryLayerKeys.Remove(entry);
        _entryStackEntries.Remove(entry);
        _entryCoverage.Remove(entry);
        if (_subscribedEntries.Remove(entry))
            entry.PropertyChanged -= OnEntryPropertyChanged;
        if (_processors.Remove(entry, out var removedProcessor)
            && removedProcessor is IDisposable disposableProcessor)
        {
            // Releases any file/stream a processor keeps open for lazy reads
            // (e.g. S-111 dcf2 retains its HDF5 file for deferred time-step
            // value decoding).
            disposableProcessor.Dispose();
        }
        _entryOrder.Remove(entry);
        _globalTime.Unregister(entry);
        _s128CatalogSource.RemoveDataset(entry.DisplayName);
        // Publish the new (empty / smaller) stack so PickService and
        // anyone else who cares drops references to the removed layers.
        if (_layerCollection is not null)
            _layerCollection.ReplaceDatasetLayers(FlattenLayerOrder());
        // Recompute overlap-suppression clips now this cell's coverage is gone,
        // so a coarser cell it used to suppress paints in full again (#438 Ph2).
        ApplyOverlapSuppression();
        DatasetRemoved?.Invoke(entry);
    }

    /// <summary>
    /// Unloads a lazily-loaded exchange-set cell's <em>bytes</em> (layers,
    /// sub-layers, and processor) while leaving the <see cref="DatasetEntry"/>
    /// registered in the Datasets panel, and marks it
    /// <see cref="DatasetEntry.IsDeferred"/> again so it reverts to an extent
    /// outline that can be reloaded when it next enters the viewport. This is
    /// the LRU-eviction counterpart to <see cref="LoadAsync"/>; unlike
    /// <see cref="RemoveEntry"/> it does not fire <see cref="DatasetRemoved"/>
    /// or drop the entry from the collection. No-op for an entry that owns no
    /// layers. See issue #458.
    /// </summary>
    /// <param name="entry">The exchange-set cell entry to unload.</param>
    public void UnloadEntry(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        RemoveEntryLayers(entry);
        UnsubscribeSubLayers(entry);
        entry.SubLayers.Clear();
        _entryLayerKeys.Remove(entry);
        _entryStackEntries.Remove(entry);
        _entryCoverage.Remove(entry);
        if (_subscribedEntries.Remove(entry))
            entry.PropertyChanged -= OnEntryPropertyChanged;
        if (_processors.Remove(entry, out var removedProcessor)
            && removedProcessor is IDisposable disposableProcessor)
        {
            disposableProcessor.Dispose();
        }
        // NB: unlike RemoveEntry, do NOT drop the entry from _entryOrder here.
        // Eviction keeps the DatasetEntry registered; FlattenLayerOrder already
        // skips entries with no layers (RemoveEntryLayers cleared _entryLayers
        // above), so its slot is inert while unloaded but preserved. Removing it
        // would make the next reload look like a first load and re-insert it at
        // index 0, reshuffling cross-dataset paint order on every evict/reload
        // cycle. See issue #458.
        // Active state lives on the entry's renderer-neutral MapDataset
        // projection, so it naturally survives this unload → reload cycle.
        _globalTime.Unregister(entry);
        entry.IsDeferred = true;
        if (_layerCollection is not null)
            _layerCollection.ReplaceDatasetLayers(FlattenLayerOrder());
        // Recompute overlap-suppression clips now this cell's coverage is gone
        // (evicted), so any coarser cell it suppressed paints in full (#438 Ph2).
        ApplyOverlapSuppression();
    }

    public void SetEntryOrder(IReadOnlyList<DatasetEntry> orderedEntries)
    {
        ArgumentNullException.ThrowIfNull(orderedEntries);
        if (_layerCollection is null) return;

        // Rebuild the canonical order from the supplied sequence,
        // dropping any entries that are no longer bound to layers
        // (e.g. removed concurrently).
        _entryOrder.Clear();
        foreach (var e in orderedEntries)
        {
            if (_entryLayers.ContainsKey(e))
                _entryOrder.Add(e);
        }
        _layerCollection.ReplaceDatasetLayers(FlattenLayerOrder());
    }

    private List<ILayer> FlattenLayerOrder()
    {
        // PR-L1 (S-98): defer the cross-dataset paint order to the
        // S-98 interoperability authority. _entryOrder is top-of-UI
        // first (mirrors the Datasets panel); the authority sorts
        // by S-98 display plane (BaseChartUnder → EcdisAlerts) and
        // uses input order as the final tiebreaker. We feed it
        // bottom-of-UI first so the topmost-UI dataset wins ties
        // (and lands at the highest layer index — drawn last, on
        // top), preserving the prior behaviour for single-plane
        // dataset stacks.
        //
        // Issue #398: the S-98 engine now operates on renderer-neutral
        // SubLayerStackItem values (in the Mapsui-free Datasets.Pipelines
        // assembly). We feed it each dataset's items, then project the
        // ordered / suppressed result back onto the prebuilt Mapsui
        // layers via LayerStackProjector (reusing cached ILayers and
        // filtering only suppressed features — no re-rasterisation).
        //
        // PR-L3: we keep building the FULL plane-sorted list of
        // entries (including inactive datasets) so the Layer Stack
        // panel can still show their rows and let the user re-enable
        // them. Only the rendered layer list (returned to the map
        // host) is filtered to active entries; the snapshot stored
        // in <see cref="_currentStackEntries"/> retains every entry.
        var perDataset = new List<IReadOnlyList<SubLayerStackItem>>(_entryOrder.Count);
        var prebuilt = new Dictionary<(string DatasetId, string LayerKey), LayerStackEntry>();
        for (int i = 0; i < _entryOrder.Count; i++)
        {
            var entry = _entryOrder[i];
            if (!_entryLayers.TryGetValue(entry, out var layers)) continue;

            var datasetId = entry.Id.Value;

            if (_entryStackEntries.TryGetValue(entry, out var stack) && stack.Count > 0)
            {
                var items = new List<SubLayerStackItem>(stack.Count);
                foreach (var se in stack)
                {
                    items.Add(se.Item);
                    prebuilt[LayerStackProjector.KeyOf(se.Item)] = se;
                }
                perDataset.Add(items);
            }
            else
            {
                // Fallback: processor didn't supply StackEntries. Drop
                // each layer onto the spec's default plane with
                // priority 0 so it still participates in S-98 ordering.
                var specName = _processors.TryGetValue(entry, out var proc)
                    ? proc.Spec.Name
                    : "unknown";
                var plane = _authorityProvider.Current.GetDefaultPlane(specName);
                var synth = new List<SubLayerStackItem>(layers.Count);
                for (int li = 0; li < layers.Count; li++)
                {
                    // Synthesise a stable key so the projector can recover the
                    // layer; there is no portrayal payload for these legacy
                    // fallbacks so we key by dataset + ordinal.
                    var layerKey = string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"__synth__{li}");
                    var item = new SubLayerStackItem(
                        new SyntheticStackPayload(layerKey),
                        plane,
                        WithinPlanePriority: 0,
                        SourceDatasetId: datasetId);
                    synth.Add(item);
                    prebuilt[(datasetId, layerKey)] = new LayerStackEntry(layers[li], item);
                }
                perDataset.Add(synth);
            }
        }

        var authority = _authorityProvider.Current;
        var sorted = LayerStackBuilder.Build(authority, perDataset);

        // PR-L2: apply S-98 inter-product rules (suppression, etc.)
        // after the per-plane sort. The rule set is the default
        // S98DefaultRules collection; rules read the mariner settings
        // (e.g. SafetyContour for R-101-102-B's safety-contour
        // exception per MSC.232(82) §5.8). LoadOrderInteroperabilityAuthority
        // explicitly no-ops ApplyRules so the strict load-order mode
        // is unaffected.
        var loaded = BuildLoadedDatasetInfos();
        var ruled = authority.ApplyRules(sorted, loaded, CurrentPresentation.Mariner);

        // Project the ordered / suppressed neutral items back onto the
        // prebuilt Mapsui layers. Cached ILayers are reused where the S-98
        // outcome is unchanged; the BuildGridCoverageLayer callback lets the
        // projector rebuild a grid-coverage raster when a rule changed its
        // payload (e.g. S-104 land clipping), so this path is no longer always
        // rasterisation-free. Cache the FULL projected list (including inactive
        // datasets) for the Layer Stack panel.
        var projected = LayerStackProjector.Project(ruled, prebuilt, _mapsuiRenderer.BuildGridCoverageLayer);
        _currentStackEntries = projected;

        // PR-L3: filter inactive datasets out of the rendered layer
        // list handed back to the map host. The active flag is the
        // single source of truth: inactive entries don't paint and
        // don't influence pick.
        var renderEntries = new List<LayerStackEntry>(projected.Count);
        foreach (var e in projected)
        {
            if (!GetActive(e.SourceDatasetId)) continue;
            renderEntries.Add(e);
        }

        var list = LayerStackProjector.ToLayerList(renderEntries);
        _currentStackedLayers = list;
        LayerStackChanged?.Invoke();
        return list;
    }

    /// <summary>
    /// Builds the snapshot of <see cref="LoadedDatasetInfo"/> values
    /// the S-98 rule engine consumes. <c>Active</c> combines the
    /// PR-L3 in-memory flag, the existing <c>DatasetEntry.IsVisible</c>
    /// proxy, and a "did the processor actually produce layers?"
    /// check so a failed render doesn't accidentally suppress
    /// sibling products.
    /// </summary>
    private IReadOnlyList<LoadedDatasetInfo> BuildLoadedDatasetInfos()
    {
        var result = new List<LoadedDatasetInfo>(_entryOrder.Count);
        foreach (var entry in _entryOrder)
        {
            if (!_processors.TryGetValue(entry, out var proc)) continue;
            var dataset = entry.MapDataset;
            if (dataset is null) continue;
            var active = dataset.IsActive
                && dataset.IsVisible
                && _entryLayers.TryGetValue(entry, out var layers)
                && layers.Count > 0;
            result.Add(new LoadedDatasetInfo(dataset.Id.Value, dataset.Metadata.Spec.Name, active));
        }
        return result;
    }

    /// <summary>
    /// Runs the processor's spec-specific validation rule pack and
    /// swallows any exception so a buggy rule cannot abort a dataset
    /// load. Returns the report on success, the processor's null on
    /// "no rule pack for this spec", or null on exception.
    /// </summary>
    private static EncDotNet.S100.Validation.ValidationReport? SafeValidate(IDatasetProcessor processor)
    {
        try
        {
            return processor.Validate();
        }
        catch (Exception ex)
        {
            // Defensive — individual rule failures are already
            // captured as synthetic Error findings by ValidationRuleSet.
            // This catches the unlikely case where projection or rule
            // pack construction itself throws.
            System.Diagnostics.Debug.WriteLine($"[validation] {processor.Spec.Name}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private RenderContext CreateRenderContext(
        IDatasetProcessor processor,
        DateTime? timeStep = null,
        MapPresentationState? presentation = null)
    {
        RenderContext context = processor switch
        {
            S104DatasetProcessor when timeStep is not null
                => new S104RenderContext(timeStep),
            S104DatasetProcessor
                => new S104RenderContext(),
            S111DatasetProcessor when timeStep is not null
                => new S111RenderContext(timeStep),
            S111DatasetProcessor
                => new S111RenderContext(),
            S101DatasetProcessor
                => new S101RenderContext(),
            S102DatasetProcessor
                => new S102RenderContext(),
            S122DatasetProcessor
                => new S122RenderContext(),
            S124DatasetProcessor
                => new S124RenderContext(),
            S125DatasetProcessor
                => new S125RenderContext(),
            S201DatasetProcessor
                => new S201RenderContext(),
            S127DatasetProcessor
                => new S127RenderContext(),
            S129DatasetProcessor
                => new S129RenderContext(),
            S411DatasetProcessor when timeStep is not null
                => new S411RenderContext(timeStep),
            S411DatasetProcessor
                => new S411RenderContext(),
            _ => new S101RenderContext(),
        };

        return (presentation ?? CurrentPresentation).ApplyTo(context, processor.PortrayalSpec);
    }

    private void ReplaceLayers(
        DatasetEntry entry,
        IReadOnlyList<ILayer> layers,
        IReadOnlyList<string>? layerKeys,
        IReadOnlyList<LayerStackEntry>? stackEntries,
        int? cellMinimumDisplayScale = null,
        NetTopologySuite.Geometries.Geometry? coverageGeometry = null)
    {
        bool isFirstLoad = !_entryOrder.Contains(entry);

        RemoveEntryLayers(entry);
        _entryLayers[entry] = layers;
        _entryLayerKeys[entry] = layerKeys;

        // Record this cell's coverage footprint + scale band for cross-cell
        // overlap suppression (issue #438 Phase 2). The suppression band prefers
        // the exchange-set catalogue scale, falling back to the processor-derived
        // cell scale (same precedence as the Phase 1 zoom-out window below).
        var suppressionScale = entry.MinimumDisplayScale ?? cellMinimumDisplayScale;
        if (coverageGeometry is { IsEmpty: false } && suppressionScale is int band)
            _entryCoverage[entry] = (coverageGeometry, band);
        else
            _entryCoverage.Remove(entry);
        // Keep _entryStackEntries in sync with _entryLayers. If the
        // processor didn't supply StackEntries, FlattenLayerOrder will
        // synthesise defaults below — but we still clear any stale
        // entries from a previous render so they don't leak.
        if (stackEntries is not null && stackEntries.Count > 0)
            _entryStackEntries[entry] = stackEntries;
        else
            _entryStackEntries.Remove(entry);

        // Reconcile sub-layers (don't replace) so existing per-sub-layer
        // visibility / opacity choices survive palette switches and
        // time-scrub re-renders. Sub-layers are matched by stable key.
        ReconcileSubLayers(entry, layerKeys);

        // Re-apply effective display state (parent + sub-layer combined)
        // to the freshly-produced layers. Each ReplaceLayers call creates
        // new ILayer instances that default to Enabled=true / Opacity=1
        // — without this step those defaults silently win.
        ApplyDisplayState(entry);

        // Hole-safe per-cell zoom-out visibility window (issue #438 Phase 1):
        // clamp each layer's MaxVisible to the cell's coarsest intended scale
        // so finer nested cells drop out first as the viewport zooms out,
        // leaving the coarser cell underneath. Re-applied on every render
        // because each build produces fresh ILayer instances.
        ApplyCellScaleWindow(entry, layers, cellMinimumDisplayScale);

        // Subscribe lazily on first ReplaceLayers so that property
        // changes raised by the UI propagate to the live ILayer
        // instances. The subscription persists across re-renders.
        if (_subscribedEntries.Add(entry))
            entry.PropertyChanged += OnEntryPropertyChanged;

        foreach (var layer in layers)
        {
            _layerCollection!.AddDatasetLayer(layer);
        }

        // PR-L1 (S-98): always recompute the cross-dataset paint
        // order after a load/re-render. The S-98 plane sort can
        // place a newly-loaded dataset *under* existing layers
        // (e.g. an S-102 bathymetry load arrives after S-101 line
        // work — the bathy must sit between the ENC's area fills
        // and its line work). Pre-PR-L1 we only re-shuffled on
        // re-renders; that was correct for the old "load order
        // wins" model.
        if (isFirstLoad)
        {
            _entryOrder.Insert(0, entry);
        }
        _layerCollection!.ReplaceDatasetLayers(FlattenLayerOrder());

        // Cross-cell scale-band overlap suppression (issue #438 Phase 2):
        // recompute every loaded cell's clip region now that this cell's layers
        // (and coverage) have changed, so a coarser cell stops drawing where a
        // finer overlapping cell provides coverage.
        ApplyOverlapSuppression();
    }

    /// <summary>
    /// Recomputes and attaches cross-cell scale-band overlap-suppression clip
    /// regions (issue #438 Phase 2) across every loaded cell: each coarser cell
    /// is clipped to its data coverage minus the union of finer, overlapping
    /// in-band cells' coverage. Skipped (and all clips cleared) when the mariner
    /// has opted to ignore scale minima — consistent with the Phase 1 zoom-out
    /// window (<see cref="ApplyCellScaleWindow"/>) — so an override still shows
    /// every cell in full. Recomputed on every load / unload / re-render because
    /// each build produces fresh <see cref="ILayer"/> instances and the finer/
    /// coarser overlap set changes as cells come and go, and on every
    /// visibility/opacity change (a cell that is not currently drawing is
    /// excluded as a suppressor so hiding a finer cell does not leave a hole).
    /// </summary>
    private void ApplyOverlapSuppression()
    {
        var cells = new List<OverlapSuppressionCell>(_entryLayers.Count);
        foreach (var (entry, layers) in _entryLayers)
        {
            if (layers.Count == 0)
                continue;

            // A cell that is not currently drawing (parent hidden, opacity 0, or
            // all its sub-layers toggled off) must not suppress coarser cells —
            // otherwise hiding a finer cell would leave the "blank hole" its own
            // content used to fill. ApplyDisplayState (run before this on load,
            // and on every visibility/opacity change) has already folded the
            // composed state into each layer's Enabled/Opacity, so a cell is
            // drawing iff any of its layers is enabled with non-zero opacity.
            // Non-drawing entries stay in the set with null Coverage so their own
            // clip attachments are cleared (they paint in full when re-shown).
            var isDrawing = false;
            foreach (var layer in layers)
            {
                if (layer.Enabled && layer.Opacity > 0)
                {
                    isDrawing = true;
                    break;
                }
            }

            _entryCoverage.TryGetValue(entry, out var coverage);
            var effectiveCoverage = isDrawing ? coverage.Coverage : null;
            cells.Add(new OverlapSuppressionCell
            {
                Layers = layers,
                Coverage = effectiveCoverage,
                ScaleDenominator = effectiveCoverage is null ? null : coverage.ScaleDenominator,
            });
        }

        if (cells.Count == 0)
            return;

        if (CurrentPresentation.Mariner.IgnoreScaleMinimum)
            OverlapSuppression.ClearAll(cells);
        else
            OverlapSuppression.Apply(cells);
    }

    /// <inheritdoc />
    public IReadOnlyList<OverscaleCellInput> GetOverscaleCells()
    {
        List<OverscaleCellInput>? cells = null;
        foreach (var (entry, layers) in _entryLayers)
        {
            if (layers.Count == 0)
                continue;

            // The cell's compilation (finest) scale — the denominator past which
            // zooming in is overscale (S-101 FC §3.1.1 maximumDisplayScale).
            if (entry.MaximumDisplayScale is not int compilationScale || compilationScale <= 0)
                continue;

            // Only cells that are actually drawing contribute an indication (a
            // hidden cell isn't being overscaled on screen). Same drawing test
            // as ApplyOverlapSuppression.
            var isDrawing = false;
            foreach (var layer in layers)
            {
                if (layer.Enabled && layer.Opacity > 0)
                {
                    isDrawing = true;
                    break;
                }
            }

            if (!isDrawing)
                continue;

            if (!_entryCoverage.TryGetValue(entry, out var coverage)
                || coverage.Coverage is not { IsEmpty: false })
                continue;

            (cells ??= []).Add(new OverscaleCellInput
            {
                Name = entry.DisplayName,
                Coverage = coverage.Coverage,
                CompilationScaleDenominator = compilationScale,
            });
        }

        return (IReadOnlyList<OverscaleCellInput>?)cells ?? [];
    }

    /// <summary>
    /// Applies the hole-safe per-cell zoom-out visibility window (issue #438
    /// Phase 1) to <paramref name="entry"/>'s freshly-built layers when the
    /// exchange-set catalogue supplied a coarsest display scale
    /// (<see cref="DatasetEntry.MinimumDisplayScale"/>). Skipped when the
    /// mariner has opted to ignore scale minima (consistent with the S-101
    /// in-file out-of-scale-band cap), so a mariner override still shows every
    /// cell at all zooms. Toggling the setting re-renders (which calls back
    /// into <see cref="ReplaceLayers"/>), so the window is re-evaluated.
    /// </summary>
    private void ApplyCellScaleWindow(
        DatasetEntry entry,
        IReadOnlyList<ILayer> layers,
        int? cellMinimumDisplayScale = null)
    {
        // Default to "never disappears" until we confirm a window was applied.
        entry.ContentMaxVisibleResolution = null;

        if (layers.Count == 0)
            return;
        // Prefer the exchange-set catalogue value (DatasetEntry.MinimumDisplayScale);
        // otherwise fall back to the scale the processor derived from the
        // dataset's own content (S-101 in-file DataCoverage.minimumDisplayScale,
        // S-57 DSPM compilation scale). This makes a standalone-loaded cell hide
        // when zoomed out just as it would when loaded from an exchange set.
        if ((entry.MinimumDisplayScale ?? cellMinimumDisplayScale) is not int minimumDisplayScale)
            return;
        if (CurrentPresentation.Mariner.IgnoreScaleMinimum)
            return;

        MapsuiDatasetRenderer.ApplyCellScaleWindow(layers, minimumDisplayScale);

        // Record the whole-cell zoom-out cutoff so the out-of-scale extent
        // indicator (issue #446) knows the resolution at which this dataset
        // fully drops out. A dataset is visible while at least one layer draws
        // (resolution <= its MaxVisible), so the cutoff is the largest finite
        // MaxVisible across the clamped layers — the last layer to vanish.
        double cutoff = 0.0;
        foreach (var layer in layers)
        {
            if (layer is BaseLayer baseLayer && baseLayer.MaxVisible < double.MaxValue)
                cutoff = Math.Max(cutoff, baseLayer.MaxVisible);
        }

        entry.ContentMaxVisibleResolution = cutoff > 0.0 ? cutoff : null;
    }

    /// <summary>
    /// Brings <see cref="DatasetEntry.SubLayers"/> in line with the
    /// processor's freshly-emitted layer keys. Existing
    /// <see cref="DatasetSubLayer"/> instances are reused (matched by
    /// <see cref="DatasetSubLayer.Key"/>) so user toggles survive
    /// re-renders. Single-layer datasets have an empty SubLayers
    /// collection, which the UI treats as "no disclosure".
    /// </summary>
    private void ReconcileSubLayers(DatasetEntry entry, IReadOnlyList<string>? layerKeys)
    {
        // Single-layer datasets: clear any (stale) sub-layers and bail.
        if (layerKeys is null || layerKeys.Count <= 1)
        {
            if (entry.SubLayers.Count > 0)
            {
                UnsubscribeSubLayers(entry);
                entry.SubLayers.Clear();
            }
            return;
        }

        var existing = entry.SubLayers.ToDictionary(s => s.Key, s => s);
        var seen = new HashSet<string>();
        var orderedNew = new List<DatasetSubLayer>(layerKeys.Count);
        foreach (var key in layerKeys)
        {
            // Suffix-resolve duplicate keys defensively (the contract
            // expects unique keys; this just keeps a runtime collision
            // from corrupting the SubLayers collection).
            var k = key;
            int n = 1;
            while (!seen.Add(k))
            {
                k = $"{key}#{++n}";
            }

            if (existing.TryGetValue(k, out var sub))
            {
                orderedNew.Add(sub);
            }
            else
            {
                var displayName = ResolveSubLayerDisplayName(k);
                sub = new DatasetSubLayer(k, displayName);
                sub.PropertyChanged += OnSubLayerPropertyChanged;
                orderedNew.Add(sub);
            }
        }

        // Drop sub-layers that no longer correspond to any emitted
        // layer (e.g. processor changed shape between renders).
        foreach (var stale in existing.Values.Where(s => !seen.Contains(s.Key)))
        {
            stale.PropertyChanged -= OnSubLayerPropertyChanged;
        }

        entry.SubLayers.Clear();
        foreach (var sub in orderedNew) entry.SubLayers.Add(sub);
    }

    private void UnsubscribeSubLayers(DatasetEntry entry)
    {
        foreach (var s in entry.SubLayers)
            s.PropertyChanged -= OnSubLayerPropertyChanged;
    }

    /// <summary>
    /// Maps stable processor-supplied sub-layer keys to localized
    /// display names. Unknown keys fall back to the key itself so a
    /// new processor that forgets to add a translation still shows
    /// something readable.
    /// </summary>
    private static string ResolveSubLayerDisplayName(string key) => key switch
    {
        "s111.arrows" => Strings.SubLayer_S111_Arrows,
        _ => key,
    };

    private void ApplyDisplayState(DatasetEntry entry)
    {
        if (!_entryLayers.TryGetValue(entry, out var layers)) return;

        // When the processor emitted sub-layer keys, fold the per-
        // sub-layer state into the per-layer Enabled/Opacity values.
        // Otherwise apply parent state uniformly.
        _entryLayerKeys.TryGetValue(entry, out var keys);

        var subLayerLookup = entry.SubLayers.Count > 0
            ? entry.SubLayers.ToDictionary(s => s.Key, s => s)
            : null;

        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            DatasetSubLayer? sub = null;
            if (subLayerLookup is not null && keys is not null && i < keys.Count)
            {
                subLayerLookup.TryGetValue(keys[i], out sub);
            }

            // AND visibility, multiply opacity. (Mapsui has a single
            // scalar opacity per layer, so multiplication is the
            // canonical way to express parent×sub.)
            layer.Enabled = entry.IsVisible && (sub?.IsVisible ?? true);
            layer.Opacity = entry.Opacity * (sub?.Opacity ?? 1.0);
        }
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DatasetEntry entry) return;
        if (e.PropertyName is not (nameof(DatasetEntry.IsVisible) or nameof(DatasetEntry.Opacity)))
            return;
        ApplyDisplayState(entry);
        // Visibility/opacity feeds the suppression set (a hidden finer cell must
        // stop clipping coarser cells), so keep the clip attachments in sync.
        ApplyOverlapSuppression();
    }

    private void OnSubLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DatasetSubLayer sub) return;
        if (e.PropertyName is not (nameof(DatasetSubLayer.IsVisible) or nameof(DatasetSubLayer.Opacity)))
            return;

        // The sub-layer doesn't know its parent; find it by membership.
        // The cost is bounded by the number of loaded datasets which is
        // always small for an interactive viewer.
        foreach (var (entry, _) in _entryLayers)
        {
            if (entry.SubLayers.Contains(sub))
            {
                ApplyDisplayState(entry);
                // Toggling all of a cell's sub-layers off makes it non-drawing,
                // so refresh suppression to release any clip it imposed.
                ApplyOverlapSuppression();
                break;
            }
        }
    }

    private DatasetEntry? FindEntry(string datasetId)
    {
        var loadedEntry = _processors.Keys.FirstOrDefault(entry =>
            string.Equals(entry.Id.Value, datasetId, StringComparison.Ordinal));
        return loadedEntry ?? _entryOrder.FirstOrDefault(entry =>
            string.Equals(entry.Id.Value, datasetId, StringComparison.Ordinal));
    }

    private void RemoveEntryLayers(DatasetEntry entry)
    {
        if (_layerCollection is null)
            return;

        if (_entryLayers.TryGetValue(entry, out var oldLayers))
        {
            foreach (var layer in oldLayers)
            {
                _layerCollection.RemoveDatasetLayer(layer);
            }
            _entryLayers.Remove(entry);
        }
    }

    private void OnAuthorityChanged()
    {
        // The host swapped the active interoperability authority
        // (e.g. flipped a viewer setting between S-98 and load-order).
        // Re-flatten the current stack through the new authority's
        // policy and push the result to the map host. Cheap when no
        // datasets are loaded.
        if (_layerCollection is null) return;
        if (_entryOrder.Count == 0) return;
        _layerCollection.ReplaceDatasetLayers(FlattenLayerOrder());
    }

    private void EnsureInitialized()
    {
        if (_layerCollection is null || _viewport is null)
            throw new InvalidOperationException("DatasetLoaderService.Initialize must be called before LoadAsync.");
    }
}
