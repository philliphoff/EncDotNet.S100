using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Mapsui.Avalonia;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.Services.Updates;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Projections;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Viewer;

public partial class MainWindow : ShadUI.Window
{
    private readonly IRecentFilesService _recentFiles;
    private readonly ScreenshotService _screenshotService;
    private readonly IDatasetLoaderService _loader;
    private readonly IPickService _pickService;
    private readonly IFileDialogService _fileDialog;
    private readonly IExchangeSetService _exchangeSetService;
    private readonly IUpdateNotificationCoordinator? _updateNotificationCoordinator;
    private readonly MainViewModel _viewModel;
    private readonly DatasetCatalogAggregator _catalogAggregator;
    private readonly CancellationTokenSource _windowLifetimeCancellation = new();
    private readonly MapsuiMapHost _mapHost;
    private readonly Action _rendererRedrawHandler;
    private ValidationOverlayService? _validationOverlay;
    private EncDotNet.S100.Viewer.Diagnostics.RenderActivityMonitor? _renderActivityMonitor;
    private Map? _renderActivityMap;
    private EventHandler? _renderActivityRefreshHandler;
    private EncDotNet.S100.Viewer.Services.DynamicSources.DynamicSourceOverlayHost? _dynamicSourceOverlayHost;
    private EncDotNet.S100.Viewer.Services.PickHighlightController? _pickHighlightController;
    private EncDotNet.S100.Viewer.Services.DatasetExtentIndicatorController? _extentIndicatorController;
    private EncDotNet.S100.Viewer.Services.OverscaleCurtainController? _overscaleCurtainController;
    private Mapsui.Layers.MemoryLayer? _routeOverlayLayer;
    private EncDotNet.S100.Viewer.Tools.IMeasureOverlayAppearanceProvider? _routeAppearance;
    private EncDotNet.S100.Viewer.Services.RoutesService? _routeStore;
    private readonly List<IDisposable> _dynamicSourceRegistrations = new();
    private string? _screenshotPath;
    private bool _exitAfterScreenshot;
    private bool _closeAfterScreenshot;
    private bool _fullWindowScreenshot;
    private ViewerCommandSettings? _startupOptions;
    private Color _accentColor;

    public MainWindow() : this(null) { }

    /// <summary>
    /// Legacy constructor retained for the Avalonia design-time previewer
    /// and for callers that pre-DI created their own instance. Falls through
    /// to the dependency-injected constructor by resolving services from
    /// <see cref="App.Services"/> if available, else newing up defaults.
    /// </summary>
    internal MainWindow(ViewerCommandSettings? options)
        : this(
            options,
            ResolveOrFallback<MainViewModel>(static () => throw new InvalidOperationException(
                "MainViewModel cannot be resolved without the application service provider.")),
            ResolveOrFallback<DatasetCatalogAggregator>(static () => new DatasetCatalogAggregator()),
            ResolveOrFallback<IRecentFilesService>(static () => throw new InvalidOperationException(
                "IRecentFilesService cannot be resolved without the application service provider.")),
            ResolveOrFallback<ScreenshotService>(static () => new ScreenshotService()),
            ResolveOrFallback<IDatasetLoaderService>(static () => throw new InvalidOperationException(
                "IDatasetLoaderService cannot be resolved without the application service provider.")),
            ResolveOrFallback<IPickService>(static () => throw new InvalidOperationException(
                "IPickService cannot be resolved without the application service provider.")),
            ResolveOrFallback<IFileDialogService>(static () => new FileDialogService()),
            ResolveOrFallback<IExchangeSetService>(static () => throw new InvalidOperationException(
                "IExchangeSetService cannot be resolved without the application service provider.")),
            null)
    {
    }

    private static T ResolveOrFallback<T>(Func<T> fallback) where T : class
    {
        try
        {
            return App.Services.GetRequiredService<T>();
        }
        catch (InvalidOperationException)
        {
            return fallback();
        }
    }

    internal MainWindow(
        ViewerCommandSettings? options,
        MainViewModel viewModel,
        DatasetCatalogAggregator catalogAggregator,
        IRecentFilesService recentFiles,
        ScreenshotService screenshotService,
        IDatasetLoaderService loader,
        IPickService pickService,
        IFileDialogService fileDialog,
        IExchangeSetService exchangeSetService,
        IUpdateNotificationCoordinator? updateNotificationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(catalogAggregator);
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(screenshotService);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(pickService);
        ArgumentNullException.ThrowIfNull(fileDialog);
        ArgumentNullException.ThrowIfNull(exchangeSetService);

        InitializeComponent();

        // Bind the notification overlay to the DI-managed notification
        // service so background services can surface notifications.
        NotificationHost.ItemsSource =
            App.Services.GetRequiredService<Services.Notifications.INotificationService>().Active;

        _viewModel = viewModel;
        _catalogAggregator = catalogAggregator;
        _recentFiles = recentFiles;
        _screenshotService = screenshotService;
        _loader = loader;
        _pickService = pickService;
        _fileDialog = fileDialog;
        _exchangeSetService = exchangeSetService;
        _updateNotificationCoordinator = updateNotificationCoordinator;

        // Hand the loader a map host now that the Mapsui control exists, and
        // seed catalogues / build the pipeline factory from CLI options. The
        // loader subscribes to its own settings dependencies internally.
        var map = MapControl.Map
            ?? throw new InvalidOperationException(
                "The map control must have a map before creating the Viewer host.");
        _mapHost = new MapsuiMapHost(
            map,
            AvaloniaMapsuiMapAdapter.Attach(MapControl),
            App.Services.GetRequiredService<DatasetProcessorOwner>(),
            App.Services.GetRequiredService<
                EncDotNet.S100.Renderers.Mapsui.MapsuiDatasetRenderer>(),
            App.Services.GetRequiredService<
                EncDotNet.S100.Datasets.Pipelines.Interoperability.IInteroperabilityAuthorityProvider>());
        _rendererRedrawHandler = _mapHost.RequestRedraw;
        App.Services.GetRequiredService<MapCapabilityAccessor<IMapCoordinateConverter>>().Current = _mapHost;
        App.Services.GetRequiredService<MapCapabilityAccessor<IMapViewportController>>().Current = _mapHost;
        // Snapshot is the MCP readiness gate, so publish it only after the
        // coordinate and viewport capabilities used alongside it are attached.
        App.Services.GetRequiredService<MapCapabilityAccessor<IMapSnapshotRenderer>>().Current = _mapHost;
        // Let the feedback reporter capture the whole application window.
        App.Services.GetRequiredService<IAppScreenshotProvider>().Target = this;
        // Render-state controller bridges MCP / scripted callers to the
        // viewer's palette and ECDIS display category without exposing
        // SettingsViewModel / EcdisDisplayState directly.
        App.Services.GetRequiredService<IRenderStateControllerAccessor>().Current =
            new ViewerRenderStateController(
                App.Services.GetRequiredService<ViewModels.SettingsViewModel>(),
                App.Services.GetRequiredService<EcdisDisplayState>());
        // UI controller bridges MCP / scripted callers to the viewer's
        // activity-panel state (which docks are open and which tab each
        // shows) without exposing MainViewModel directly.
        App.Services.GetRequiredService<IViewerUiControllerAccessor>().Current =
            new ViewerUiController(_viewModel);
        _loader.Initialize(_mapHost, _mapHost, options);
        // Wire validation finding click-to-zoom: each finding view-model
        // routes its <c>ZoomToFindingCommand</c> through this dispatcher.
        _viewModel.Datasets.ZoomDispatcher = _mapHost.ZoomToExtent;
        // Build the validation findings overlay layer that draws above
        // all dataset layers for the currently-selected dataset. The
        // service subscribes to the datasets view-model and lives for
        // the lifetime of the window.
        _validationOverlay = new ValidationOverlayService(_mapHost, _viewModel.Datasets);

        Closed += (_, _) =>
        {
            _windowLifetimeCancellation.Cancel();
            _windowLifetimeCancellation.Dispose();
            App.Services.GetRequiredService<DatasetProcessorOwner>().Dispose();
            _validationOverlay?.Dispose();
            _validationOverlay = null;
            ClearRendererRedrawHandlers();
            // Detach render-activity wiring so the static hub does not
            // outlive the window and a torn-down map is not probed.
            EncDotNet.S100.Viewer.Diagnostics.RenderActivityHub.Sink = null;
            if (_renderActivityMonitor is not null)
            {
                _renderActivityMonitor.BusyProbe = null;
                if (_renderActivityMap is not null && _renderActivityRefreshHandler is not null)
                {
                    _renderActivityMap.RefreshGraphicsRequest -= _renderActivityRefreshHandler;
                }
                _renderActivityMonitor = null;
                _renderActivityMap = null;
                _renderActivityRefreshHandler = null;
            }
            // PR-M3: flush any pending debounced size writes so the last
            // splitter drag isn't lost on shutdown.
            _viewModel.OnShutdown();
            // Record a clean shutdown so the next launch does not report
            // this (graceful) exit as a crash.
            EncDotNet.S100.Viewer.Diagnostics.UncleanShutdownSentinel.MarkCleanExit();
            foreach (var reg in _dynamicSourceRegistrations) reg.Dispose();
            _dynamicSourceRegistrations.Clear();
            App.Services.GetRequiredService<
                EncDotNet.S100.Viewer.Services.DynamicSources.DynamicFeatureSourceRegistryAccessor>()
                .Current = null;
            _dynamicSourceOverlayHost?.Dispose();
            _dynamicSourceOverlayHost = null;
            _pickHighlightController?.Dispose();
            _pickHighlightController = null;
            _extentIndicatorController?.Dispose();
            _extentIndicatorController = null;
            _overscaleCurtainController?.Dispose();
            _overscaleCurtainController = null;
            // Clear the late-bound accessors this window owns so panel /
            // screenshot MCP tools observe the torn-down state (UiNotReady /
            // WindowNotReady) rather than a stale controller, and so the
            // MainViewModel / window are not kept alive after close.
            App.Services.GetRequiredService<IViewerUiControllerAccessor>().Current = null;
            App.Services.GetRequiredService<IAppScreenshotProvider>().Target = null;
            App.Services.GetRequiredService<MapCapabilityAccessor<IMapSnapshotRenderer>>().Current = null;
            App.Services.GetRequiredService<MapCapabilityAccessor<IMapCoordinateConverter>>().Current = null;
            App.Services.GetRequiredService<MapCapabilityAccessor<IMapViewportController>>().Current = null;
            _mapHost.Dispose();
        };
        DataContext = _viewModel;

        // Build the native menu bar (File / View › Appearance) and keep its
        // toggle items mirrored against the view-model. The builder owns the
        // PropertyChanged subscriptions for the lifetime of this window.
        new NativeMenuBuilder(_viewModel, _recentFiles).Attach(
            window: this,
            openDatasetAsync: OpenDatasetAsync,
            openExchangeSetAsync: OpenExchangeSetAsync,
            openExchangeSetZipAsync: OpenExchangeSetZipAsync);

        // Show built-in specification entries in the catalogue views
        foreach (var spec in Specifications.Specification.AvailableSpecs)
        {
            var (fcVersion, fcVersionDate) = CatalogueSpecDetection.ReadBuiltInFeatureCatalogueInfo(spec);
            _viewModel.FeatureCatalogues.AddBuiltIn(spec, Strings.Catalogue_BuiltInLabel, fcVersion, fcVersionDate);

            if (Specifications.Specification.HasPortrayalCatalogue(spec))
            {
                _viewModel.PortrayalCatalogues.AddBuiltIn(spec, Strings.Catalogue_BuiltInLabel, CatalogueSpecDetection.ReadBuiltInPortrayalCatalogueVersion(spec));
            }
        }

        // Apply persisted accent color. Both the user's colour choice and
        // the active chrome theme feed the effective brush, so re-apply on
        // either change (the low-light themes mute the accent).
        ApplyAccentColor(_viewModel.Settings.AccentColor);
        _viewModel.Settings.AccentColorChanged += ApplyAccentColor;
        if (Application.Current is { } themedApp)
            themedApp.ActualThemeVariantChanged += (_, _) => ApplyAccentColor(_accentColor);

        // Apply persisted scale-bar distance unit and react to changes.
        ScaleBar.Unit = _viewModel.Settings.DistanceUnit;
        _viewModel.Settings.DistanceUnitChanged += unit => ScaleBar.Unit = unit;

        // Surface DatasetsViewModel rejection of unknown file extensions.
        _viewModel.Datasets.UnrecognizedFileEncountered += extension =>
        {
            App.Services.GetRequiredService<INotificationService>()
                .Create(Strings.Toast_Warning)
                .WithSeverity(NotificationSeverity.Warning)
                .WithContent(string.Format(Strings.Status_UnrecognizedFileType, extension))
                .Show();
        };

        // Clean up layers when a dataset entry is removed from the list.
        _viewModel.Datasets.Entries.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && e.OldItems is not null)
            {
                foreach (DatasetEntry removed in e.OldItems)
                {
                    _loader.RemoveEntry(removed);
                }
            }
        };

        // Basemap (issue #295). Default Offline (bundled Natural Earth
        // land — zero network); the user can switch to None or Online in
        // Settings (or via --basemap). Keep a reference so swapping mode
        // can replace it live. Always sits at index 0, beneath datasets.
        _mapHost.SetBasemapLayer(
            BasemapLayerFactory.TryCreate(_viewModel.Settings.SelectedBasemapMode));
        _viewModel.Settings.BasemapModeChanged += OnBasemapModeChanged;

        // ENC water colour (S-52 / S-101 DEPDW) — used as the map control
        // background so the unrendered area outside the tile layer's
        // extent visually blends with the chart's water.
        if (MapControl.Map is { } mapForBackColor)
        {
            mapForBackColor.BackColor = new Mapsui.Styles.Color(170, 211, 223);
        }

        // Wire the render-activity monitor that backs the MCP
        // 'await_render_idle' / 'get_render_stats' tools. The
        // InstrumentedMapControl feeds paint stats through the static
        // RenderActivityHub.Sink; here we additionally feed Mapsui's
        // graphics-refresh signal as activity and expose a layer-busy
        // probe so idle is not reported while an async fetch is pending.
        if (MapControl.Map is { } activityMap)
        {
            var monitor = App.Services.GetRequiredService<
                EncDotNet.S100.Viewer.Diagnostics.RenderActivityMonitor>();
            _renderActivityMonitor = monitor;
            _renderActivityMap = activityMap;
            EncDotNet.S100.Viewer.Diagnostics.RenderActivityHub.Sink = monitor;

            monitor.BusyProbe = () =>
            {
                // Snapshot the layer collection defensively: it may be
                // mutated on the UI thread while this runs on a threadpool
                // thread driving an MCP request.
                foreach (var layer in activityMap.Layers.ToArray())
                {
                    if (layer.Busy) return true;
                }
                return false;
            };

            _renderActivityRefreshHandler = (_, _) => monitor.NotifyActivity();
            activityMap.RefreshGraphicsRequest += _renderActivityRefreshHandler;
        }

        // When the off-thread vector-snapshot prebuild publishes a freshly
        // recorded image on a background thread, marshal a single graphics
        // refresh onto the UI thread so the transient scaled-stale blit is
        // replaced by the crisp image. Wired unconditionally (no-op unless the
        // prebuild is enabled) so toggling the prebuild on at runtime works
        // without a relaunch.
        EncDotNet.S100.Renderers.Mapsui.S100VectorSnapshotRenderer.RequestRedraw =
            _rendererRedrawHandler;

        // Same marshalling for the TiledScene ("B") subsystem: when a worker
        // publishes a freshly rasterised VectorScene image, request a single
        // UI-thread repaint that swaps the transient stale blit for the new image.
        EncDotNet.S100.Renderers.Mapsui.S100VectorSceneRenderer.RequestRedraw =
            _rendererRedrawHandler;

        // Same marshalling for the Phase-2 tiled arm of the TiledScene subsystem:
        // when a worker publishes a freshly rasterised base-plane tile, request a
        // single UI-thread repaint that composites it into the visible mosaic.
        EncDotNet.S100.Renderers.Mapsui.S100VectorTileRenderer.RequestRedraw =
            _rendererRedrawHandler;

        // Bind the map-viewport notifier as early as possible so the
        // AIS overlay's zoom-gated decorator (resolved below via
        // GetServices<IDynamicFeatureSource>) can read the current
        // viewport synchronously in its constructor. See
        // docs/design/ais-zoom-gated-subscription.md.
        if (MapControl.Map?.Navigator is { } notifierNav)
        {
            App.Services.GetRequiredService<
                EncDotNet.S100.Viewer.Services.MapViewportNotifier>()
                .Bind(notifierNav);

            // Clamp zoom in/out so the user cannot zoom to an unbounded,
            // meaningless scale (e.g. many world copies off the edge of a
            // cross-antimeridian dataset, or arbitrarily deep past chart
            // resolution).
            MapZoomLimits.Apply(notifierNav);
        }

        // PR-D2: dynamic-source overlay host. Registered *after* the
        // basemap so MapsuiMapHost's ComputeOverlayInsertIndex places
        // the overlay above the OSM tile layer rather than at index 0
        // (where the subsequently-added basemap would cover it).
        _dynamicSourceOverlayHost = new EncDotNet.S100.Viewer.Services.DynamicSources.DynamicSourceOverlayHost(
            _mapHost,
            App.Services,
            logger: App.Services.GetService<Microsoft.Extensions.Logging.ILogger<EncDotNet.S100.Viewer.Services.DynamicSources.DynamicSourceOverlayHost>>());

        // PR-D2.1: seed per-source visibility from persisted settings
        // *before* the Register loop so each source's MemoryLayer.Enabled
        // starts in the user's last-known state, then wire write-back so
        // the Layer Stack panel's visibility toggle persists.
        var viewerSettings = App.Services.GetRequiredService<ViewerSettings>();
        foreach (var kv in viewerSettings.DynamicSourceVisibility)
        {
            _dynamicSourceOverlayHost.SetVisible(kv.Key, kv.Value);
        }
        _dynamicSourceOverlayHost.SourcesChanged += () =>
        {
            foreach (var info in _dynamicSourceOverlayHost.Sources)
            {
                viewerSettings.DynamicSourceVisibility[info.Id] = _dynamicSourceOverlayHost.GetVisible(info.Id);
            }
            try { viewerSettings.Save(); } catch { /* best-effort */ }
        };

        // Attach the registry to the accessor so view-models resolved
        // before window construction (e.g. LayerStackViewModel as a
        // singleton) start seeing dynamic sources.
        App.Services.GetRequiredService<
            EncDotNet.S100.Viewer.Services.DynamicSources.DynamicFeatureSourceRegistryAccessor>()
            .Current = _dynamicSourceOverlayHost;

        foreach (var source in App.Services.GetServices<EncDotNet.S100.DynamicSources.IDynamicFeatureSource>())
        {
            _dynamicSourceRegistrations.Add(_dynamicSourceOverlayHost.Register(source));
        }

        // Pick highlight: keep a cursor-echo marker + selected-feature outline
        // on the overlay tier in sync with the current pick report, so the
        // pick stays visible as the user (or an MCP agent) pans the map.
        _pickHighlightController = new EncDotNet.S100.Viewer.Services.PickHighlightController(
            _mapHost,
            App.Services.GetRequiredService<PickReportViewModel>(),
            App.Services.GetRequiredService<ViewerDatasetCatalog>(),
            App.Services.GetRequiredService<
                EncDotNet.S100.Viewer.Tools.IMeasureOverlayAppearanceProvider>(),
            App.Services.GetRequiredService<SettingsViewModel>());

        // Out-of-scale extent indicators: outline the extents of loaded
        // datasets that have zoomed out past their display-scale minimum, so a
        // wide-spread exchange set still shows where its members are (#446).
        _extentIndicatorController = new EncDotNet.S100.Viewer.Services.DatasetExtentIndicatorController(
            _mapHost,
            _viewModel.Datasets,
            App.Services.GetRequiredService<
                EncDotNet.S100.Viewer.Tools.IMeasureOverlayAppearanceProvider>(),
            App.Services.GetRequiredService<SettingsViewModel>());

        // On-chart overscale curtain: paint a subtle vertical-line pattern over
        // the region of each cell displayed beyond its compilation scale (#441).
        _overscaleCurtainController = new EncDotNet.S100.Viewer.Services.OverscaleCurtainController(
            _mapHost,
            _viewModel.Datasets,
            _loader,
            App.Services.GetRequiredService<
                EncDotNet.S100.Viewer.Services.IMapViewportNotifier>(),
            App.Services.GetRequiredService<SettingsViewModel>());
        // Disable Mapsui's built-in LoggingWidget — it can throw "minX > maxX" on
        // narrow viewports during resize, and the exception is raised on the
        // render thread where we cannot intercept it.
        Mapsui.Widgets.InfoWidgets.LoggingWidget.ShowLoggingInMap = Mapsui.Widgets.ActiveMode.No;
        if (MapControl.Map is { } mapForWidgets)
        {
            var remaining = mapForWidgets.Widgets
                .Where(w => w is not Mapsui.Widgets.InfoWidgets.LoggingWidget)
                .ToArray();
            mapForWidgets.Widgets.Clear();
            foreach (var w in remaining)
            {
                mapForWidgets.Widgets.Enqueue(w);
            }
        }

        // Enable trackpad pan/pinch/rotate gestures, single/double-tap pick,
        // long-press pick, mouse lat/lon readout, scale-bar/compass viewport
        // sync, and the zoom in/out overlay buttons.
        var interactionController = new MapInteractionController(
            _viewModel,
            _pickService,
            _loader,
            App.Services.GetService<EncDotNet.S100.Viewer.Services.DynamicSources.IDynamicSourcePickService>());
        interactionController.Attach(MapControl, ZoomInButton, ZoomOutButton, ZoomToExtentButton, ScaleBar, CompassRose);

        // Wire the map-tool controller to the map: tools are registered with
        // the view-model's controller and pointer events are forwarded by
        // the interaction controller.
        InitializeMapTools(interactionController);

        // Apply the cursor that matches the current mode and keep it in sync
        // with the active tool.
        ApplyToolCursor();
        _viewModel.Tools.ActiveToolChanged += _ => Dispatcher.UIThread.Post(ApplyToolCursor);

        // Enable drag & drop of dataset files onto the map
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Apply CLI options
        _startupOptions = options;
        _screenshotPath = options?.ScreenshotPath;
        _exitAfterScreenshot = options?.ExitAfterScreenshot ?? false;
        _closeAfterScreenshot = options?.CloseAfterScreenshot ?? false;
        _fullWindowScreenshot = options?.FullWindowScreenshot ?? false;

        // A fixed window size makes screenshots reproducible across
        // machines. Applied before the window is shown.
        if (options?.ParsedWindowSize is { } size)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            CanResize = true;
            Width = size.Width;
            Height = size.Height;
        }

        // When an explicit startup viewport is requested, suppress the
        // per-dataset auto-zoom so the requested framing wins.
        if (options?.HasExplicitViewport == true)
        {
            _loader.SuppressAutoZoom = true;
        }

        // Add CLI portrayal catalogues to the view model (transient — not persisted)
        if (options?.PortrayalCatalogues is { } pcArgs)
        {
            foreach (var pcPath in pcArgs)
            {
                if (Directory.Exists(pcPath) && CatalogueSpecDetection.DetectPortrayalCatalogueSpec(pcPath) is { } pcSpec)
                {
                    _viewModel.PortrayalCatalogues.AddTransient(pcSpec, pcPath);
                }
            }
        }

        // Add CLI feature catalogues to the view model (transient — not persisted)
        if (options?.FeatureCatalogues is { } fcArgs)
        {
            foreach (var fcPath in fcArgs)
            {
                if (File.Exists(fcPath) && CatalogueSpecDetection.DetectFeatureCatalogueSpec(fcPath) is { } fcSpec)
                {
                    _viewModel.FeatureCatalogues.AddTransient(fcSpec, fcPath);
                }
            }
        }

        // Run startup automation once the window is shown: load CLI
        // datasets, drive the map to the requested state, and capture a
        // screenshot — in a deterministic order so an agent gets a
        // stable result.
        var datasetPaths = options?.Datasets?.Where(File.Exists).ToArray() ?? [];
        var needsAutomation = datasetPaths.Length > 0
            || _screenshotPath is not null
            || options?.HasExplicitViewport == true
            || options?.TimeStep is not null;
        if (needsAutomation)
        {
            Opened += async (_, _) => await RunStartupAutomationAsync(datasetPaths);
        }
        else if (options?.HasExplicitViewport != true)
        {
            // Interactive launch with no CLI-driven viewport: if the
            // own-ship overlay is enabled, frame the map on the own-ship
            // rather than leaving it at the whole-world default.
            Opened += async (_, _) => await FrameOnOwnShipAtStartupAsync();
        }

        // Surface a recovery notification when the previous run terminated
        // without a clean shutdown (a native crash, FailFast, kill, …).
        Opened += (_, _) => ReportPreviousUncleanShutdown();

        if (_updateNotificationCoordinator is not null)
        {
            Opened += async (_, _) => await _updateNotificationCoordinator
                .CheckAndNotifyAsync(_windowLifetimeCancellation.Token);
        }

        // Developer aid (--demo-notifications): seed a representative set of
        // notification cards so the overlay's styling and behaviour can be
        // verified on-screen without loading real data.
        if (options?.DemoNotifications == true)
        {
            Opened += (_, _) => SeedDemoNotifications();
        }
    }

    private bool _demoNotificationsSeeded;

    /// <summary>
    /// Seeds a representative spread of notification cards (severities,
    /// long body text for the "Show more"/"Show less" expander, and a
    /// persistent indeterminate/determinate progress bar) for on-screen
    /// verification of the notification overlay. Reachable only via the
    /// undocumented <c>--demo-notifications</c> developer flag, so the demo
    /// strings are intentionally inline rather than localized.
    /// </summary>
    private void SeedDemoNotifications()
    {
        if (_demoNotificationsSeeded)
            return;
        _demoNotificationsSeeded = true;

        var notifications = App.Services.GetRequiredService<INotificationService>();

        const string longBody =
            "This is a deliberately long notification body that exceeds two lines "
            + "so the card clips it with an ellipsis and offers a \"Show more\" link. "
            + "Expanding it reveals the full wrapped text, and a \"Show less\" link "
            + "collapses it again — exactly the behaviour we are verifying here.";

        notifications.Create("Information")
            .WithSeverity(NotificationSeverity.Info)
            .WithContent(longBody)
            .Persistent()
            .Show();

        notifications.Create("Success")
            .WithSeverity(NotificationSeverity.Success)
            .WithContent("Dataset loaded and rendered.")
            .Persistent()
            .Show();

        notifications.Create("Warning")
            .WithSeverity(NotificationSeverity.Warning)
            .WithContent("Some cells reference updates that were not applied.")
            .WithAction("Details", () => { })
            .WithAction("Remind me later", () => { })
            .WithAction("Skip this version", () => { })
            .WithAction("Stop checking", () => { })
            .Persistent()
            .Show();

        notifications.Create("Error")
            .WithSeverity(NotificationSeverity.Error)
            .WithContent(longBody)
            .WithAction("Copy details", () => { })
            .Persistent()
            .Show();

        var indeterminate = notifications.Create("Loading exchange set…")
            .WithSeverity(NotificationSeverity.Info)
            .WithContent("Parsing catalogue")
            .Persistent()
            .Show();
        indeterminate.SetIndeterminate(true);

        var determinate = notifications.Create("Loading dataset…")
            .WithSeverity(NotificationSeverity.Info)
            .WithContent("US5WA50M/US5WA50M.000")
            .Persistent()
            .Show();
        determinate.Report(0.45);
    }

    private bool _previousCrashReported;

    /// <summary>
    /// Shows a sticky warning toast when one or more previous sessions
    /// ended unexpectedly (detected by
    /// <see cref="App.PreviousUncleanShutdowns"/>), offering a one-click
    /// action to open the feedback reporter — which already carries the
    /// captured crash context via the last-error tracker. Shown at most
    /// once per window.
    /// </summary>
    private void ReportPreviousUncleanShutdown()
    {
        if (_previousCrashReported)
            return;
        _previousCrashReported = true;

        var crashed = App.PreviousUncleanShutdowns;
        if (crashed.Count == 0)
            return;

        var mostRecent = crashed[^1];
        var body = crashed.Count > 1
            ? string.Format(Strings.Toast_PreviousCrashBodyMultiple, crashed.Count)
            : string.Format(Strings.Toast_PreviousCrashBody, mostRecent.StartedUtc.ToLocalTime());

        App.Services.GetRequiredService<INotificationService>()
            .Create(Strings.Toast_PreviousCrashTitle)
            .WithSeverity(NotificationSeverity.Warning)
            .WithContent(body)
            .WithAction(
                Strings.Toast_PreviousCrashAction,
                () => _viewModel.ShowFeedbackCommand.Execute(null))
            .Persistent()
            .Show();
    }

    /// <summary>
    /// Interactive-launch framing: when the own-ship overlay is enabled,
    /// centre and zoom the map on the own-ship instead of the whole-world
    /// default. If the overlay is enabled but no fix has arrived yet, waits
    /// briefly for the first fix (via <see cref="OwnShipSource.Changed"/>)
    /// before framing, then gives up quietly. No-op when the overlay is
    /// disabled or an explicit CLI viewport was supplied.
    /// </summary>
    private async Task FrameOnOwnShipAtStartupAsync()
    {
        var source = ResolveOrFallback<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipSource>(
            static () => null!);
        if (source is null || !source.IsEnabled) return;

        if (TryFrameOnOwnShip(source)) return;

        // No fix yet: wait once for the first published feature, with a
        // short timeout so launch isn't blocked if no fix ever arrives.
        var tcs = new TaskCompletionSource();
        void OnChanged(object? sender, EncDotNet.S100.DynamicSources.DynamicFeaturesChanged e) => tcs.TrySetResult();
        source.Changed += OnChanged;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            if (completed == tcs.Task)
            {
                await Dispatcher.UIThread.InvokeAsync(() => TryFrameOnOwnShip(source));
            }
        }
        finally
        {
            source.Changed -= OnChanged;
        }
    }

    /// <summary>
    /// Closes the consolidated display-settings overlay when its in-panel
    /// close (✕) button is clicked. The flyout otherwise dismisses on
    /// light-dismiss (click-away) or Escape; this button gives the panel
    /// an explicit affordance matching the mockup.
    /// </summary>
    private void OnCloseDisplaySettings(object? sender, RoutedEventArgs e)
        => DisplaySettingsButton.Flyout?.Hide();

    /// <summary>
    /// Swaps the basemap layer in response to the Settings basemap-mode
    /// selector (issue #295). The basemap always sits at the bottom of
    /// the layer stack (index 0) beneath every dataset and overlay; a new
    /// mode removes the old layer and inserts a fresh one (or none for
    /// <see cref="BasemapMode.None"/>).
    /// </summary>
    private void OnBasemapModeChanged(BasemapMode mode)
    {
        _mapHost.SetBasemapLayer(BasemapLayerFactory.TryCreate(mode));
        _mapHost.RequestRedraw();
    }

    private void ClearRendererRedrawHandlers()
    {
        if (ReferenceEquals(
            EncDotNet.S100.Renderers.Mapsui.S100VectorSnapshotRenderer.RequestRedraw,
            _rendererRedrawHandler))
        {
            EncDotNet.S100.Renderers.Mapsui.S100VectorSnapshotRenderer.RequestRedraw = null;
        }

        if (ReferenceEquals(
            EncDotNet.S100.Renderers.Mapsui.S100VectorSceneRenderer.RequestRedraw,
            _rendererRedrawHandler))
        {
            EncDotNet.S100.Renderers.Mapsui.S100VectorSceneRenderer.RequestRedraw = null;
        }

        if (ReferenceEquals(
            EncDotNet.S100.Renderers.Mapsui.S100VectorTileRenderer.RequestRedraw,
            _rendererRedrawHandler))
        {
            EncDotNet.S100.Renderers.Mapsui.S100VectorTileRenderer.RequestRedraw = null;
        }
    }

    /// <summary>
    /// Centres and zooms the map on the own-ship's current fix, if one is
    /// published. Returns <see langword="true"/> when framing was applied.
    /// </summary>
    private bool TryFrameOnOwnShip(
        EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipSource source)
    {
        var feature = source.CurrentFeatures.FirstOrDefault();
        if (feature?.Coordinates is not { Count: > 0 } coords) return false;

        var (lat, lon) = coords[0];
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        // Harbour-scale resolution (~web-mercator zoom 13): close enough to
        // see the own-ship and its surroundings without losing context.
        const double resolution = 156543.03392804097 / (1 << 13);
        _mapHost.SetViewportToCenterAndResolution(new MPoint(x, y), resolution);
        return true;
    }

    /// <summary>
    /// Deterministic startup sequence for headless/agent runs: load any
    /// CLI datasets, wait for rendering to quiesce, drive the map to the
    /// requested time step and viewport, then capture a screenshot and
    /// optionally exit. Replaces the old fixed-delay screenshot path so
    /// the capture reflects a settled render rather than a guess.
    /// </summary>
    private async Task RunStartupAutomationAsync(string[] datasetPaths)
    {
        var lastEventTicks = new long[1];
        Action<DatasetEntry> handler =
            _ => Interlocked.Exchange(ref lastEventTicks[0], Environment.TickCount64);
        _loader.DatasetLoaded += handler;
        try
        {
            if (datasetPaths.Length > 0)
            {
                _viewModel.SelectDefaultTab();
                foreach (var datasetPath in datasetPaths)
                {
                    var spec = DatasetPipelineFactory.DetectProductSpec(datasetPath) ?? "S-101";
                    var entry = _viewModel.Datasets.Add(datasetPath, spec);
                    await _loader.LoadAsync(entry);
                }

                await WaitForRenderQuiesceAsync(lastEventTicks, expectWork: true);
            }

            // Drive the global clock; the loader re-renders in response,
            // so wait for that pass to settle before framing/capturing.
            if (ApplyStartupTimeStep())
            {
                await WaitForRenderQuiesceAsync(lastEventTicks, expectWork: false);
            }
        }
        finally
        {
            _loader.DatasetLoaded -= handler;
        }

        ApplyStartupOwnShip();
        ApplyStartupViewport();

        if (_screenshotPath is not null)
        {
            // Let the final frame paint on the render thread before we
            // snapshot. Short and fixed — the heavy waiting already
            // happened above on the load/render-quiesce signals.
            await Task.Delay(400);
            await CaptureScreenshotAsync(_screenshotPath);

            if (_closeAfterScreenshot)
            {
                CloseAllDatasets();
            }

            if (_exitAfterScreenshot)
            {
                if (Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
                else
                {
                    Close();
                }
            }
        }
    }

    private void CloseAllDatasets()
    {
        var loaded = _viewModel.Datasets.Entries.ToArray();
        foreach (var entry in loaded)
        {
            _viewModel.Datasets.Entries.Remove(entry);
            _loader.RemoveEntry(entry);
        }
    }

    /// <summary>
    /// Polls until dataset-load / re-render events stop arriving for a
    /// short quiet window, or a hard timeout elapses. Mirrors the
    /// debounce used by the exchange-set zoom path so a single render
    /// quiesce signal drives both framing and capture.
    /// </summary>
    private static async Task WaitForRenderQuiesceAsync(long[] lastEventTicks, bool expectWork)
    {
        const int quietWindowMs = 600;
        const int pollMs = 100;
        const int maxWaitMs = 30_000;

        var startedAt = Environment.TickCount64;
        while (true)
        {
            await Task.Delay(pollMs);

            var lastEvent = Interlocked.Read(ref lastEventTicks[0]);
            var now = Environment.TickCount64;

            if (lastEvent == 0)
            {
                // No events yet. When we expected work, keep waiting up
                // to the cap; otherwise (e.g. a time-step that produced
                // no re-render) return promptly.
                if (!expectWork || now - startedAt >= maxWaitMs) return;
                continue;
            }

            if (now - lastEvent >= quietWindowMs) return;
            if (now - startedAt >= maxWaitMs) return;
        }
    }

    /// <summary>
    /// Applies the <c>--own-ship-pos</c> / <c>--own-ship-cog</c> /
    /// <c>--own-ship-sog</c> startup options by driving the own-ship
    /// helm, if any were supplied. No-op otherwise. Runs after datasets
    /// load and before framing/capture so an agent can snapshot own-ship
    /// in a known kinematic state.
    /// </summary>
    private void ApplyStartupOwnShip()
    {
        if (_startupOptions is not { } options || !options.HasOwnShipOption) return;

        var helm = ResolveOrFallback<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipHelm>(
            static () => null!);
        if (helm is null) return;

        if (options.ParsedOwnShipPosition is { } pos)
        {
            helm.SetState(pos.Latitude, pos.Longitude,
                options.OwnShipCourse, options.OwnShipSpeed);
        }
        else
        {
            if (options.OwnShipCourse is { } cog) helm.SetCourse(cog);
            if (options.OwnShipSpeed is { } sog) helm.SetSpeed(sog);
        }
    }

    /// <summary>
    /// Applies the <c>--center</c>/<c>--zoom</c> or <c>--bbox</c> startup
    /// viewport, if supplied. No-op otherwise. WGS-84 inputs are
    /// projected to the map's web-mercator CRS.
    /// </summary>
    private void ApplyStartupViewport()
    {
        if (_startupOptions is not { } options) return;

        if (options.ParsedBoundingBox is { } bbox)
        {
            var (minX, minY) = SphericalMercator.FromLonLat(bbox.West, bbox.South);
            var (maxX, maxY) = SphericalMercator.FromLonLat(bbox.East, bbox.North);
            var extent = new MRect(minX, minY, maxX, maxY);
            if (extent.Width > 0 && extent.Height > 0)
            {
                _mapHost.SetViewportToExtent(extent);
            }
            return;
        }

        if (options.ParsedCenter is { } center && options.Zoom is { } zoom)
        {
            var (x, y) = SphericalMercator.FromLonLat(center.Longitude, center.Latitude);
            // Standard web-mercator resolution (metres/pixel) at a given
            // 256-pixel-tile zoom level: 156543.03392804097 / 2^zoom.
            var resolution = 156543.03392804097 / Math.Pow(2, zoom);
            _mapHost.SetViewportToCenterAndResolution(new MPoint(x, y), resolution);
        }
    }

    /// <summary>
    /// Applies the <c>--time-step</c> startup option (a zero-based index
    /// into the aggregated time samples, or an ISO-8601 UTC timestamp).
    /// Returns <see langword="true"/> when a time step was applied (and
    /// thus a re-render was triggered).
    /// </summary>
    private bool ApplyStartupTimeStep()
    {
        if (_startupOptions?.TimeStep is not { } raw || string.IsNullOrWhiteSpace(raw))
            return false;

        var globalTime = App.Services.GetService<GlobalTimeService>();
        if (globalTime is null || !globalTime.IsActive)
            return false;

        var samples = globalTime.AllSamples;
        if (samples.Count == 0)
            return false;

        DateTime target;
        if (int.TryParse(raw.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var index))
        {
            index = Math.Clamp(index, 0, samples.Count - 1);
            target = samples[index];
        }
        else if (DateTime.TryParse(raw.Trim(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal
                | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            // Snap to the nearest available sample.
            target = samples.OrderBy(s => Math.Abs((s - parsed).Ticks)).First();
        }
        else
        {
            return false;
        }

        globalTime.SetCurrentTime(target);
        return true;
    }

    private void ApplyAccentColor(Color color)
    {
        _accentColor = color;
        var variant = Application.Current?.ActualThemeVariant;
        var theme = ChromeThemes.FromVariant(variant) ?? ChromeTheme.Light;
        var themed = AccentColors.ForTheme(color, theme);
        Resources["AccentBrush"] = new SolidColorBrush(themed);
        Resources["AccentSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0x33, themed.R, themed.G, themed.B));
    }

    private Task CaptureScreenshotAsync(string outputPath)
    {
        // Full-window capture snapshots the whole window (panels,
        // toolbars, status bar); the default captures just the map.
        Control target = _fullWindowScreenshot ? this : MapControl;
        return _screenshotService.CaptureAsync(
            target,
            outputPath,
            _windowLifetimeCancellation.Token);
    }

    /// <summary>
    /// Updates the map's cursor to reflect the active map tool (Pick Mode
    /// cross-hair, Measure Mode cross-hair, etc.). Called once at startup
    /// and again whenever the active tool changes.
    /// </summary>
    private void ApplyToolCursor()
    {
        MapControl.Cursor = _viewModel.Tools.ActiveTool?.Cursor ?? Cursor.Default;
    }

    /// <summary>
    /// Registers the available <see cref="IMapTool"/>s with the view-model's
    /// <see cref="MapToolController"/>, then initialises the controller with
    /// a context that knows how to add overlay layers, refresh graphics, and
    /// project pointer positions to lat/lon.
    /// </summary>
    private void InitializeMapTools(MapInteractionController interactionController)
    {
        var tools = _viewModel.Tools;

        var context = new MapToolContext(
            mapControl: MapControl,
            addLayer: _mapHost.AddToolLayer,
            removeLayer: _mapHost.RemoveToolLayer,
            setStatusSummary: text => Dispatcher.UIThread.Post(() => _viewModel.MeasureSummary = text),
            refreshGraphics: _mapHost.RequestRedraw,
            screenToLatLon: ScreenToLatLon,
            latLonToScreen: LatLonToScreen);

        // Tools (pick, measure) were registered by the view-model in its
        // constructor; here we just hand them an Avalonia-aware context.
        tools.Initialize(context);

        // Hand the same controller to the interaction controller so pointer
        // events are offered to the active tool first.
        interactionController.SetToolController(tools);

        // The route overlay is persistent (unlike the measure overlay, which
        // only exists while its tool is active): routes stay visible whether
        // or not the editor tool is engaged. The host owns the layer and
        // rebuilds it whenever the route store or theme/accent changes.
        InitializeRouteOverlay();

        // Tool selection is intentionally not persisted across launches —
        // entering Pick or Measure mode must be an explicit user action.
    }

    /// <summary>
    /// Creates the persistent route overlay layer, adds it to the map, and
    /// subscribes to the route store and appearance provider so the overlay
    /// reflects the current routes and theme at all times.
    /// </summary>
    private void InitializeRouteOverlay()
    {
        _routeStore = _viewModel.RoutesService;
        _routeAppearance = App.Services.GetRequiredService<
            EncDotNet.S100.Viewer.Tools.IMeasureOverlayAppearanceProvider>();

        _routeOverlayLayer = EncDotNet.S100.Viewer.Tools.RouteOverlayLayer.Create();
        _mapHost.AddToolLayer(_routeOverlayLayer);

        _routeStore.Changed += OnRouteStoreChanged;
        _routeAppearance.Changed += OnRouteStoreChanged;

        RebuildRouteOverlay();
    }

    private void OnRouteStoreChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RebuildRouteOverlay);

    /// <summary>
    /// Rebuilds the persistent route overlay from the current
    /// <see cref="EncDotNet.S100.Viewer.Services.RoutesService"/> state and
    /// schedules a redraw.
    /// </summary>
    private void RebuildRouteOverlay()
    {
        if (_routeOverlayLayer is null || _routeStore is null || _routeAppearance is null)
            return;

        EncDotNet.S100.Viewer.Tools.RouteOverlayLayer.Update(
            _routeOverlayLayer,
            _routeStore.Routes,
            _routeStore.SelectedWaypointIndex,
            _routeAppearance.Current);
        _mapHost.RequestRedraw();
    }

    /// <summary>
    /// Converts a pointer position (in <see cref="MapControl"/> client
    /// coordinates) to a WGS-84 lat/lon, or <c>null</c> when the pointer
    /// projects to an invalid Mercator location. Mirrors the projection
    /// math used by the mouse lat/lon readout in
    /// <see cref="MapInteractionController"/>.
    /// </summary>
    private GeoPosition? ScreenToLatLon(Point screen)
    {
        if (MapControl.Map?.Navigator is not { } navigator)
            return null;

        var world = navigator.Viewport.ScreenToWorld(screen.X, screen.Y);
        var (lon, lat) = SphericalMercator.ToLonLat(world.X, world.Y);
        if (double.IsNaN(lat) || double.IsNaN(lon) ||
            double.IsInfinity(lat) || double.IsInfinity(lon) ||
            lat < -90.0 || lat > 90.0)
        {
            return null;
        }

        // Normalize longitude into the canonical (-180, 180] range so paths
        // that cross the antimeridian render with consistent endpoints.
        lon = ((lon + 540.0) % 360.0) - 180.0;
        return new GeoPosition(lat, lon);
    }

    /// <summary>
    /// Converts a WGS-84 lat/lon to a pointer position in
    /// <see cref="MapControl"/> client coordinates, or <c>null</c> when no
    /// viewport is available. Inverse of <see cref="ScreenToLatLon"/>; used
    /// by editing tools to hit-test pointer gestures against world-space
    /// features.
    /// </summary>
    private Point? LatLonToScreen(GeoPosition world)
    {
        if (MapControl.Map?.Navigator is not { } navigator)
            return null;

        var (x, y) = SphericalMercator.FromLonLat(world.Longitude, world.Latitude);
        var screen = navigator.Viewport.WorldToScreen(x, y);
        if (double.IsNaN(screen.X) || double.IsNaN(screen.Y) ||
            double.IsInfinity(screen.X) || double.IsInfinity(screen.Y))
        {
            return null;
        }

        return new Point(screen.X, screen.Y);
    }

    private async Task OpenDatasetAsync()
    {
        var paths = await _fileDialog.OpenDatasetsAsync(this, allowMultiple: true);
        if (paths.Count == 0)
            return;

        _viewModel.SelectDefaultTab();

        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;

            await _viewModel.Datasets.LoadFromPathAsync(path);
        }
    }

    private async Task OpenExchangeSetAsync()
    {
        var folder = await _fileDialog.OpenExchangeSetFolderAsync(this);
        if (folder is null)
            return;

        await RunExchangeSetAsync(folder);
    }

    private async Task OpenExchangeSetZipAsync()
    {
        var zip = await _fileDialog.OpenExchangeSetZipAsync(this);
        if (zip is null)
            return;

        await RunExchangeSetAsync(zip);
    }

    private async Task RunExchangeSetAsync(string sourcePath)
    {
        _viewModel.SelectDefaultTab();

        var token = _viewModel.BeginExchangeSetLoad(sourcePath);

        // One progress notification drives the whole open: indeterminate
        // until the catalogue is parsed, then determinate as datasets are
        // dispatched. A Cancel action mirrors the overlay. Failure /
        // cancellation terminal states are driven in place by the exchange-set
        // service; the success / partial terminal is deferred and driven here
        // (see DriveExchangeSetTerminalAsync) once the cells are visible.
        var notification = App.Services.GetRequiredService<INotificationService>()
            .Create(Strings.Toast_ExchangeSetLoading)
            .WithSeverity(NotificationSeverity.Info)
            .WithContent(Services.Notifications.NotificationFormat.ShortenPath(sourcePath))
            .AsProgress(indeterminate: true)
            .Persistent()
            .WithAction(
                Strings.Toast_Cancel,
                () => _viewModel.CancelExchangeSetCommand.Execute(null),
                dismissOnInvoke: false)
            .Show();

        var progress = new Progress<Services.ExchangeSetProgress>(p =>
        {
            // Stay indeterminate until at least one cell has actually finished
            // loading. The exchange-set service reports Completed based on real
            // load completions (not dispatch), so a single-cell set animates as
            // indeterminate for its whole load, and multi-cell sets fill as each
            // cell lands — never racing to 100% during the instant dispatch.
            if (!notification.IsDismissed && p.Total > 0 && p.Completed > 0)
            {
                notification.Report((double)p.Completed / p.Total);
            }
        });

        // Subscribe to per-dataset load completions for the duration of
        // this open. We accumulate each loaded entry's layer extents so
        // we can zoom to their union — the catalogue may not declare
        // per-dataset bounding boxes (S-101 producer dumps often skip
        // them) and Map.Extent alone could include unrelated layers.
        var loadedEntries = new HashSet<DatasetEntry>();
        var unionSlot = new MRect?[1];
        var lastEventTicks = new long[1];
        Action<DatasetEntry> handler = entry =>
        {
            // Only count entries from any exchange set — we filter to
            // this specific open below by checking the entry's Source.
            if (!entry.IsFromExchangeSet) return;
            if (!loadedEntries.Add(entry)) return;
            if (_loader.EntryLayers.TryGetValue(entry, out var layers))
            {
                foreach (var layer in layers)
                {
                    if (layer.Extent is { } e && e.Width > 0 && e.Height > 0)
                    {
                        unionSlot[0] = unionSlot[0] is null
                            ? new MRect(e.MinX, e.MinY, e.MaxX, e.MaxY)
                            : unionSlot[0]!.Join(e);
                    }
                }
            }
            Interlocked.Exchange(ref lastEventTicks[0], Environment.TickCount64);
        };
        _loader.DatasetLoaded += handler;

        try
        {
            // Frame the viewport as soon as the service knows the catalogue's
            // union bounding box — before any dataset finishes loading — so
            // incremental per-dataset paints appear in the correctly-framed
            // view instead of off-screen (issue #448). The service resumes on
            // this UI thread (ConfigureAwait(true)) before invoking the
            // callback, so we frame inline here; a Dispatcher.UIThread.Invoke
            // fallback covers any off-thread invocation. A flag lets us skip
            // the redundant end-of-load reframe below.
            var framedEarly = false;
            void FrameEarly(EncDotNet.S100.ExchangeSets.BoundingBox bbox)
            {
                ZoomToCatalogueBoundingBox(bbox);
                framedEarly = true;
            }

            Action<EncDotNet.S100.ExchangeSets.BoundingBox> onFramingReady = bbox =>
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    FrameEarly(bbox);
                }
                else
                {
                    Dispatcher.UIThread.Invoke(() => FrameEarly(bbox));
                }
            };

            var result = await _exchangeSetService.OpenAsync(
                sourcePath, progress, token, notification, onFramingReady);
            _viewModel.EndExchangeSetLoad(result);

            // Frame the loaded cells. If early framing already ran, skip the
            // reframe: the early and final union bounding boxes are computed
            // from the same immutable catalogue metadata, so they are identical
            // and a second zoom would only be a jarring no-op. Otherwise prefer
            // the catalogue's union bbox when available; failing that, debounce
            // on DatasetLoaded events (zoom once no new event has arrived for
            // QuietWindowMs), which naturally handles per-dataset load failures
            // (which never raise the event) without waiting a fixed timeout.
            if (framedEarly)
            {
                // Already framed up front — nothing to do.
            }
            else if (result.UnionBoundingBox is { } bbox)
            {
                ZoomToCatalogueBoundingBox(bbox);
            }
            else
            {
                await ZoomWhenLoadingQuietsAsync(loadedEntries, unionSlot, lastEventTicks);
            }

            // The cells are loaded and framed; hold the progress notification
            // until the map has actually painted them, then drive it to its
            // terminal "loaded" state. This keeps the success notification from
            // ever preceding the charts becoming visible. Failure / cancelled
            // outcomes were driven immediately inside OpenAsync and leave
            // PendingTerminal null, so this is a no-op for them.
            await DriveExchangeSetTerminalAsync(notification, result, token);
        }
        catch (Exception ex)
        {
            _viewModel.EndExchangeSetLoad(new Services.ExchangeSetOpenResult
            {
                SourcePath = sourcePath,
                FailureMessage = ex.Message,
            });

            // OpenAsync handles its own failures and drives the
            // notification's terminal state; this is a safety net for
            // anything thrown afterwards (e.g. the post-load zoom).
            if (!notification.IsDismissed)
            {
                notification.ClearProgress();
                notification.SetActions();
                notification.Update(
                    title: Strings.Toast_ExchangeSetFailed,
                    message: ex.Message,
                    severity: NotificationSeverity.Error);
                notification.ScheduleAutoDismiss(
                    NotificationService.DefaultDelayFor(NotificationSeverity.Error));
            }
        }
        finally
        {
            _loader.DatasetLoaded -= handler;
        }
    }

    /// <summary>
    /// Drives the shared exchange-set progress notification to its deferred
    /// terminal state (<see cref="Services.ExchangeSetOpenResult.PendingTerminal"/>)
    /// once the loaded cells have been framed and the map has painted them,
    /// so the "loaded" notification never precedes the charts becoming
    /// visible. A no-op when the outcome has no pending terminal (failure /
    /// cancellation, already driven inside <c>OpenAsync</c>) or the
    /// notification was dismissed by the user. The render-idle wait is bounded
    /// by a timeout so a slow basemap never blocks the notification.
    /// </summary>
    private async Task DriveExchangeSetTerminalAsync(
        Services.Notifications.INotificationHandle notification,
        Services.ExchangeSetOpenResult result,
        CancellationToken token)
    {
        if (result.PendingTerminal is not { } pending || notification.IsDismissed)
            return;

        if (_renderActivityMonitor is not null)
        {
            try
            {
                await _renderActivityMonitor
                    .WaitForIdleAsync(
                        TimeSpan.FromMilliseconds(200),
                        TimeSpan.FromSeconds(8),
                        token)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Still settle the notification below — the cells are loaded.
            }
        }

        if (notification.IsDismissed)
            return;

        notification.ClearProgress();
        notification.SetActions();
        notification.Update(
            title: pending.Title, message: pending.Message, severity: pending.Severity);
        notification.ScheduleAutoDismiss(
            NotificationService.DefaultDelayFor(pending.Severity));
    }

    private void ZoomToCatalogueBoundingBox(EncDotNet.S100.ExchangeSets.BoundingBox bbox)
    {
        // EPSG:4326 lat/lon → web mercator. SphericalMercator clamps
        // the input range, so polar catalogues degrade gracefully.
        var (minX, minY) = SphericalMercator.FromLonLat(
            bbox.WestBoundLongitude, bbox.SouthBoundLatitude);
        var (maxX, maxY) = SphericalMercator.FromLonLat(
            bbox.EastBoundLongitude, bbox.NorthBoundLatitude);
        var extent = new MRect(minX, minY, maxX, maxY);
        if (extent.Width > 0 && extent.Height > 0)
        {
            _mapHost.ZoomToExtent(extent, durationMilliseconds: 250);
        }
    }

    private async Task ZoomWhenLoadingQuietsAsync(
        HashSet<DatasetEntry> loadedEntries,
        MRect?[] unionSlot,
        long[] lastEventTicks)
    {
        // Quiet-window debounce: the per-dataset loaders complete on
        // their own background tasks and may fail silently (caught and
        // logged inside DatasetLoaderService), so we can't simply
        // await an exact count. Instead we wait until DatasetLoaded
        // events stop arriving for a short window — at that point
        // every dispatched dataset has either completed or errored,
        // and the accumulated union extent is final.
        const int quietWindowMs = 600;
        const int pollMs = 100;
        const int maxWaitMs = 30_000;

        var startedAt = Environment.TickCount64;
        while (true)
        {
            await Task.Delay(pollMs);

            var lastEvent = Interlocked.Read(ref lastEventTicks[0]);
            var now = Environment.TickCount64;

            // No events yet — keep waiting up to maxWaitMs from start.
            if (lastEvent == 0)
            {
                if (now - startedAt >= maxWaitMs) return;
                continue;
            }

            // We've seen at least one event; trigger as soon as the
            // bus is quiet for quietWindowMs.
            if (now - lastEvent >= quietWindowMs)
                break;

            if (now - startedAt >= maxWaitMs)
                break;
        }

        var extent = unionSlot[0] ?? MapControl.Map.Extent;
        if (extent is null || extent.Width <= 0 || extent.Height <= 0) return;

        _mapHost.ZoomToExtent(extent, durationMilliseconds: 250);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { } files)
            return;

        _viewModel.SelectDefaultTab();

        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (path is null)
                continue;

            // Folder drop: treat as an exchange set when CATALOG.XML
            // (S-100) or CATALOG.031 (S-57) is at the root. Otherwise, a
            // catalogue-less folder of loose ENC cells (a base ….000 plus
            // its updates) is scanned and loaded; anything else raises a
            // notification rather than being silently ignored.
            if (Directory.Exists(path))
            {
                if (ExchangeSetDetection.LooksLikeExchangeSetFolder(path)
                    || ExchangeSetDetection.LooksLikeS57ExchangeSetFolder(path)
                    || ExchangeSetDetection.LooksLikeLooseCellFolder(path))
                {
                    await RunExchangeSetAsync(path);
                }
                else
                {
                    App.Services.GetRequiredService<INotificationService>()
                        .Create(Strings.Toast_Warning)
                        .WithSeverity(NotificationSeverity.Warning)
                        .WithContent(string.Format(Strings.Status_FolderNoDatasets, path))
                        .Show();
                }
                continue;
            }

            if (!File.Exists(path))
                continue;

            // File drop: a .zip with a root-level CATALOG.XML is an
            // exchange-set ZIP, and a dropped CATALOG.031 is an S-57
            // exchange set; everything else falls through to the
            // single-dataset loader.
            if (ExchangeSetDetection.IsZipPath(path) &&
                ExchangeSetDetection.LooksLikeExchangeSetZip(path))
            {
                await RunExchangeSetAsync(path);
                continue;
            }

            if (ExchangeSetDetection.IsS57CataloguePath(path))
            {
                await RunExchangeSetAsync(path);
                continue;
            }

            await _viewModel.Datasets.LoadFromPathAsync(path);
        }
    }
}
