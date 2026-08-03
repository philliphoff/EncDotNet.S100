using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Input.Platform;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S101;
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
/// Default <see cref="IDatasetLoaderService"/> implementation. Coordinates
/// Viewer load policy and layer rendering while processor lifetime is delegated
/// to <see cref="DatasetProcessorOwner"/>.
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
    private readonly DatasetProcessorOwner _processorOwner;
    /// <summary>
    /// Lets a standalone dataset load hold its progress notification in the
    /// "loading" state until the map has actually painted the new dataset,
    /// so the terminal success only appears once the data is visible rather
    /// than merely parsed. Optional: <see langword="null"/> (e.g. in tests,
    /// or before the map control exists) disables the wait.
    /// </summary>
    private readonly IRenderActivityMonitor? _renderActivityMonitor;

    private readonly Dictionary<MapDatasetId, DatasetEntry> _processorEntries = [];
    private readonly Dictionary<MapDatasetId, DatasetEntry> _sessionEntries = [];
    private readonly ConditionalWeakTable<DatasetEntry, LoadGeneration> _loadGenerations = new();
    private readonly ConditionalWeakTable<DatasetEntry, SemaphoreSlim> _loadGates = new();
    private readonly HashSet<DatasetEntry> _subscribedEntries = new();

    private IMapLayerCollection? _layerCollection;
    private MapsuiMapSession? _mapSession;
    private IMapViewportController? _viewport;
    private MapPresentationState _presentation;

    private MapPresentationState CurrentPresentation =>
        Volatile.Read(ref _presentation);

    /// <inheritdoc />
    public bool IsInitialized => _layerCollection is not null && _viewport is not null;

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
        DatasetProcessorOwner processorOwner,
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
        ArgumentNullException.ThrowIfNull(processorOwner);

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
        _processorOwner = processorOwner;
        _renderActivityMonitor = renderActivityMonitor;

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
        if (_globalTime.CurrentTime is { } now && _globalTime.IsActive)
            _ = ReRenderAtTimeAsync(now, CancellationToken.None);
    }

    public DatasetProcessorSnapshot AcquireProcessors()
    {
        var snapshot = new Dictionary<DatasetEntry, IDatasetProcessor>();
        var leases = new List<IDisposable>();
        foreach (var entry in _processorEntries.Values.ToArray())
        {
            if (!_processorOwner.TryAcquire(entry.Id, out var lease))
                continue;

            leases.Add(lease);
            snapshot[entry] = lease.Processor;
        }
        return new DatasetProcessorSnapshot(
            new ReadOnlyDictionary<DatasetEntry, IDatasetProcessor>(snapshot),
            leases);
    }
    public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers
    {
        get
        {
            var layers = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
            if (_mapSession is null)
                return layers;

            foreach (var snapshot in _mapSession.GetDatasets())
            {
                if (_sessionEntries.TryGetValue(snapshot.Dataset.Id, out var entry))
                    layers[entry] = snapshot.Layers;
            }
            return layers;
        }
    }

    public IReadOnlyList<ILayer> CurrentStackedLayers =>
        _mapSession?.GetStackedLayers() ?? [];

    public IReadOnlyList<LayerStackEntry> CurrentStackEntries =>
        _mapSession?.GetLayerStackEntries() ?? [];

    public event Action? LayerStackChanged;

    public event Action<string>? ActiveChanged;

    public bool GetActive(string datasetId)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetId);
        var entry = FindEntry(datasetId);
        return entry?.IsActive ?? true;
    }

    public void SetActive(string datasetId, bool active)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetId);
        var entry = FindEntry(datasetId);
        if (entry is null || entry.IsActive == active) return;
        entry.IsActive = active;
        ApplyEntryState(entry);
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
        _mapSession = layerCollection.DatasetSession
            ?? throw new InvalidOperationException(
                "The map layer collection must provide a Mapsui dataset session.");
        _mapSession.LayersChanged += OnSessionLayersChanged;
        _mapSession.DatasetRefreshFailed += OnDatasetRefreshFailed;
        _mapSession.SetMarinerSettings(CurrentPresentation.Mariner);
        _globalTime.AttachTo(_mapSession);
        _globalTime.CurrentTimeChanged +=
            time => _ = ReRenderAtTimeAsync(time, CancellationToken.None);
        _globalTime.RangeChanged += OnGlobalRangeChanged;
        _viewport = viewport;

        var transientFcPaths = _catalogueSeeder.Seed(options);
        _fcOverrides.SetTransientPaths(transientFcPaths);

    }

    public async Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureInitialized();

        var loadGeneration = BeginLoad(entry);
        var gate = _loadGates.GetValue(entry, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (IsCurrentLoad(entry, loadGeneration))
            {
                await LoadCoreAsync(
                    entry,
                    loadGeneration,
                    cancellationToken).ConfigureAwait(true);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task LoadCoreAsync(
        DatasetEntry entry,
        long loadGeneration,
        CancellationToken cancellationToken)
    {
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

        IDatasetProcessor? callerOwnedProcessor = null;
        IDatasetProcessor? registeredProcessor = null;
        var loadCompleted = false;
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
            callerOwnedProcessor = processor;
            if (!IsCurrentLoad(entry, loadGeneration))
            {
                DriveLoadAbandoned(loadNotification, fromExchangeSet, entry);
                return;
            }
            if (!_processorOwner.TryRegister(entry.Id, processor))
            {
                DriveLoadAbandoned(loadNotification, fromExchangeSet, entry);
                return;
            }
            callerOwnedProcessor = null;
            registeredProcessor = processor;
            _processorEntries[entry.Id] = entry;
            _sessionEntries[entry.Id] = entry;
            var wasKnownBySession = _mapSession!.GetDataset(entry.Id) is not null;

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
                foreach (var other in _processorEntries.Values)
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
            // Default the surface to hidden only on the session's first load.
            // Lazy unload retains session state and its order slot, so a reload
            // preserves a surface the user had opted into (issue #483).
            if (processor is S104DatasetProcessor { IsGriddedSurface: true }
                && !wasKnownBySession)
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

            entry.SetVersionAssessment(processor.VersionAssessment);
            entry.AvailableTimes = [];
            entry.CurrentTime = null;
            entry.SetLoadedState(processor.Metadata);
            var mapDataset = entry.MapDataset
                ?? throw new InvalidOperationException(
                    "A loaded entry must expose renderer-neutral dataset state.");
            _mapSession.SetDataset(
                mapDataset,
                entry.MinimumDisplayScale,
                entry.MaximumDisplayScale);
            ProjectSessionState(entry);
            SubscribeEntry(entry);
            var initialTime = entry.CurrentTime;
            var gatedHidden = entry.AvailableTimes.Count > 0
                && initialTime is null;

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
                {
                    DriveLoadAbandoned(loadNotification, fromExchangeSet, entry);
                    return;
                }
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
            if (!_processorOwner.TryAcquire(entry.Id, out var validationLease)
                || !ReferenceEquals(validationLease.Processor, processor))
            {
                validationLease?.Dispose();
                DriveLoadAbandoned(loadNotification, fromExchangeSet, entry);
                return;
            }

            EncDotNet.S100.Validation.ValidationReport? validation;
            using (validationLease)
            {
                validation = await Task.Run(
                    () => SafeValidate(processor),
                    token).ConfigureAwait(true);
            }
            if (!IsCurrentLoad(entry, loadGeneration)
                || !OwnsProcessor(entry, processor))
            {
                DriveLoadAbandoned(loadNotification, fromExchangeSet, entry);
                return;
            }
            entry.SetValidationReport(validation);
            ApplyEntryState(entry);

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
            if (!IsCurrentLoad(entry, loadGeneration)
                || !OwnsProcessor(entry, processor))
            {
                DriveLoadAbandoned(loadNotification, fromExchangeSet, entry);
                return;
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

            DatasetLoaded?.Invoke(entry);
            loadCompleted = true;
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
        finally
        {
            if (!loadCompleted && registeredProcessor is not null)
                RollBackLoad(entry, registeredProcessor);
            if (callerOwnedProcessor is IDisposable disposableProcessor)
                disposableProcessor.Dispose();
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

    private static void DriveLoadAbandoned(
        INotificationHandle? handle,
        bool fromExchangeSet,
        DatasetEntry entry)
    {
        if (!fromExchangeSet)
        {
            DriveTerminal(
                handle,
                NotificationSeverity.Info,
                Strings.Toast_DatasetCancelled,
                entry.DisplayName);
        }
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
        _mapSession?.SetCurrentTime(t);
        if (_mapSession is not null)
        {
            await _mapSession.RefreshTimeAsync(
                (processor, selectedTime) => CreateRenderContext(
                    processor,
                    selectedTime),
                cancellationToken).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task SetPresentationAsync(
        MapPresentationState presentation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        cancellationToken.ThrowIfCancellationRequested();

        using var __cmd = ViewerObservability.BeginCommand("presentation.apply");
        _mapSession?.SetMarinerSettings(presentation.Mariner);
        Volatile.Write(ref _presentation, presentation);
        if (_mapSession is not null
            && await _mapSession.RefreshAsync(
                (processor, selectedTime) => CreateRenderContext(
                    processor,
                    selectedTime,
                    presentation),
                cancellationToken).ConfigureAwait(true))
        {
            _notifications.Create(Strings.Toast_Success)
                .WithSeverity(NotificationSeverity.Success)
                .WithContent(Strings.Toast_SettingsApplied)
                .Show();
        }
    }

    private async Task<MapsuiDatasetResult?> RenderAndReplaceAsync(
        DatasetEntry entry,
        IDatasetProcessor processor,
        DateTime? timeStep,
        MapPresentationState? presentation,
        CancellationToken cancellationToken,
        Action<MapsuiDatasetResult>? onApplied = null)
    {
        return await RenderAndReplaceCoreAsync(
            entry,
            processor,
            timeStep,
            presentation,
            cancellationToken,
            onApplied).ConfigureAwait(true);
    }

    private Task ReplaceLayersAsync(
        DatasetEntry entry,
        IDatasetProcessor processor,
        CancellationToken cancellationToken,
        Action? onApplied = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OwnsProcessor(entry, processor))
            return Task.CompletedTask;

        _mapSession!.ClearLayers(entry.Id);
        onApplied?.Invoke();
        ApplyEntryState(entry);
        return Task.CompletedTask;
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
        var result = await _mapSession!.RenderAsync(
            entry.Id,
            context,
            cancellationToken).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        if (result is null || !OwnsProcessor(entry, processor))
            return null;

        ProjectSessionState(entry);
        onApplied?.Invoke(result);
        ApplyEntryState(entry);
        return result;
    }

    private bool OwnsProcessor(DatasetEntry entry, IDatasetProcessor processor) =>
        _processorOwner.Owns(entry.Id, processor);

    public void RemoveEntry(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        InvalidateLoad(entry);

        UnsubscribeSubLayers(entry);
        entry.SubLayers.Clear();
        if (_subscribedEntries.Remove(entry))
            entry.PropertyChanged -= OnEntryPropertyChanged;
        _mapSession?.RemoveDataset(entry.Id);
        _processorEntries.Remove(entry.Id);
        _sessionEntries.Remove(entry.Id);
        _s128CatalogSource.RemoveDataset(entry.DisplayName);
        entry.IsLoaded = false;
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
        InvalidateLoad(entry);

        _mapSession?.RemoveDataset(entry.Id, preserveState: true);
        _processorEntries.Remove(entry.Id);
        entry.IsLoaded = false;
        entry.IsDeferred = true;
    }

    public void SetEntryOrder(IReadOnlyList<DatasetEntry> orderedEntries)
    {
        ArgumentNullException.ThrowIfNull(orderedEntries);
        if (_mapSession is null) return;

        // The Viewer list is top-first; the reusable session and Mapsui dataset
        // band use bottom-to-top paint order.
        _mapSession.SetOrder(
            orderedEntries
                .Reverse()
                .Select(entry => entry.Id)
                .ToArray());
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

    /// <inheritdoc />
    public IReadOnlyList<OverscaleCellInput> GetOverscaleCells()
    {
        List<OverscaleCellInput>? cells = null;
        if (_mapSession is null)
            return [];

        foreach (var snapshot in _mapSession.GetDatasets())
        {
            if (snapshot.MaximumDisplayScale is not int compilationScale
                || compilationScale <= 0
                || !snapshot.IsDrawing
                || snapshot.CoverageGeometry is not { IsEmpty: false } coverage)
            {
                continue;
            }

            (cells ??= []).Add(new OverscaleCellInput
            {
                Name = snapshot.Dataset.Name,
                Coverage = coverage,
                CompilationScaleDenominator = compilationScale,
            });
        }

        return (IReadOnlyList<OverscaleCellInput>?)cells ?? [];
    }

    private void ProjectSessionState(DatasetEntry entry)
    {
        var snapshot = _mapSession?.GetDataset(entry.Id);
        if (snapshot is null)
            return;

        var existing = entry.SubLayers.ToDictionary(s => s.Key, s => s);
        var projected = new List<DatasetSubLayer>(snapshot.Dataset.SubLayers.Count);
        foreach (var state in snapshot.Dataset.SubLayers)
        {
            if (!existing.TryGetValue(state.Key, out var subLayer))
            {
                subLayer = new DatasetSubLayer(
                    state.Key,
                    ResolveSubLayerDisplayName(state.Key));
            }
            subLayer.PropertyChanged -= OnSubLayerPropertyChanged;
            subLayer.IsVisible = state.IsVisible;
            subLayer.Opacity = state.Opacity;
            subLayer.PropertyChanged += OnSubLayerPropertyChanged;
            projected.Add(subLayer);
        }

        foreach (var stale in existing.Values.Except(projected))
            stale.PropertyChanged -= OnSubLayerPropertyChanged;

        entry.SubLayers.Clear();
        foreach (var subLayer in projected)
            entry.SubLayers.Add(subLayer);

        entry.AvailableTimes = snapshot.Dataset.AvailableTimes;
        entry.CurrentTime = snapshot.Dataset.CurrentTime;
        entry.Info = snapshot.Info;
        entry.ContentMaxVisibleResolution = snapshot.ContentMaxVisibleResolution;
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

    private void SubscribeEntry(DatasetEntry entry)
    {
        if (_subscribedEntries.Add(entry))
            entry.PropertyChanged += OnEntryPropertyChanged;
    }

    private void ApplyEntryState(DatasetEntry entry)
    {
        if (_mapSession is null || entry.MapDataset is not { } dataset)
            return;

        _mapSession.SetDataset(
            dataset,
            entry.MinimumDisplayScale,
            entry.MaximumDisplayScale);
        entry.ContentMaxVisibleResolution =
            _mapSession.GetDataset(entry.Id)?.ContentMaxVisibleResolution;
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DatasetEntry entry) return;
        if (e.PropertyName is not (
            nameof(DatasetEntry.IsVisible)
            or nameof(DatasetEntry.IsActive)
            or nameof(DatasetEntry.Opacity)))
            return;
        ApplyEntryState(entry);
    }

    private void OnSubLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DatasetSubLayer sub) return;
        if (e.PropertyName is not (nameof(DatasetSubLayer.IsVisible) or nameof(DatasetSubLayer.Opacity)))
            return;

        foreach (var entry in _processorEntries.Values)
        {
            if (entry.SubLayers.Contains(sub))
            {
                ApplyEntryState(entry);
                break;
            }
        }
    }

    private DatasetEntry? FindEntry(string datasetId)
    {
        var loadedEntry = _processorEntries.Values.FirstOrDefault(entry =>
            string.Equals(entry.Id.Value, datasetId, StringComparison.Ordinal));
        return loadedEntry ?? _sessionEntries.Values.FirstOrDefault(entry =>
            string.Equals(entry.Id.Value, datasetId, StringComparison.Ordinal));
    }

    private void OnSessionLayersChanged()
    {
        foreach (var snapshot in _mapSession?.GetDatasets() ?? [])
        {
            if (_sessionEntries.TryGetValue(snapshot.Dataset.Id, out var entry))
                ProjectSessionState(entry);
        }
        LayerStackChanged?.Invoke();
    }

    private void OnDatasetRefreshFailed(
        MapDatasetId datasetId,
        Exception exception)
    {
        var source = _sessionEntries.TryGetValue(datasetId, out var entry)
            ? entry.FilePath
            : datasetId.Value;
        Console.Error.WriteLine(
            $"Failed to refresh {source}:{Environment.NewLine}{exception}");
    }

    private void EnsureInitialized()
    {
        if (_layerCollection is null || _viewport is null)
            throw new InvalidOperationException("DatasetLoaderService.Initialize must be called before LoadAsync.");
    }

    private long BeginLoad(DatasetEntry entry)
        => Interlocked.Increment(ref _loadGenerations.GetOrCreateValue(entry).Value);

    private void InvalidateLoad(DatasetEntry entry)
        => Interlocked.Increment(ref _loadGenerations.GetOrCreateValue(entry).Value);

    private bool IsCurrentLoad(DatasetEntry entry, long generation)
        => Volatile.Read(ref _loadGenerations.GetOrCreateValue(entry).Value) == generation;

    private void RollBackLoad(DatasetEntry entry, IDatasetProcessor processor)
    {
        if (!_processorOwner.Remove(entry.Id, processor))
            return;

        _processorEntries.Remove(entry.Id);
        _sessionEntries.Remove(entry.Id);
        _mapSession?.RemoveDataset(
            entry.Id,
            removeProcessor: false);
        UnsubscribeSubLayers(entry);
        entry.SubLayers.Clear();
        if (_subscribedEntries.Remove(entry))
            entry.PropertyChanged -= OnEntryPropertyChanged;
        _s128CatalogSource.RemoveDataset(entry.DisplayName);
        entry.IsLoaded = false;
        entry.Info = null;
        entry.SetValidationReport(null);
    }

    private sealed class LoadGeneration
    {
        public long Value;
    }
}
