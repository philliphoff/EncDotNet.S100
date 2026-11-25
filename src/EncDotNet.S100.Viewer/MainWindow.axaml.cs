using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.Services.Updates;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Projections;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Viewer;

public partial class MainWindow : ShadUI.Window, IMapPaneHost
{
    private readonly IRecentFilesService _recentFiles;
    private readonly IDatasetLoaderService _loader;
    private readonly IPickService _pickService;
    private readonly IFileDialogService _fileDialog;
    private readonly IExchangeSetService _exchangeSetService;
    private readonly IUpdateNotificationCoordinator? _updateNotificationCoordinator;
    private readonly MainViewModel _viewModel;
    private readonly ILibraryImporter _libraryImporter;
    private readonly CancellationTokenSource _windowLifetimeCancellation = new();
    private readonly MapViews _mapViews;
    private readonly MapSplitViewModel _split;
    private readonly MapViewProbes _mapProbes;
    private readonly DebouncedSettingsSaver _mapLayoutSaver;
    private readonly Dictionary<MapViewId, MapView> _paneViews = [];
    private bool _diPlannerTaken;
    private bool _closing;
    private string? _paneLayoutSignature;
    private float _routeCheckLegendReserve;
    private EncDotNet.S100.Viewer.Services.Legends.ILegendSectionSource? _activeCoverageLegend;

    /// <summary>The active map (#1730, #1732), or <see langword="null"/> once the window has closed.</summary>
    private MapView? ActiveView => _mapViews.Active as MapView;

    /// <summary>The status-bar width below which the MARKING pill drops its label.</summary>
    private const double AmlMarkingLabelMinWidth = 900;

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
            ResolveOrFallback<ILibraryImporter>(static () => throw new InvalidOperationException(
                "ILibraryImporter cannot be resolved without the application service provider.")),
            ResolveOrFallback<IRecentFilesService>(static () => throw new InvalidOperationException(
                "IRecentFilesService cannot be resolved without the application service provider.")),
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
        ILibraryImporter libraryImporter,
        IRecentFilesService recentFiles,
        IDatasetLoaderService loader,
        IPickService pickService,
        IFileDialogService fileDialog,
        IExchangeSetService exchangeSetService,
        IUpdateNotificationCoordinator? updateNotificationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(libraryImporter);
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(pickService);
        ArgumentNullException.ThrowIfNull(fileDialog);
        ArgumentNullException.ThrowIfNull(exchangeSetService);

        InitializeComponent();

        // Open large on first run and where the user left it afterwards (#825).
        InitializeWindowPlacement(options, App.Services.GetRequiredService<ViewerSettings>());

        // Bind the notification overlay to the DI-managed notification
        // service so background services can surface notifications.
        NotificationHost.ItemsSource =
            App.Services.GetRequiredService<Services.Notifications.INotificationService>().Active;

        _viewModel = viewModel;
        _libraryImporter = libraryImporter;
        _recentFiles = recentFiles;
        _loader = loader;
        _pickService = pickService;
        _fileDialog = fileDialog;
        _exchangeSetService = exchangeSetService;
        _updateNotificationCoordinator = updateNotificationCoordinator;

        // Timeline keys (#708 C8): ←/→ step, Home/End, N, +/−/0 and T, while
        // the map, the Timeline or nothing has focus (never while typing).
        AddHandler(KeyDownEvent, OnTimelineKeyDown, RoutingStrategies.Tunnel);

        // Route editor keys (#895): Ctrl+E (⌘E) edit, Ctrl+Z / Ctrl+Shift+Z
        // undo and redo, Delete removes the selected waypoint; never while typing.
        Tools.RouteEditorKeys.Attach(
            this,
            _viewModel.RoutesService,
            IsMapKeyScope);

        // Measure keys (#1130, D20): G geometry, Delete clears, Ctrl+C (⌘C) copies.
        Tools.MeasureKeys.Attach(
            this,
            _viewModel.MapTools.MeasureSession,
            _viewModel.MapTools.MeasureCard,
            IsMapKeyScope);

        // Range & bearing keys (#1130 D20): G geometry, Delete clears, Ctrl+C
        // (⌘C) copies; only while the tool is on and never while typing.
        Tools.RangeBearingKeys.Attach(
            this,
            _viewModel.MapTools.RangeBearing.State,
            () =>
            {
                if (_viewModel.MapTools.RangeBearing.CopyText is { } text && Clipboard is { } clipboard)
                    _ = clipboard.SetTextAsync(text);
            },
            IsMapKeyScope);

        // The dock follows the map area's width (#712 F5); the split folds
        // panes that don't fit it (#1732, D17).
        MapArea.SizeChanged += (_, e) =>
        {
            _viewModel.Layout.MapWidth = e.NewSize.Width;
            if (_split is { } split)
                split.MapAreaWidth = e.NewSize.Width;
        };

        // Legends D9: in a narrow window the right dock opens folded; its edge
        // tab shows it over the map's right edge, and Escape or a map click
        // folds it again.
        _viewModel.Layout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ShellLayoutViewModel.IsRightDockOverlaid)
                or nameof(ShellLayoutViewModel.IsRightDockDocked)
                or nameof(ShellLayoutViewModel.IsRightDockPanelVisible))
                ApplyRightDockOverlay();
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && _viewModel.Layout.FoldOverlaidRightDock())
                e.Handled = true;
        }, RoutingStrategies.Tunnel);
        MapArea.AddHandler(PointerPressedEvent, (_, _) => _viewModel.Layout.FoldOverlaidRightDock(),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        SizeChanged += (_, e) => _viewModel.Layout.WindowWidth = e.NewSize.Width;

        // The maps (#1729, #1730, #1732): a MapView per pane owns the map host
        // over the pane's control, its overlays, interaction and probes.
        // Registering a view with MapViews binds the map capability accessors
        // and the window-level viewport notifier to the active one.
        _mapViews = App.Services.GetRequiredService<MapViews>();
        _mapProbes = new MapViewProbes(
            _mapViews,
            App.Services.GetRequiredService<EncDotNet.S100.Viewer.Diagnostics.RenderActivityMonitor>(),
            App.Services.GetRequiredService<EncDotNet.S100.Viewer.Diagnostics.ViewSettleMonitor>());
        var settingsForLayout = App.Services.GetRequiredService<ViewerSettings>();
        // As with the window placement (#825), automation runs neither read
        // nor write the saved split, so an agent's maps don't depend on the
        // user's last session.
        var persistMapLayout = options?.Ephemeral != true
            && !settingsForLayout.IsReadOnly
            && !settingsForLayout.McpConfiguredFromCommandLine;
        _mapLayoutSaver = new DebouncedSettingsSaver(
            save: () =>
            {
                if (_closing || !persistMapLayout)
                    return;
                _split!.WriteTo(settingsForLayout.MapLayout ??= new MapLayoutSettings());
                try { settingsForLayout.Save(); } catch { /* best-effort */ }
            },
            dispatch: action => Dispatcher.UIThread.Post(action));
        _split = new MapSplitViewModel(_mapViews, this, _mapLayoutSaver.RequestSave);
        _split.LayoutChanged += (_, _) => ApplyPaneLayout();
        _mapViews.ActiveChanged += (_, _) => OnActiveMapChanged();
        SplitMapControls.DataContext = _split;
        // View › Split Map: ⌘\ is the macOS menu item's; Ctrl+\ elsewhere (D1).
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.OemPipe, KeyModifiers.Control),
            Command = _split.ToggleSplitCommand,
        });

        // Let the feedback reporter capture the whole application window.
        App.Services.GetRequiredService<IAppScreenshotProvider>().Target = this;
        // Render-state controller bridges MCP / scripted callers to the
        // viewer's palette and ECDIS display category without exposing
        // ViewerSettingsService / EcdisDisplayState directly.
        App.Services.GetRequiredService<IRenderStateControllerAccessor>().Current =
            new ViewerRenderStateController(
                App.Services.GetRequiredService<ViewerSettingsService>(),
                App.Services.GetRequiredService<EcdisDisplayState>());
        // UI controller bridges MCP / scripted callers to the viewer's
        // activity-panel state (which docks are open and which tab each
        // shows) without exposing MainViewModel directly.
        App.Services.GetRequiredService<IViewerUiControllerAccessor>().Current =
            new ViewerUiController(_viewModel.Layout);
        // The loader drives every map's dataset session (#1731), following
        // the maps as they're added and removed.
        _loader.Initialize(_mapViews, options);
        // Wire validation finding click-to-zoom: each finding view-model
        // routes its <c>ZoomToFindingCommand</c> through this dispatcher.
        _viewModel.Datasets.ZoomDispatcher = extent => ActiveView?.Host.ZoomToExtent(extent);
        // Open the maps: the saved split with each pane's view (D2), or one
        // map. A view set on the command line wins over the saved views.
        _split.Restore(
            persistMapLayout ? settingsForLayout.MapLayout : null,
            restoreViews: options?.HasExplicitViewport != true);

        Closed += (_, _) =>
        {
            // Save the panes and their views while the maps still exist (D2).
            if (!_closing && persistMapLayout)
            {
                _split.WriteTo(settingsForLayout.MapLayout ??= new MapLayoutSettings());
                try { settingsForLayout.Save(); } catch { /* best-effort */ }
            }
            _closing = true;
            _mapLayoutSaver.Dispose();
            _windowLifetimeCancellation.Cancel();
            _windowLifetimeCancellation.Dispose();
            App.Services.GetRequiredService<DatasetProcessorOwner>().Dispose();
            // PR-M3: flush any pending debounced size writes so the last
            // splitter drag isn't lost on shutdown.
            _viewModel.Layout.OnShutdown();
            // Record a clean shutdown so the next launch does not report
            // this (graceful) exit as a crash.
            EncDotNet.S100.Viewer.Diagnostics.UncleanShutdownSentinel.MarkCleanExit();
            // Clear the late-bound accessors this window owns so panel /
            // screenshot MCP tools observe the torn-down state (UiNotReady /
            // WindowNotReady) rather than a stale controller, and so the
            // MainViewModel / window are not kept alive after close.
            App.Services.GetRequiredService<IViewerUiControllerAccessor>().Current = null;
            App.Services.GetRequiredService<IAppScreenshotProvider>().Target = null;
            // Removing the last view clears the map capability accessors,
            // the image renderer first, and the probes. Each view detaches its
            // overlays and dynamic sources and disposes its map host.
            foreach (var view in _paneViews.Values.ToArray())
            {
                _mapViews.Remove(view);
                view.Dispose();
            }
            _paneViews.Clear();
            _mapProbes.Dispose();
        };
        DataContext = _viewModel;

        // Build the native menu bar (File / View › Appearance) and keep its
        // toggle items mirrored against the view-model. The builder owns the
        // PropertyChanged subscriptions for the lifetime of this window.
        new NativeMenuBuilder(_viewModel, _recentFiles).Attach(
            window: this,
            openDatasetAsync: OpenDatasetAsync,
            openExchangeSetAsync: OpenExchangeSetAsync,
            openExchangeSetZipAsync: OpenExchangeSetZipAsync,
            split: _split,
            libraryImporter: _libraryImporter);

        // Open Recent replays exchange sets through the same progress UI.
        _viewModel.DatasetOpening.ExchangeSetOpener = RunExchangeSetAsync;

        // Apply persisted accent color. Both the user's colour choice and
        // the active chrome theme feed the effective brush, so re-apply on
        // either change (the low-light themes mute the accent).
        var viewerSettingsService = App.Services.GetRequiredService<ViewerSettingsService>();
        ApplyAccentColor(viewerSettingsService.AccentColor);
        viewerSettingsService.AccentColorChanged += ApplyAccentColor;
        if (Application.Current is { } themedApp)
            themedApp.ActualThemeVariantChanged += (_, _) => ApplyAccentColor(_accentColor);

        // Surface DatasetsViewModel rejection of files no product recognises.
        _viewModel.Datasets.UnrecognizedFileEncountered += path =>
        {
            App.Services.GetRequiredService<INotificationService>()
                .Create(Strings.Toast_Warning)
                .WithSeverity(NotificationSeverity.Warning)
                .WithContent(UnrecognizedFileMessage.For(path))
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

        // Slice 7d: the predicted tide and current stations follow the view
        // whenever their live feeds are on.
        App.Services.GetService<EncDotNet.S100.Viewer.Services.Tides.TideStationsOverlay>()?.Attach();

        // Library ↔ Timeline actions (#711): Show on timeline, Go to start of run, Reveal in Library.
        _ = new EncDotNet.S100.Viewer.Services.LibraryTimelineLink(
            App.Services.GetRequiredService<LibraryPanelViewModel>(),
            _viewModel.Timeline,
            App.Services.GetRequiredService<EncDotNet.S100.Viewer.Services.GlobalTimeService>(),
            _viewModel.Layout);

        // Navigational warnings (#932): the no-sources state's Open Library.
        var warnings = _viewModel.MapChrome.NavigationalWarnings;
        warnings.OpenLibraryRequested += (_, _) => _viewModel.Layout.SelectTab(LibraryImportCoordinator.LibraryPanelId);
        // #959 E1: a finding's "Open in Navigational warnings" brings the panel forward.
        warnings.RevealRequested += (_, _) => _viewModel.Layout.SelectTab("NavigationalWarnings");

        // #959: a Warning card's "Crosses route · leg 3 → 4" brings the Routes panel's Findings forward.
        if (App.Services.GetService<EncDotNet.S100.Viewer.Services.PassageCheck.RouteFindingsService>() is { } routeFindings)
            routeFindings.OpenRequested += (_, _) => _viewModel.Layout.SelectTab("Routes");

        // Depth basis indicator (#1191): what the chart's depths are relative to.
        InitializeDepthBasis();

        // The route check's map legend and the AML CLB chip, over the map.
        InitializeRouteCheckLegend();

        // Enable drag & drop of dataset files onto the map
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Apply CLI options
        _startupOptions = options;

        // When an explicit startup viewport is requested, suppress the
        // per-dataset auto-zoom so the requested framing wins.
        if (options?.HasExplicitViewport == true)
        {
            _loader.SuppressAutoZoom = true;
        }

        // Run startup automation once the window is shown: load CLI
        // datasets and drive the map to the requested state — in a
        // deterministic order so an agent gets a stable result.
        var datasetPaths = options?.Datasets?.Where(File.Exists).ToArray() ?? [];
        var needsAutomation = datasetPaths.Length > 0
            || options?.HasExplicitViewport == true
            || options?.TimeStep is not null
            || options?.HasOwnShipOption == true;
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

        // Put the live feeds subscribed to in the Library on the map (design
        // note D8). Automated runs start from their CLI datasets alone.
        if (!needsAutomation)
            Opened += (_, _) => RestoreSavedSources();
        // #1073: SOUNDCHARTS_NMEA_INPUT adds own-ship weather, in automated
        // runs too, as it did when it was an overlay.
        Opened += (_, _) => AddNmeaWeatherFromEnvironment();

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
                () => _viewModel.AppCommands.ShowFeedbackCommand.Execute(null))
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
    /// <summary>Puts the live feeds subscribed to in the Library on the map, and follows the Library.</summary>
    private static void RestoreSavedSources() =>
        App.Services.GetService<EncDotNet.S100.Viewer.Services.Sources.LiveFeedLibrary>()?.Start();

    /// <summary>
    /// Adds and loads own-ship weather when <c>SOUNDCHARTS_NMEA_INPUT</c>
    /// names an input. It isn't saved: the variable adds it at each start.
    /// </summary>
    private static void AddNmeaWeatherFromEnvironment()
    {
        if (EncDotNet.S100.Viewer.Services.DynamicSources.Nmea.NmeaOwnShipLink.InputFromEnvironment() is not { } input)
            return;
        var datasets = App.Services.GetService<DatasetsViewModel>();
        var entry = datasets?.AddSource(
            EncDotNet.S100.Datasets.Pipelines.S100Sources.NmeaWeather,
            new EncDotNet.S100.Datasets.Pipelines.NmeaWeatherSourceSettings { Input = input });
        if (entry is not null)
            datasets!.RequestLoad(entry);
    }

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
        ActiveView?.Host.SetViewportToCenterAndResolution(new MPoint(x, y), resolution);
        return true;
    }

    /// <summary>
    /// Deterministic startup sequence for headless/agent runs: load any
    /// CLI datasets, wait for rendering to quiesce, then drive the map to
    /// the requested time step and viewport. Live capture and further
    /// state changes are driven over MCP once the window is up; the
    /// process itself is stopped from the OS (there is no MCP shutdown).
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
                _viewModel.Layout.SelectDefaultTab();
                foreach (var datasetPath in datasetPaths)
                {
                    // No registered product claims an unrecognised file, so skip
                    // it rather than guess (the interactive path warns instead).
                    if (DatasetPipelineFactory.DetectProductSpec(datasetPath) is not { } spec)
                        continue;
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

        // Own-ship-only launch (no datasets to auto-zoom, no explicit CLI
        // viewport): frame the map on the own-ship we just positioned,
        // matching the interactive framing path. With datasets or an
        // explicit viewport, that framing wins instead.
        if (datasetPaths.Length == 0
            && _startupOptions?.HasOwnShipOption == true
            && _startupOptions?.HasExplicitViewport != true)
        {
            await FrameOnOwnShipAtStartupAsync();
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
                ActiveView?.Host.SetViewportToExtent(extent);
            }
            return;
        }

        if (options.ParsedCenter is { } center && options.Zoom is { } zoom)
        {
            var (x, y) = SphericalMercator.FromLonLat(center.Longitude, center.Latitude);
            // Standard web-mercator resolution (metres/pixel) at a given
            // 256-pixel-tile zoom level: 156543.03392804097 / 2^zoom.
            var resolution = 156543.03392804097 / Math.Pow(2, zoom);
            ActiveView?.Host.SetViewportToCenterAndResolution(new MPoint(x, y), resolution);
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
        AccentColors.Apply(Resources, color, Application.Current?.ActualThemeVariant);
    }

    /// <summary>
    /// Binds the route check's map legend (#959 C8) and the AML CLB chip, and
    /// tells the map view how much of the map's bottom edge the legend takes.
    /// </summary>
    private void InitializeRouteCheckLegend()
    {
        // #959 C8: the map legend, a control at the map's bottom left. The
        // finding chip (drawn by the overlay) sits above it, so the overlay
        // learns its height.
        // #1408 D6: it's the route check's Legend section, drawn compact; it
        // hides (and the reserve drops to 0) while the Legend tab is shown.
        var routeCheckLegend = new EncDotNet.S100.Viewer.ViewModels.RouteCheckLegendViewModel(
            App.Services.GetRequiredService<EncDotNet.S100.Viewer.Services.Legends.LegendService>(),
            App.Services.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.RouteCheckLegendState>());
        RouteCheckLegend.DataContext = routeCheckLegend;
        // #1374: the AML CLB chip ("not used for depth checks").
        AmlClbChip.DataContext = App.Services.GetRequiredService<AmlDisplaySectionViewModel>();
        // Clear of the Time HUD when it's shown, else close to the map's edge.
        void PlaceLegend() => RouteCheckLegend.Margin = new Thickness(
            12, 0, 0, _viewModel.Layout.IsTimeHudVisible ? EncDotNet.S100.Viewer.Tools.RouteOverlayRenderer.LegendBottomMargin : 12);
        PlaceLegend();
        _viewModel.Layout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellLayoutViewModel.IsTimeHudVisible))
                PlaceLegend();
        };
        RouteCheckLegend.LayoutUpdated += (_, _) =>
        {
            // From the map's bottom edge to the legend's top.
            // The legend's parent ends at the map area's bottom edge, as each pane does.
            var reserve = RouteCheckLegend.IsEffectivelyVisible && routeCheckLegend.IsVisible && RouteCheckLegend.Bounds.Height > 0
                ? (float)(MapWindowOverlays.Bounds.Height - RouteCheckLegend.Bounds.Y)
                : 0f;
            _routeCheckLegendReserve = reserve;
            foreach (var view in _paneViews.Values)
                view.SetRouteCheckLegendReserve(reserve);
        };
    }

    /// <summary>
    /// True for the visuals whose focus counts as "on the map" for the route
    /// editor, measure and range &amp; bearing keys.
    /// </summary>
    private bool IsMapKeyScope(Visual visual) =>
        visual is EncDotNet.S100.Viewer.Diagnostics.InstrumentedMapControl || ReferenceEquals(visual, MapArea);

    private async Task OpenDatasetAsync()
    {
        var paths = await _fileDialog.OpenDatasetsAsync(this, allowMultiple: true);
        if (paths.Count == 0)
            return;

        _viewModel.Layout.SelectDefaultTab();

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
        _viewModel.Layout.SelectDefaultTab();

        var token = _viewModel.DatasetOpening.BeginExchangeSetLoad(sourcePath);

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
                () => _viewModel.DatasetOpening.CancelExchangeSetCommand.Execute(null),
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
            _viewModel.DatasetOpening.EndExchangeSetLoad(result);

            // Remember a successfully opened exchange set in Open Recent
            // (issue #655). A dropped CATALOG.031 is recorded as its folder.
            if (!result.Cancelled && !result.CatalogueNotFound && result.FailureMessage is null && result.Loaded > 0)
                _recentFiles.Add(RecentExchangeSetPath(sourcePath));

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
            _viewModel.DatasetOpening.EndExchangeSetLoad(new Services.ExchangeSetOpenResult
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

        if (ActiveView?.RenderActivity is { } renderActivity)
        {
            try
            {
                await renderActivity
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
            ActiveView?.Host.ZoomToExtent(extent, durationMilliseconds: 250);
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

        if (ActiveView is not { } view)
            return;
        var extent = unionSlot[0] ?? view.Pane.MapControl.Map.Extent;
        if (extent is null || extent.Width <= 0 || extent.Height <= 0) return;

        view.Host.ZoomToExtent(extent, durationMilliseconds: 250);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { } files)
            return;

        _viewModel.Layout.SelectDefaultTab();

        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (path is null)
                continue;

            // Folder drop: treat as an exchange set when CATALOG.XML
            // (S-100) or CATALOG.031 (S-57) is at the root, or a
            // catalogue-less folder of loose ENC cells (a base ….000 plus
            // its updates), and open it — offering to add it to the library.
            // Any other folder (e.g. a tree of many exchange sets) is too
            // broad to open; offer to add it to the library instead
            // (issue #655).
            if (Directory.Exists(path))
            {
                if (ExchangeSetDetection.LooksLikeExchangeSetFolder(path)
                    || ExchangeSetDetection.LooksLikeS57ExchangeSetFolder(path)
                    || ExchangeSetDetection.LooksLikeLooseCellFolder(path))
                {
                    await RunExchangeSetAsync(path);
                    OfferAddToLibrary(path);
                }
                else
                {
                    await _libraryImporter.AddPathAsync(path, targetCollectionId: null);
                }
                continue;
            }

            if (!File.Exists(path))
                continue;

            // A collection manifest (named *.s100collection.json, or JSON whose
            // format says so) is added to the library, not opened as a dataset.
            if (EncDotNet.S100.Collections.Manifests.CollectionManifest.IsManifestPath(path))
            {
                await _libraryImporter.AddPathAsync(path, targetCollectionId: null);
                continue;
            }

            // File drop: a .zip with a root-level CATALOG.XML is an
            // exchange-set ZIP, and a dropped CATALOG.031 is an S-57
            // exchange set; everything else falls through to the
            // single-dataset loader.
            if (ExchangeSetDetection.IsZipPath(path) &&
                ExchangeSetDetection.LooksLikeExchangeSetZip(path))
            {
                await RunExchangeSetAsync(path);
                OfferAddToLibrary(path);
                continue;
            }

            if (ExchangeSetDetection.IsS57CataloguePath(path))
            {
                await RunExchangeSetAsync(path);
                OfferAddToLibrary(Path.GetDirectoryName(path)!);
                continue;
            }

            await _viewModel.Datasets.LoadFromPathAsync(path);
        }
    }

    /// <summary>
    /// After opening a dropped exchange set, offers (via a notification
    /// action) to add it to the library so it can be found again without
    /// opening it — unless the library already covers it.
    /// </summary>
    private void OfferAddToLibrary(string path)
    {
        if (_libraryImporter.IsInLibrary(path))
            return;

        App.Services.GetRequiredService<INotificationService>()
            .Create(Strings.Toast_AddToLibraryAction)
            .WithSeverity(NotificationSeverity.Info)
            .WithContent(Strings.Toast_AddedToLibraryPrompt)
            .WithAction(Strings.Toast_AddToLibraryAction,
                () => _ = _libraryImporter.AddPathAsync(path, targetCollectionId: null),
                isPrimary: true)
            .AutoDismiss(TimeSpan.FromSeconds(12))
            .Show();
    }

    /// <summary>
    /// The path Open Recent records for an opened exchange set: the full path,
    /// without a trailing separator, and a <c>CATALOG.031</c> as its folder.
    /// </summary>
    internal static string RecentExchangeSetPath(string sourcePath)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        return ExchangeSetDetection.IsS57CataloguePath(full) ? Path.GetDirectoryName(full)! : full;
    }

    /// <summary>
    /// Moves the right dock over the map's right edge while it's shown from a
    /// narrow fold (legends D9), so the map keeps its width; back into its
    /// column otherwise.
    /// </summary>
    private void ApplyRightDockOverlay()
    {
        // While split, the shown dock opens over the map too, so it never
        // takes a pane's room (#1732, D17); it covers part of the rightmost pane.
        var overlaid = _viewModel.Layout.IsRightDockOverlaid
            || (_viewModel.Layout.IsMapSplit && _viewModel.Layout.IsRightDockShown);
        Avalonia.Controls.Grid.SetColumn(RightDockPanel, overlaid ? 2 : 4);
        RightDockPanel.HorizontalAlignment = overlaid
            ? Avalonia.Layout.HorizontalAlignment.Right
            : Avalonia.Layout.HorizontalAlignment.Stretch;
        RightDockPanel.Width = overlaid
            ? Math.Max(
                ShellLayoutViewModel.RightDockMinimumWidth,
                Math.Min(_viewModel.Layout.RightDockSavedWidth ?? ShellLayoutViewModel.RightDockDefaultWidth,
                    MapArea.Bounds.Width - ShellLayoutViewModel.RightDockOverlayMapMargin))
            : double.NaN;
        RightDockPanel.ZIndex = overlaid ? 100 : 0;

        // Docked, the panel shows the window behind it; over the map it needs
        // an opaque surface of its own.
        _rightDockOverlayBackground?.Dispose();
        _rightDockOverlayBackground = null;
        if (overlaid)
        {
            _rightDockOverlayBackground = RightDockPanel.Bind(
                BackgroundProperty,
                Avalonia.Controls.ResourceNodeExtensions.GetResourceObservable(this, "BackgroundColor", static value => value switch
                {
                    Color c => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B)),
                    ISolidColorBrush b => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(b.Color.R, b.Color.G, b.Color.B)),
                    _ => Brushes.White,
                }));
        }
        else
        {
            RightDockPanel.ClearValue(BackgroundProperty);
        }
    }

    private IDisposable? _rightDockOverlayBackground;

    private void OnTimelineKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !IsTimelineKeyScope(FocusManager?.GetFocusedElement() as Visual))
            return;
        if (e.Key == Key.T && e.KeyModifiers == KeyModifiers.None)
        {
            _viewModel.Layout.ToggleTimelineCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (_viewModel.Timeline.HandleKey(e.Key, e.KeyModifiers))
            e.Handled = true;
    }

    /// <summary>
    /// True when the Timeline's keys apply: focus is on nothing, the map or
    /// the Timeline, and not in a text box (lists and trees keep their keys).
    /// </summary>
    private bool IsTimelineKeyScope(Visual? focused)
    {
        if (focused is null || ReferenceEquals(focused, this))
            return true;
        for (var visual = focused; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Avalonia.Controls.TextBox)
                return false;
            if (visual is Views.TimelineView or Views.TimeHudView or EncDotNet.S100.Viewer.Diagnostics.InstrumentedMapControl)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Feeds the status bar's depth basis pill from <see cref="EncDotNet.S100.Viewer.Services.Depth.DepthBasisService"/>
    /// and keeps it to a whole form that fits the status bar (#1191).
    /// </summary>
    private void InitializeDepthBasis()
    {
        if (App.Services.GetService<EncDotNet.S100.Viewer.Services.Depth.DepthBasisService>() is not { } depthBasis)
            return;

        var timeFormat = App.Services.GetService<ITimeFormatProvider>();
        void Push() => _viewModel.StatusBar.UpdateDepthBasis(
            depthBasis.Current, depthBasis.WaterLevelLoaded, timeFormat?.Current ?? TimeFormat.Utc);
        depthBasis.Changed += (_, _) => Push();

        // Relief shading on marks the pill (s102-relief-design.md section 3).
        if (App.Services.GetService<EcdisDisplayState>() is { } display)
        {
            void PushRelief() => _viewModel.StatusBar.DepthBasis.SetRelief(
                display.GetLayerVisible(S102Relief.LayerKey) ?? false,
                () => display.SetLayerVisible(S102Relief.LayerKey, false));
            display.Changed += () => Dispatcher.UIThread.Post(PushRelief);
            PushRelief();
        }

        // Tide predictions (#1453, D2, D15): the flyout's offer and stale notice.
        if (App.Services.GetService<ViewerSettingsService>() is { } viewerSettings
            && App.Services.GetService<EncDotNet.S100.Viewer.Services.Tides.TideStationListService>() is { } tideList)
        {
            _viewModel.StatusBar.DepthBasis.SetTidePredictions(
                () => viewerSettings.UseTidePredictions = true,
                () => _ = tideList.DownloadAsync());
        }

        if (timeFormat is not null)
            timeFormat.TimeFormatChanged += _ => Dispatcher.UIThread.Post(Push);

        // The pill draws the longest whole form that fits: the room is the
        // status bar's width less every other item in the right-hand cluster.
        void FitDepthBasis()
        {
            if (StatusBarDock is not { } dock || StatusRightCluster is not { } cluster || DepthBasisIndicator is not { } pill)
                return;
            var room = dock.Bounds.Width - (cluster.DesiredSize.Width - pill.DesiredSize.Width);
            pill.Fit(room);
        }
        StatusBarDock.SizeChanged += (_, _) => FitDepthBasis();
        // The MARKING pill keeps its marking whole and drops its label when
        // the status bar is short of room (#1371, decision 10).
        StatusBarDock.SizeChanged += (_, _) =>
            _viewModel.StatusBar.AmlMarking.ShowsLabel = StatusBarDock.Bounds.Width >= AmlMarkingLabelMinWidth;
        StatusRightCluster.SizeChanged += (_, _) => FitDepthBasis();
        _viewModel.StatusBar.DepthBasis.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DepthBasisIndicatorViewModel.FormTexts))
                Dispatcher.UIThread.Post(FitDepthBasis, DispatcherPriority.Render);
        };

        depthBasis.Invalidate();
    }
}
