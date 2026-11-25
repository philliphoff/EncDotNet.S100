using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Services.Aml;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Projections;
using Microsoft.Extensions.DependencyInjection;
using DynamicSourceHost = EncDotNet.S100.Renderers.Mapsui.DynamicSources.S100DynamicSourceHost;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The services side of one map (#1729, dual-map design note §2.1): the
/// <see cref="MapsuiMapHost"/> over a <see cref="MapPane"/>'s control, the
/// overlays drawn on that map, the pointer interaction and map tools, the
/// route-check hit testing, and the render-activity and settle probes.
/// Disposing the view tears all of it down.
/// </summary>
/// <remarks>
/// Built in two steps so the window can publish the host as the map
/// capabilities and initialise the dataset loader with it before the
/// overlays are attached, in the order the window always used: construct
/// (host only), then <see cref="Attach"/>.
/// </remarks>
internal sealed class MapView : IDisposable
{
    /// <summary>Within this many pixels of the checked route, the pointer moves the profile cursor.</summary>
    private const double CheckHoverRadiusPx = 12;

    private readonly IServiceProvider _services;
    private readonly List<IDisposable> _dynamicSourceRegistrations = new();

    private MainViewModel? _viewModel;
    private ViewerSettingsService? _viewerSettingsService;
    private ValidationOverlayService? _validationOverlay;
    private RenderActivityMonitor? _renderActivityMonitor;
    private ViewSettleMonitor? _settleMonitor;
    private Map? _renderActivityMap;
    private EventHandler? _renderActivityRefreshHandler;
    private DynamicSourceHost? _dynamicSourceOverlayHost;
    private PickHighlightController? _pickHighlightController;
    private LegendFindOverlayController? _legendFindController;
    private DatasetExtentIndicatorController? _extentIndicatorController;
    private AmlCoverageOutlineController? _amlOutlineController;
    private LibraryCoverageOverlayController? _libraryCoverageController;
    private AdjustedAreaBoundaryController? _adjustedAreaBoundary;
    private OverscaleCurtainController? _overscaleCurtainController;
    private Mapsui.Layers.MemoryLayer? _routeOverlayLayer;
    private IMeasureOverlayAppearanceProvider? _routeAppearance;
    private RoutesService? _routeStore;
    private PassageCheckService? _passageCheck;
    private RouteFindingsService? _routeFindings;
    private IMarinerSettingsProvider? _routeMariner;
    private ITimeFormatProvider? _routeTimeFormat;
    private float _routeCheckLegendReserve;
    private bool _disposed;

    /// <summary>
    /// Creates the view's map host over <paramref name="pane"/>'s control.
    /// Call <see cref="Attach"/> to wire the overlays and interaction.
    /// </summary>
    /// <param name="id">The view's stable id.</param>
    /// <param name="pane">The pane holding the map control.</param>
    /// <param name="services">The application services.</param>
    public MapView(MapViewId id, MapPane pane, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(services);

        Id = id;
        Pane = pane;
        _services = services;

        // The host sets up S-100 through the public mapControl.AddS100 entry
        // point (the same one the MapHost sample uses), sharing the DI
        // processor owner and dataset renderer as borrowed collaborators so
        // other Viewer services keep acquiring leases / rendering on the same
        // instances.
        Host = new MapsuiMapHost(
            pane.MapControl,
            services.GetRequiredService<DatasetProcessorOwner>(),
            services.GetRequiredService<EncDotNet.S100.Renderers.Mapsui.MapsuiDatasetRenderer>(),
            services.GetRequiredService<
                EncDotNet.S100.Datasets.Pipelines.Interoperability.IInteroperabilityAuthorityProvider>(),
            services.GetRequiredService<EncDotNet.S100.Rendering.Engine.Prediction.DemandPlanner>());
    }

    /// <summary>The view's stable id.</summary>
    public MapViewId Id { get; }

    /// <summary>The pane holding this view's map control.</summary>
    public MapPane Pane { get; }

    /// <summary>The map host over the pane's control.</summary>
    public MapsuiMapHost Host { get; }

    /// <summary>
    /// The render-activity monitor fed by this map, or <see langword="null"/>
    /// before <see cref="Attach"/> or when the control has no map.
    /// </summary>
    public RenderActivityMonitor? RenderActivity => _renderActivityMonitor;

    /// <summary>
    /// Wires the overlays, probes, interaction and map tools to the map.
    /// </summary>
    /// <param name="viewModel">The window's view-model.</param>
    /// <param name="loader">The dataset loader.</param>
    /// <param name="pickService">The pick service.</param>
    /// <param name="navigation">The zoom buttons and compass that drive this map.</param>
    public void Attach(
        MainViewModel viewModel,
        IDatasetLoaderService loader,
        IPickService pickService,
        MapNavigationControls navigation)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(pickService);
        ArgumentNullException.ThrowIfNull(navigation);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_viewModel is not null)
            throw new InvalidOperationException("The map view is already attached.");

        _viewModel = viewModel;
        var services = _services;
        var mapControl = Pane.MapControl;

        // Build the validation findings overlay layer that draws above
        // all dataset layers for the currently-selected dataset. The
        // service subscribes to the datasets view-model and lives for
        // the lifetime of the view.
        _validationOverlay = new ValidationOverlayService(Host, viewModel.Datasets);

        // Apply persisted scale-bar distance unit and react to changes.
        _viewerSettingsService = services.GetRequiredService<ViewerSettingsService>();
        Pane.ScaleBar.Unit = _viewerSettingsService.DistanceUnit;
        _viewerSettingsService.DistanceUnitChanged += OnDistanceUnitChanged;

        // Basemap (issue #295). Default Offline (bundled Natural Earth
        // land — zero network); the user can switch to None or Online in
        // Settings (or via --basemap). Keep a reference so swapping mode
        // can replace it live. Always sits at index 0, beneath datasets.
        Host.SetBasemapLayer(
            BasemapLayerFactory.TryCreate(_viewerSettingsService.SelectedBasemapMode));
        _viewerSettingsService.BasemapModeChanged += OnBasemapModeChanged;

        // ENC water colour (S-52 / S-101 DEPDW) — used as the map control
        // background so the unrendered area outside the tile layer's
        // extent visually blends with the chart's water.
        if (mapControl.Map is { } mapForBackColor)
        {
            mapForBackColor.BackColor = new Mapsui.Styles.Color(170, 211, 223);
        }

        // Wire the render-activity monitor that backs the MCP
        // 'await_render_idle' / 'get_render_stats' tools. The
        // InstrumentedMapControl feeds paint stats through the static
        // RenderActivityHub.Sink; here we additionally feed Mapsui's
        // graphics-refresh signal as activity and expose a layer-busy
        // probe so idle is not reported while an async fetch is pending.
        if (mapControl.Map is { } activityMap)
        {
            var monitor = services.GetRequiredService<RenderActivityMonitor>();
            _renderActivityMonitor = monitor;
            _renderActivityMap = activityMap;
            RenderActivityHub.Sink = monitor;

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

            // The tile half of the settled signal behind the MCP await_settled
            // tool (map rendering slice 1). Read on the MCP request's thread from
            // a snapshot of the layers; inert until a harness turns tracking on.
            var settle = services.GetRequiredService<ViewSettleMonitor>();
            _settleMonitor = settle;
            settle.TileProbe = () => EncDotNet.S100.Renderers.Mapsui.MapsuiChartLayers.EvaluateSettle(
                activityMap.Layers.ToArray());
            settle.RequestRedraw = activityMap.RefreshGraphics;
        }

        // The background vector-snapshot / scene / tile renderers repaint through
        // a per-session redraw sink the session stamps onto each dataset layer
        // (wired via MapsuiMapHost from the Avalonia adapter's UI-thread
        // RefreshGraphics), so the view doesn't set process-global hooks.

        // Bind the map-viewport notifier as early as possible so the
        // AIS overlay's zoom-gated decorator (resolved below via
        // GetServices<IDynamicFeatureSource>) can read the current
        // viewport synchronously in its constructor. See
        // docs/design/ais-zoom-gated-subscription.md.
        if (mapControl.Map?.Navigator is { } notifierNav)
        {
            services.GetRequiredService<MapViewportNotifier>().Bind(notifierNav);

            // Clamp zoom in/out so the user cannot zoom to an unbounded,
            // meaningless scale (e.g. many world copies off the edge of a
            // cross-antimeridian dataset, or arbitrarily deep past chart
            // resolution).
            MapZoomLimits.Apply(notifierNav);
        }

        AttachDynamicSources();
        AttachOverlays(viewModel, loader);

        // Disable Mapsui's built-in LoggingWidget — it can throw "minX > maxX" on
        // narrow viewports during resize, and the exception is raised on the
        // render thread where we cannot intercept it.
        Mapsui.Widgets.InfoWidgets.LoggingWidget.ShowLoggingInMap = Mapsui.Widgets.ActiveMode.No;
        if (mapControl.Map is { } mapForWidgets)
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
            viewModel,
            pickService,
            loader,
            services.GetService<DynamicSources.IDynamicSourcePickService>())
        {
            DepthBasis = services.GetService<DepthBasisService>(),
            Viewport = Host,
            ReferenceMenu = services.GetService<ChartReference.ReferenceTerms>() is { } terms
                ? new ChartReference.MapReferenceMenu(terms)
                : null,
        };
        interactionController.Attach(
            mapControl,
            navigation.ZoomIn,
            navigation.ZoomOut,
            navigation.ZoomToExtent,
            Pane.ScaleBar,
            navigation.Compass);
        interactionController.PlainTapped += (_, tap) => _libraryCoverageController?.HandleTap(tap);

        // Wire the map-tool controller to the map: tools are registered with
        // the view-model's controller and pointer events are forwarded by
        // the interaction controller.
        InitializeMapTools(interactionController);

        // Apply the cursor that matches the current mode and keep it in sync
        // with the active tool.
        ApplyToolCursor();
        viewModel.MapTools.Tools.ActiveToolChanged += OnActiveToolChanged;
    }

    private void AttachDynamicSources()
    {
        var services = _services;

        // PR-D2: dynamic-source overlay host. Registered *after* the
        // basemap so MapsuiMapHost's ComputeOverlayInsertIndex places
        // the overlay above the OSM tile layer rather than at index 0
        // (where the subsequently-added basemap would cover it).
        var host = new DynamicSourceHost(
            Host,
            rendererKey => rendererKey is null
                ? null
                : services.GetKeyedService<EncDotNet.S100.Renderers.Mapsui.DynamicSources.IDynamicFeatureRenderer>(rendererKey),
            marshal: action =>
            {
                if (Dispatcher.UIThread.CheckAccess()) action();
                else Dispatcher.UIThread.Post(action);
            },
            logger: services.GetService<Microsoft.Extensions.Logging.ILogger<DynamicSourceHost>>());
        _dynamicSourceOverlayHost = host;

        // PR-D2.1: seed per-source visibility from persisted settings
        // *before* the Register loop so each source's MemoryLayer.Enabled
        // starts in the user's last-known state, then wire write-back so
        // the Layer Stack panel's visibility toggle persists.
        var viewerSettings = services.GetRequiredService<ViewerSettings>();
        foreach (var kv in viewerSettings.DynamicSourceVisibility)
        {
            host.SetVisible(kv.Key, kv.Value);
        }
        host.SourcesChanged += () =>
        {
            foreach (var info in host.Sources)
            {
                viewerSettings.DynamicSourceVisibility[info.Id] = host.GetVisible(info.Id);
            }
            try { viewerSettings.Save(); } catch { /* best-effort */ }
        };

        // Attach the registry to the accessor so view-models resolved
        // before window construction (e.g. LayerStackViewModel as a
        // singleton) start seeing dynamic sources.
        services.GetRequiredService<DynamicSources.DynamicFeatureSourceRegistryAccessor>().Current = host;

        foreach (var source in services.GetServices<EncDotNet.S100.DynamicSources.IDynamicFeatureSource>())
        {
            _dynamicSourceRegistrations.Add(host.Register(source));
        }
    }

    /// <summary>
    /// Adds the overlay controllers in the order they stack on the overlay tier.
    /// </summary>
    private void AttachOverlays(MainViewModel viewModel, IDatasetLoaderService loader)
    {
        var services = _services;
        var appearance = services.GetRequiredService<IMeasureOverlayAppearanceProvider>();
        var viewerSettingsService = services.GetRequiredService<ViewerSettingsService>();

        // Pick highlight: keep a cursor-echo marker + selected-feature outline
        // on the overlay tier in sync with the current pick report, so the
        // pick stays visible as the user (or an MCP agent) pans the map.
        _pickHighlightController = new PickHighlightController(
            Host,
            services.GetRequiredService<PickReportViewModel>(),
            services.GetRequiredService<ViewerDatasetCatalog>(),
            appearance,
            viewerSettingsService,
            UiThread.RunOrPost,
            warningDrawing: services.GetService<NavigationalWarningDrawingService>());

        // Hover to find (#1412, legends D19): ring a hovered Legend row's features.
        _legendFindController = new LegendFindOverlayController(
            Host,
            services.GetRequiredService<LegendFindService>(),
            appearance,
            viewerSettingsService,
            UiThread.RunOrPost);

        // Out-of-scale extent indicators: outline the extents of loaded
        // datasets that have zoomed out past their display-scale minimum, so a
        // wide-spread exchange set still shows where its members are (#446).
        _extentIndicatorController = new DatasetExtentIndicatorController(
            Host,
            viewModel.Datasets,
            appearance,
            viewerSettingsService,
            UiThread.RunOrPost,
            UiThread.Post);

        // AML coverage outlines (#1371): where AML cells that aren't drawn
        // (or are hidden) lie, as an ink frame with inward ticks.
        _amlOutlineController = new AmlCoverageOutlineController(
            Host,
            viewModel.Datasets,
            appearance,
            UiThread.RunOrPost,
            UiThread.Post);

        // Timeline lanes (#710): the hovered lane's footprint.
        services.GetRequiredService<TimelineMapScope>().Attach(Host);

        // Library coverage (issue #655): outline the datasets listed in the
        // Library panel — without loading them — while that panel is showing.
        _libraryCoverageController = new LibraryCoverageOverlayController(
            Host,
            services.GetRequiredService<LibraryPanelViewModel>(),
            viewModel.Layout,
            services.GetRequiredService<IMapViewportNotifier>(),
            appearance,
            () => services.GetRequiredService<MapCapabilityAccessor<IMapViewportController>>().Current,
            UiThread.RunOrPost);
        // Legends D8: its outline styles show as the Legend tab's Library coverage section.
        services.GetRequiredService<LegendService>().AddSource(_libraryCoverageController);

        // Chart-wide water level adjustment (#1132): the edge of the adjusted area.
        _adjustedAreaBoundary = new AdjustedAreaBoundaryController(
            Host,
            services.GetRequiredService<IWaterLevelAdjustmentState>(),
            // D23: clipped to the loaded charts' water.
            () => services.GetRequiredService<ViewerWaterLevelCatalog>().WaterMask);

        // On-chart overscale curtain: paint a subtle vertical-line pattern over
        // the region of each cell displayed beyond its compilation scale (#441).
        _overscaleCurtainController = new OverscaleCurtainController(
            Host,
            viewModel.Datasets,
            loader,
            services.GetRequiredService<IMapViewportNotifier>(),
            viewerSettingsService,
            UiThread.RunOrPost);
    }

    /// <summary>
    /// Sets how far the route check's map legend reaches up from the map's
    /// bottom edge, so the finding chip the overlay draws sits above it.
    /// </summary>
    /// <param name="reserve">The height in pixels, or 0 while the legend is hidden.</param>
    public void SetRouteCheckLegendReserve(float reserve)
    {
        if (Math.Abs(reserve - _routeCheckLegendReserve) > 0.5f)
        {
            _routeCheckLegendReserve = reserve;
            RebuildRouteOverlay();
        }
    }

    private void OnDistanceUnitChanged(DistanceUnit unit) => Pane.ScaleBar.Unit = unit;

    /// <summary>
    /// Swaps the basemap layer in response to the Settings basemap-mode
    /// selector (issue #295). The basemap always sits at the bottom of
    /// the layer stack (index 0) beneath every dataset and overlay; a new
    /// mode removes the old layer and inserts a fresh one (or none for
    /// <see cref="BasemapMode.None"/>).
    /// </summary>
    private void OnBasemapModeChanged(BasemapMode mode)
    {
        Host.SetBasemapLayer(BasemapLayerFactory.TryCreate(mode));
        Host.RequestRedraw();
    }

    private void OnActiveToolChanged(IMapTool? tool) => Dispatcher.UIThread.Post(ApplyToolCursor);

    /// <summary>
    /// Updates the map's cursor to reflect the active map tool (Pick Mode
    /// cross-hair, Measure Mode cross-hair, etc.). Called once at startup
    /// and again whenever the active tool changes.
    /// </summary>
    private void ApplyToolCursor()
    {
        if (_viewModel is null || _disposed)
            return;
        Pane.MapControl.Cursor = _viewModel.MapTools.Tools.ActiveTool?.Cursor ?? Cursor.Default;
    }

    /// <summary>
    /// Initialises the view-model's <see cref="MapToolController"/> (whose
    /// tools the view-model registered) with a context that knows how to add
    /// overlay layers, refresh graphics, and project pointer positions to
    /// lat/lon.
    /// </summary>
    private void InitializeMapTools(MapInteractionController interactionController)
    {
        var viewModel = _viewModel!;
        var tools = viewModel.MapTools.Tools;

        var context = new MapToolContext(
            mapControl: Pane.MapControl,
            addLayer: Host.AddToolLayer,
            removeLayer: Host.RemoveToolLayer,
            setStatusSummary: text => Dispatcher.UIThread.Post(() => viewModel.MapTools.MeasureSummary = text),
            refreshGraphics: Host.RequestRedraw,
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
        // or not the editor tool is engaged. The view owns the layer and
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
        var services = _services;
        _routeStore = _viewModel!.RoutesService;
        _routeAppearance = services.GetRequiredService<IMeasureOverlayAppearanceProvider>();

        _routeOverlayLayer = RouteOverlayLayer.Create();
        Host.AddToolLayer(_routeOverlayLayer);

        _routeStore.Changed += OnRouteStoreChanged;
        _routeStore.EditModeChanged += OnRouteStoreChanged;
        _routeStore.CheckModeChanged += OnRouteStoreChanged;
        _routeAppearance.Changed += OnRouteStoreChanged;

        // Check passage (#901): the route is coloured by UKC state while the
        // mode is on, and redrawn whenever a result (or the cursor) changes.
        _passageCheck = services.GetService<PassageCheckService>();
        _routeMariner = services.GetService<IMarinerSettingsProvider>();
        _routeTimeFormat = services.GetService<ITimeFormatProvider>();
        if (_passageCheck is not null)
        {
            _passageCheck.Changed += OnRouteStoreChanged;

            // #902: hovering the checked route moves the Clearance cursor (and the ghost).
            Pane.MapControl.AddHandler(InputElement.PointerMovedEvent, OnMapPointerMovedForCheck, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        // #905: the selected finding is outlined on the map with its chip.
        _routeFindings = services.GetService<RouteFindingsService>();
        if (_routeFindings is not null)
            _routeFindings.Changed += OnRouteStoreChanged;

        // #959 C6: clicking a finding's marker selects it; clicking a cluster zooms in.
        Pane.MapControl.AddHandler(InputElement.PointerPressedEvent, OnMapPointerPressedForCheck, RoutingStrategies.Tunnel);

        RebuildRouteOverlay();
    }

    private void OnMapPointerPressedForCheck(object? sender, PointerPressedEventArgs e)
    {
        var mapControl = Pane.MapControl;
        if (_routeOverlayLayer is null || _routeFindings is null
            || !e.GetCurrentPoint(mapControl).Properties.IsLeftButtonPressed
            || RouteOverlayLayer.SceneOf(_routeOverlayLayer).Check is not { } check)
            return;

        var placement = RouteCheckMapLayout.Place(check.Map, LatLonToScreen);
        if (RouteCheckMapLayout.HitTest(placement, e.GetPosition(mapControl)) is not { } hit)
            return;

        e.Handled = true;
        if (hit.Marker is { } marker)
        {
            if (_routeFindings.Findings.FirstOrDefault(f => f.Id == marker.Mark.FindingId) is { } finding)
                _routeFindings.Select(finding, zoom: false);
        }
        else if (hit.Cluster is { } cluster && mapControl.Map?.Navigator is { } navigator)
        {
            // Two zoom steps on the cluster: its markers are within 24 px, so
            // at four times the scale they stand apart.
            var world = navigator.Viewport.ScreenToWorld(cluster.Center.X, cluster.Center.Y + RouteCheckMapLayout.StemLength);
            Host.AnimateToCenterAndResolution(world, navigator.Viewport.Resolution / 4, 250);
        }
    }

    private void OnMapPointerMovedForCheck(object? sender, PointerEventArgs e)
    {
        if (_passageCheck is not { IsActive: true, Current: { } current })
            return;

        var pointer = e.GetPosition(Pane.MapControl);
        double? nearest = null;
        var best = CheckHoverRadiusPx * CheckHoverRadiusPx;
        foreach (var sample in current.Result.Samples)
        {
            if (LatLonToScreen(sample.Position) is not { } p)
                continue;
            var dx = p.X - pointer.X;
            var dy = p.Y - pointer.Y;
            var d2 = (dx * dx) + (dy * dy);
            if (d2 < best)
            {
                best = d2;
                nearest = sample.DistanceMeters;
            }
        }

        if (nearest is { } distance)
            _passageCheck.SetCursor(distance);
    }

    private void OnRouteStoreChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RebuildRouteOverlay);

    /// <summary>
    /// Rebuilds the persistent route overlay from the current
    /// <see cref="RoutesService"/> state and schedules a redraw.
    /// </summary>
    private void RebuildRouteOverlay()
    {
        if (_disposed || _routeOverlayLayer is null || _routeStore is null || _routeAppearance is null)
            return;

        RouteOverlayLayer.Update(
            _routeOverlayLayer,
            _routeStore.Routes,
            _routeStore.SelectedWaypointIndex,
            _routeStore.SelectedLegIndex,
            _routeStore.IsEditModeActive,
            _routeAppearance.Current,
            RouteCheckOverlay.Build(
                _routeStore.IsCheckModeActive && _passageCheck is not null,
                _passageCheck?.Current,
                _passageCheck?.IsStale ?? false,
                _passageCheck?.Cursor,
                _routeAppearance.Current,
                _routeMariner?.Current.DepthUnit ?? EncDotNet.S100.Pipelines.DepthUnit.Metres,
                _routeTimeFormat?.Current ?? TimeFormat.Local,
                _routeFindings?.Selected,
                _routeFindings?.Findings,
                hasDraft: _passageCheck?.Input is not { HasDraft: false },
                route: _routeStore.Routes.ActiveRoute,
                viewTime: _routeStore.ViewTime.Current) is { } check
                ? check with { LegendReserve = _routeCheckLegendReserve }
                : null,
            // Native-mode received routes are drawn by their dataset (#960).
            route => ReceivedRoutesService.DrawsWithRouteRenderer(_routeStore, route));
        Host.RequestRedraw();
    }

    /// <summary>
    /// Converts a pointer position (in the pane's map control client
    /// coordinates) to a WGS-84 lat/lon, or <c>null</c> when the pointer
    /// projects to an invalid Mercator location. Mirrors the projection
    /// math used by the mouse lat/lon readout in
    /// <see cref="MapInteractionController"/>.
    /// </summary>
    private GeoPosition? ScreenToLatLon(Point screen)
    {
        if (Pane.MapControl.Map?.Navigator is not { } navigator)
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
    /// Converts a WGS-84 lat/lon to a pointer position in the pane's map
    /// control client coordinates, or <c>null</c> when no viewport is
    /// available. Inverse of <see cref="ScreenToLatLon"/>; used by editing
    /// tools to hit-test pointer gestures against world-space features.
    /// </summary>
    private Point? LatLonToScreen(GeoPosition world)
    {
        if (Pane.MapControl.Map?.Navigator is not { } navigator)
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

    /// <summary>
    /// Detaches the probes, overlays, dynamic sources and subscriptions this
    /// view made, then disposes the map host.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var services = _services;

        _validationOverlay?.Dispose();
        _validationOverlay = null;

        // Detach render-activity wiring so the static hub does not
        // outlive the view and a torn-down map is not probed.
        RenderActivityHub.Sink = null;
        if (_renderActivityMonitor is not null)
        {
            _renderActivityMonitor.BusyProbe = null;
            if (_settleMonitor is not null)
            {
                _settleMonitor.TileProbe = null;
                _settleMonitor.RequestRedraw = null;
                _settleMonitor = null;
            }
            if (_renderActivityMap is not null && _renderActivityRefreshHandler is not null)
            {
                _renderActivityMap.RefreshGraphicsRequest -= _renderActivityRefreshHandler;
            }
            _renderActivityMonitor = null;
            _renderActivityMap = null;
            _renderActivityRefreshHandler = null;
        }

        if (_viewerSettingsService is not null)
        {
            _viewerSettingsService.DistanceUnitChanged -= OnDistanceUnitChanged;
            _viewerSettingsService.BasemapModeChanged -= OnBasemapModeChanged;
        }
        if (_viewModel is not null)
            _viewModel.MapTools.Tools.ActiveToolChanged -= OnActiveToolChanged;
        if (_routeStore is not null)
        {
            _routeStore.Changed -= OnRouteStoreChanged;
            _routeStore.EditModeChanged -= OnRouteStoreChanged;
            _routeStore.CheckModeChanged -= OnRouteStoreChanged;
        }
        if (_routeAppearance is not null)
            _routeAppearance.Changed -= OnRouteStoreChanged;
        if (_passageCheck is not null)
            _passageCheck.Changed -= OnRouteStoreChanged;
        if (_routeFindings is not null)
            _routeFindings.Changed -= OnRouteStoreChanged;

        foreach (var reg in _dynamicSourceRegistrations) reg.Dispose();
        _dynamicSourceRegistrations.Clear();
        if (_dynamicSourceOverlayHost is not null)
        {
            var registry = services.GetRequiredService<DynamicSources.DynamicFeatureSourceRegistryAccessor>();
            if (ReferenceEquals(registry.Current, _dynamicSourceOverlayHost))
                registry.Current = null;
        }
        _dynamicSourceOverlayHost?.Dispose();
        _dynamicSourceOverlayHost = null;
        _pickHighlightController?.Dispose();
        _pickHighlightController = null;
        _legendFindController?.Dispose();
        _legendFindController = null;
        _extentIndicatorController?.Dispose();
        _extentIndicatorController = null;
        _amlOutlineController?.Dispose();
        _amlOutlineController = null;
        if (_libraryCoverageController is not null)
            services.GetService<LegendService>()?.RemoveSource(_libraryCoverageController);
        _libraryCoverageController?.Dispose();
        _libraryCoverageController = null;
        _adjustedAreaBoundary?.Dispose();
        _adjustedAreaBoundary = null;
        _overscaleCurtainController?.Dispose();
        _overscaleCurtainController = null;

        Host.Dispose();
    }
}

/// <summary>
/// The zoom buttons and compass that drive a <see cref="MapView"/>. They sit
/// in the window's overlay column for now; the split (#1727) moves them into
/// each pane.
/// </summary>
/// <param name="ZoomIn">The zoom-in button.</param>
/// <param name="ZoomOut">The zoom-out button.</param>
/// <param name="ZoomToExtent">The zoom-to-extent button.</param>
/// <param name="Compass">The compass rose.</param>
internal sealed record MapNavigationControls(
    Button ZoomIn,
    Button ZoomOut,
    Button ZoomToExtent,
    CompassRoseView Compass);
