using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.Services.Updates;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using EncDotNet.S100.Viewer.Views;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer;

public partial class App : Application
{
    internal static ViewerCommandSettings? StartupOptions { get; set; }

    /// <summary>The environment variable holding the password of <c>--secom-identity</c> (#832).</summary>
    internal const string SecomIdentityPasswordVariable = "SOUNDCHARTS_SECOM_IDENTITY_PASSWORD";

    /// <summary>
    /// The User-Agent of the Library's weather requests and downloads (#1075): SoundCharts, its
    /// version and the project's address, as the NWS API asks; no personal contact.
    /// </summary>
    internal static readonly string WeatherUserAgent =
        EncDotNet.S100.Collections.Net.LibraryHttp.ProductUserAgent("SoundCharts", typeof(App).Assembly);

    /// <summary>
    /// Checks the MCP identities for revocation now (the Keys &amp;
    /// certificates page repeats it daily), tells the user when one is
    /// revoked, with a way to the page (#845 C5, H4), and starts reading
    /// SECOM sources again when identities or trusted authorities change (#880, #883).
    /// </summary>
    private void WatchSecomIdentity()
    {
        var keys = _services!.GetRequiredService<ViewModels.Keys.KeysAndCertificatesViewModel>();
        var trust = _services!.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomServerTrust>();
        var notifications = _services!.GetService<EncDotNet.S100.Viewer.Services.Notifications.INotificationService>();
        trust.IdentityRevoked += (_, e) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            notifications?.Create(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Keys_Revoked_TitleFormat,
                    _services!.GetRequiredService<EncDotNet.S100.Viewer.Services.Secom.SecomIdentityStore>().Identities
                        .FirstOrDefault(r => string.Equals(r.Thumbprint, e.Identity.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))?.DisplayName
                        ?? e.Identity.Subject))
                .WithSeverity(EncDotNet.S100.Viewer.Services.Notifications.NotificationSeverity.Error)
                .WithContent(Strings.Keys_Revoked_Notification)
                .WithAction(Strings.Keys_Revoked_NotificationAction,
                    () => _services!.GetRequiredService<Services.Secom.KeysNavigator>().Open(ViewModels.Keys.KeysTab.Identities))
                .Persistent()
                .Show());
        _ = keys.CheckRevocationAsync();
        _ = _services!.GetRequiredService<EncDotNet.S100.Collections.Library.SecomTrustRefresh>();
        // Replacing or removing an identity moves the sources that name it (#883).
        _services!.GetRequiredService<EncDotNet.S100.Viewer.Services.Secom.SecomIdentityStore>().Library =
            _services!.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>();
    }

    private static IServiceProvider? _services;

    /// <summary>
    /// Records the most recent unhandled error so the feedback reporter
    /// can include it. Assigned once the service container is built; the
    /// global exception handlers funnel through <see cref="LogCrash"/>
    /// which forwards here.
    /// </summary>
    private static EncDotNet.S100.Viewer.Diagnostics.ILastErrorTracker? _lastErrorTracker;

    /// <summary>
    /// Previous viewer sessions that terminated without a clean shutdown
    /// (detected via <see cref="Diagnostics.UncleanShutdownSentinel"/>),
    /// or empty when the last run(s) exited normally. Set once during
    /// startup; the main window reads it to surface a crash-recovery
    /// notification. May contain more than one entry when several
    /// instances crashed since the last clean launch.
    /// </summary>
    internal static IReadOnlyList<EncDotNet.S100.Viewer.Diagnostics.PreviousSession> PreviousUncleanShutdowns { get; private set; } =
        Array.Empty<EncDotNet.S100.Viewer.Diagnostics.PreviousSession>();

    /// <summary>
    /// Application-wide service container. Populated during
    /// <see cref="OnFrameworkInitializationCompleted"/>; throws if accessed
    /// before the framework is initialized.
    /// </summary>
    internal static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException(
            "Service provider has not been initialized yet.");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        if (AttachesDeveloperTools)
        {
            this.AttachDeveloperTools();
        }
#endif
        ConfigureMacApplicationMenu();
    }

    /// <summary>
    /// Whether <see cref="Initialize"/> attaches Avalonia's developer tools, in
    /// Debug builds. They can be attached only once per process, so an app
    /// that is initialized more than once (a headless test session builds one
    /// per test) must not attach them.
    /// </summary>
    protected virtual bool AttachesDeveloperTools => true;

    /// <summary>
    /// On macOS, replaces the auto-generated application-menu "About"
    /// item (which would otherwise show Avalonia's built-in about box)
    /// with one that opens this app's About dialog.
    /// </summary>
    /// <remarks>
    /// The macOS application menu is read from
    /// <see cref="NativeMenu.GetMenu(Avalonia.AvaloniaObject)"/> on the
    /// <see cref="Application"/> exactly once, early during framework
    /// setup (after <see cref="Initialize"/> but before the main window
    /// exists). Setting it here ensures Avalonia adopts our menu instead
    /// of synthesizing the default; the standard Services/Hide/Quit items
    /// are appended to it automatically. The About item is wired on other
    /// platforms through the Help menu instead (see
    /// <see cref="Services.NativeMenuBuilder"/>).
    /// </remarks>
    private void ConfigureMacApplicationMenu()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var aboutItem = new NativeMenuItem(Strings.Menu_About);
        aboutItem.Click += (_, _) =>
        {
            try
            {
                Services.GetRequiredService<MainViewModel>().ShowAboutCommand.Execute(null);
            }
            catch (InvalidOperationException)
            {
                // Services not yet initialized; ignore the early click.
            }
        };

        NativeMenu.SetMenu(this, new NativeMenu { aboutItem });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("UnhandledException", e.ExceptionObject?.ToString() ?? "(null)");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("UnobservedTaskException", e.Exception?.ToString() ?? "(null)");
            e.SetObserved();
        };
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            LogCrash("UIThread.UnhandledException", e.Exception?.ToString() ?? "(null)");
            e.Handled = true;
        };

        _services = ConfigureServices();
        _services.GetRequiredService<ViewerPresentationCoordinator>();
        _services.GetRequiredService<LayerTimeCoordinator>();
        _services.GetRequiredService<PresentationRevealCoordinator>();

        // Now that the container exists, route recorded crashes into the
        // feedback reporter's last-error tracker (the global handlers above
        // funnel through LogCrash).
        _lastErrorTracker = _services.GetRequiredService<
            EncDotNet.S100.Viewer.Diagnostics.ILastErrorTracker>();

        // Re-root the warm tile disk cache under the active data directory
        // when one is in use (--data-dir / S100_DATA_DIR), so an isolated
        // instance keeps its tile cache self-contained. An explicit
        // S100_VECTOR_TILE_DISK_DIR env var still wins (the setter is a
        // no-op when env-pinned). Must run before the tile renderer creates
        // its shared disk cache below.
        if (_services.GetRequiredService<ViewerDataPaths>().TileDiskCacheDirectory is { } tileDir)
        {
            EncDotNet.S100.Renderers.Mapsui.RenderingOptimizations.TileDiskDirectory = tileDir;
        }

        // Detect an unclean shutdown from the previous run (native crash,
        // FailFast, kill, … — none of which the managed handlers above can
        // catch) and route it into the last-error tracker so the feedback
        // reporter and the startup notification can surface it. Disabled
        // for ephemeral automation runs so automation never writes a
        // marker or reports a stale one.
        DetectPreviousUncleanShutdown();

        // ShadUI resolves custom dialog content by an explicit
        // view/context-view-model registration on the DialogManager (a
        // DataTemplate alone is not sufficient). Register the feedback
        // dialog now that the singleton manager exists.
        _services.GetRequiredService<ShadUI.DialogManager>()
            .Register<Views.FeedbackDialogView, ViewModels.FeedbackDialogViewModel>();
        _services.GetRequiredService<ShadUI.DialogManager>()
            .Register<Views.AboutDialogView, ViewModels.AboutDialogViewModel>();
        _services.GetRequiredService<ShadUI.DialogManager>()
            .Register<Views.AddToLibraryDialogView, ViewModels.AddToLibraryDialogViewModel>();
        _services.GetRequiredService<ShadUI.DialogManager>()
            .Register<Views.AddOnlineCatalogueWizardView, ViewModels.AddOnlineCatalogueWizardViewModel>()
            .Register<Views.SharedFeedDialogView, ViewModels.SharedFeedDialogViewModel>()
            .Register<Views.AddCollectionManifestDialogView, ViewModels.AddCollectionManifestDialogViewModel>()
            .Register<Views.ImportIdentityDialogView, ViewModels.Keys.ImportIdentityDialogViewModel>()
            .Register<Views.RouteCopyDialogView, ViewModels.RouteCopyDialogViewModel>()
            .Register<Views.RouteExportDialogView, ViewModels.RouteExportDialogViewModel>()
            .Register<Views.RouteImportErrorDialogView, ViewModels.RouteImportErrorDialogViewModel>()
            .Register<Views.AddAuthorityDialogView, ViewModels.Keys.AddAuthorityDialogViewModel>()
            .Register<Views.CertificateDetailsDialogView, ViewModels.Keys.CertificateDetailsDialogViewModel>()
            .Register<Views.RemoveIdentityDialogView, ViewModels.Keys.RemoveIdentityDialogViewModel>();

        // Register every S-100 style and layer renderer before instrumentation
        // wraps Mapsui's style registry. The renderer package owns the required
        // dependency order so hosts do not have to reproduce it.
        EncDotNet.S100.Renderers.Mapsui.S100MapsuiRendering.Register();

        EncDotNet.S100.Viewer.Diagnostics.MapPaintInstrumentation.Install();

        // The viewer uses a plain ServiceCollection (no generic IHost),
        // so the IHostedService registered by AddOpenTelemetry() never
        // runs — meaning the TracerProvider / MeterProvider would
        // otherwise stay un-built and no ActivityListener / MeterListener
        // would ever subscribe. Resolving them here forces construction
        // and wires up the OTel pipeline before any instrumented code
        // runs (dataset open, pipeline process, render, etc.).
        _ = _services.GetService(typeof(OpenTelemetry.Trace.TracerProvider));
        _ = _services.GetService(typeof(OpenTelemetry.Metrics.MeterProvider));

        // Hook the logger factory into the static BeginCommand path so
        // each viewer command also emits a structured log entry.
        ViewerObservability.AttachLoggerFactory(
            _services.GetRequiredService<ILoggerFactory>());

        // Emit a startup span + log so the viewer always shows up in
        // a connected OpenTelemetry collector even before the user
        // performs any traceable action. Any subscribed exporter
        // (e.g. the .NET Aspire dashboard launched via the AppHost
        // project) will pick this up and confirm the OTEL_* wiring.
        using (var startup = Telemetry.ActivitySource.StartActivity(
                   "s100.viewer.startup", System.Diagnostics.ActivityKind.Internal))
        {
            startup?.SetTag("s100.viewer.version",
                typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0");
            var logger = _services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("EncDotNet.S100.Viewer");
            logger.LogInformation(
                "EncDotNet.S100.Viewer started (version {Version}).",
                typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        }

        // Start (or leave disabled) the MCP server based on persisted settings.
        // Failures are logged but never block app startup.
        var mcpHost = _services.GetRequiredService<McpServerHost>();
        var settingsVm = _services.GetRequiredService<SettingsViewModel>();
        // Judges which S-124 warnings the map draws from the first load on (#993),
        // and has the map stamp say "Display Not Real Time" while they're loaded
        // and the view time is pinned (S-98 §12.7.2).
        _services.GetRequiredService<EncDotNet.S100.Viewer.Services.NavigationalWarningDrawingService>();
        // S-101 features with date ranges count too (S-98 §12.8.2): their
        // drawing follows the view time as well.
        {
            var warningsService = _services.GetRequiredService<EncDotNet.S100.Viewer.Services.NavigationalWarningsService>();
            var datedDrawing = _services.GetRequiredService<EncDotNet.S100.Viewer.Services.DateDependentDrawingService>();
            var timeline = _services.GetRequiredService<TimelineViewModel>();
            void Update() => timeline.HasDateDependentData = warningsService.HasWarningDatasets || datedDrawing.HasDateDependentData;
            warningsService.Changed += (_, _) => Update();
            datedDrawing.Changed += (_, _) => Update();
            Update();
        }
        WatchSecomIdentity();
        settingsVm.McpSettingsChanged += () =>
        {
            _ = mcpHost.Apply().ContinueWith(t =>
            {
                if (t.Exception is not null)
                    LogCrash("McpServerHost", t.Exception.GetBaseException().ToString());
            }, TaskScheduler.Default);
        };

        // Live-refresh the examiner affordances in the FC and pick panels when
        // the user toggles the integration or edits the base URL (issue #442),
        // so the buttons appear/disappear without a catalogue reload or a new
        // pick.
        settingsVm.ExaminerSettingsChanged += () =>
        {
            _services.GetService<FeatureCataloguesViewModel>()?.RefreshExaminerAvailability();
            _services.GetService<PickReportViewModel>()?.RefreshExaminerAvailability();
        };
        _ = mcpHost.Apply().ContinueWith(t =>
        {
            if (t.Exception is not null)
            {
                LogCrash("McpServerHost", t.Exception.GetBaseException().ToString());
            }
        }, TaskScheduler.Default);

        // Apply persisted chrome theme + wire chrome→map coupling. The
        // chrome theme is the user's primary axis; the map palette
        // follows (per docs/design/s100-chrome-theme-spike.md §5)
        // unless the user subsequently overrides SelectedPalette
        // independently.
        var themeService = _services.GetRequiredService<IThemeService>();
        themeService.SetTheme(settingsVm.SelectedChromeTheme);
        settingsVm.ChromeThemeChanged += chromeTheme =>
        {
            themeService.SetTheme(chromeTheme);
            settingsVm.SelectedPalette = ChromeThemes.GetDefaultPaletteFor(chromeTheme);
        };

        // Re-publish own-ship fix when vessel dimensions change. Resolve
        // the concrete settings-backed provider directly: the public
        // IOwnShipVesselGeometryProvider is now the overridable wrapper
        // (pirate mode), so the cast would otherwise miss.
        var ownShipGeom = _services.GetService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.SettingsOwnShipVesselGeometryProvider>();
        if (ownShipGeom is not null)
        {
            settingsVm.OwnShipGeometryChanged += () => ownShipGeom.NotifyChanged();
        }

        // Toggle the simulated own-ship overlay live from Settings. The
        // source's IsEnabled gate empties (or republishes) the feature,
        // so flipping the checkbox shows/hides the glyph without a
        // restart.
        var ownShipSource = _services.GetService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipSource>();
        if (ownShipSource is not null)
        {
            settingsVm.OwnShipOverlayEnabledChanged += enabled => ownShipSource.IsEnabled = enabled;
        }

        // Pirate mode: engage when the user picks "Take the helm of this
        // vessel" on an AIS hit in the pick report. The coordinator opens
        // the visibility gates, persists the source selection, and starts
        // the controller following. Routing the overlay-enable through
        // settingsVm keeps the checkbox + persisted flag in sync.
        var pirateController = _services.GetService<EncDotNet.S100.Viewer.Services.DynamicSources.PirateModeController>();
        var pickReport = _services.GetService<PickReportViewModel>();
        if (pirateController is not null)
        {
            var pirateCoordinator = new EncDotNet.S100.Viewer.Services.DynamicSources.PirateModeCoordinator(
                pirateController,
                _services.GetRequiredService<EncDotNet.S100.Renderers.Mapsui.DynamicSources.IS100DynamicSourceRegistry>(),
                _services.GetRequiredService<ViewerSettings>(),
                enabled => settingsVm.OwnShipOverlayEnabled = enabled);

            if (pickReport is not null)
            {
                pickReport.TakeHelmRequested += (_, mmsi) => pirateCoordinator.Engage(mmsi);
            }

            // Take/release the helm from the Vessels panel. Engage hides the
            // followed target, so after engaging refresh the list and select
            // the own-ship row (HandleHelmEngaged) to keep the Release button
            // reachable; disengage just refreshes the label/command state.
            var vesselList = _services.GetService<VesselListViewModel>();
            if (vesselList is not null)
            {
                vesselList.TakeHelmRequested += (_, mmsi) =>
                {
                    pirateCoordinator.Engage(mmsi);
                    vesselList.HandleHelmEngaged();
                };
                vesselList.ReleaseHelmRequested += (_, _) =>
                {
                    pirateCoordinator.Disengage();
                    vesselList.HandleHelmDisengaged();
                };
            }

            // If the user turns the own-ship overlay off while pirate mode
            // is active, disengage: otherwise the followed AIS target stays
            // excluded (hidden) while own-ship is also hidden, making the
            // vessel vanish entirely. Disengage clears the exclusion +
            // geometry override and reverts the source to Simulated.
            settingsVm.OwnShipOverlayEnabledChanged += enabled =>
            {
                if (!enabled && pirateController.IsActive)
                    pirateCoordinator.Disengage();
            };

            // Re-arm pirate mode at launch if the last session left it on.
            pirateCoordinator.RestoreFromSettings();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Load persisted routes before the main window (and its route
            // overlay / panel) are built so they observe the saved set on
            // first render; the coordinator then writes changes back.
            var routePersistence =
                _services.GetRequiredService<EncDotNet.S100.Viewer.Services.RoutePersistenceService>();
            routePersistence.Initialize();
            _ = _services.GetRequiredService<EncDotNet.S100.Viewer.Services.ReceivedRoutesService>();
            // RTZ files open at the last close come back (F8, #961).
            _ = _services.GetRequiredService<EncDotNet.S100.Viewer.Services.RouteExchangeService>().ReopenFilesAsync();

            // Load the dataset collections and start background re-indexing
            // (cached indexes appear immediately; nothing is loaded).
            var library = _services.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>();
            _ = _services.GetRequiredService<EncDotNet.S100.Collections.Library.LibrarySync>();  // syncs from the first index
            _ = _services.GetRequiredService<Library.LibraryShowOnMapService>();  // and restores sources shown on the map
            library.Initialize();

            desktop.MainWindow = _services.GetRequiredService<MainWindow>();

            // Drain the tiled renderer's background Skia workers before the
            // process tears down. Avalonia raises ShutdownRequested on every
            // exit path (explicit Shutdown(), last-window-close, OS quit), so
            // hooking it here covers every graceful quit path. Without this,
            // the managed runtime can begin destroying libSkiaSharp while a
            // worker is mid-rasterise → native SIGSEGV.
            desktop.ShutdownRequested += (_, _) =>
            {
                // Flush any pending debounced route save so the last edit is
                // not lost to the debounce window.
                routePersistence.Flush();
                library.Dispose();
                EncDotNet.S100.Renderers.Mapsui.S100VectorTileRenderer.ShutdownAndDrain(TimeSpan.FromSeconds(5));
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The map view as geographic bounds for narrowing a source to it (#807),
    /// shifted into −180…180; <see langword="null"/> when there is no view, or
    /// it spans the whole world.
    /// </summary>
    private static EncDotNet.S100.Collections.GeoBounds? MapViewOf(MapViewportSnapshot? view)
    {
        if (view is null || view.LongitudeSpanDegrees >= 360)
            return null;
        var centre = (view.MinLongitude + view.MaxLongitude) / 2;
        var shift = Math.Round(centre / 360) * 360;
        return new EncDotNet.S100.Collections.GeoBounds(
            view.MinLatitude, Math.Max(-180, view.MinLongitude - shift), view.MaxLatitude, Math.Min(180, view.MaxLongitude - shift));
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Resolved on-disk locations for this run (settings + caches),
        // honouring --data-dir / S100_DATA_DIR (and --settings for the
        // settings file). A single shared instance drives the settings
        // file, the disk caches, the logs, and the crash-marker directory.
        var dataPaths = ViewerDataPaths.Resolve(StartupOptions);
        services.AddSingleton(dataPaths);

        // OpenTelemetry tracing/metrics/logging — opt-in via OTEL_* env vars.
        // CLI --log-file / --verbose add a file sink and lower the log
        // floor for agent runs. Rolling logs under the data directory are
        // always on, except for ephemeral automation runs.
        services.AddS100Observability(
            logFilePath: StartupOptions?.LogFile,
            verbose: StartupOptions?.Verbose ?? false,
            rollingLogDirectory: StartupOptions?.Ephemeral == true ? null : dataPaths.LogsDirectory,
            crashMarkerDirectory: dataPaths.CrashMarkersDirectory);

        // Persisted user settings, with any command-line overrides
        // (settings path / --ephemeral / MCP / palette / display
        // category) layered on top for this run only.
        services.AddSingleton<ViewerSettings>(sp =>
            StartupSettingsFactory.Create(StartupOptions, sp.GetRequiredService<ViewerDataPaths>()));

        // Shared application-level state
        services.AddSingleton<PortrayalCatalogueManager>();

        // Dataset collections (issue #655): indexed metadata of local folders,
        // exchange sets, online feeds and S-128 catalogues, never the data.
        services.AddSingleton(sp => new EncDotNet.S100.Collections.Indexing.NoaaEncFeedIndexer(
            new System.Net.Http.HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromMinutes(2) },
            sp.GetRequiredService<ViewerDataPaths>().CollectionFeedCacheDirectory));
        services.AddSingleton(sp => new EncDotNet.S100.Collections.Indexing.UsaceIencFeedIndexer(
            new System.Net.Http.HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromMinutes(2) },
            sp.GetRequiredService<ViewerDataPaths>().CollectionFeedCacheDirectory));
        services.AddSingleton(sp => new EncDotNet.S100.Collections.Indexing.S100FeedIndexer(
            new System.Net.Http.HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromMinutes(2) },
            sp.GetRequiredService<ViewerDataPaths>().CollectionFeedCacheDirectory));
        // Remote S-100 exchange catalogues, e.g. NOAA's S-102 on AWS (issue #685).
        services.AddSingleton(sp => new EncDotNet.S100.Collections.Indexing.S100CatalogueFeedIndexer(
            new System.Net.Http.HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromMinutes(2) },
            sp.GetRequiredService<ViewerDataPaths>().CollectionFeedCacheDirectory));
        // S-100 forecast feeds, e.g. NOAA's S-111 on AWS (issue #685).
        services.AddSingleton(sp => new EncDotNet.S100.Collections.Indexing.S100ForecastFeedIndexer(
            new System.Net.Http.HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromMinutes(2) },
            sp.GetRequiredService<ViewerDataPaths>().CollectionFeedCacheDirectory));
        // Weather services (#1075): NOMADS, NOAA's GFS on AWS, api.weather.gov alerts and ERDDAP.
        // The NWS API asks clients to identify themselves: SoundCharts and the project's address, no personal contact.
        services.AddSingleton(sp =>
        {
            var http = new System.Net.Http.HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.UserAgent.TryParseAdd(WeatherUserAgent);
            return new EncDotNet.S100.Collections.Weather.WeatherFeedIndexer(
                http, sp.GetRequiredService<ViewerDataPaths>().DownloadsDirectory, sp.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton(sp =>
        {
            // Community lists index downloaded packages' cells from the downloads folder.
            var paths = sp.GetRequiredService<ViewerDataPaths>();
            var metadata = sp.GetRequiredService<IDatasetMetadataReader>();
            return new EncDotNet.S100.Collections.Indexing.ChartCatalogsFeedIndexer(
                new System.Net.Http.HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromMinutes(2) },
                paths.CollectionFeedCacheDirectory,
                paths.DownloadsDirectory,
                (path, _) => metadata.TryRead(path));
        });
        // SECOM requests trust MCP-issued server certificates (#829); every
        // SECOM client below gets a handler from this one validator, and no
        // other client does.
        // Revocation of MCP-issued certificates (#833): CRLs are fetched over
        // plain HTTP and kept with the feed cache; signers and servers share them.
        services.AddSingleton(sp => new EncDotNet.S100.Collections.Secom.SecomRevocation(
            new System.Net.Http.HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler()),
            sp.GetRequiredService<ViewerDataPaths>().CollectionFeedCacheDirectory));
        // Keys & certificates (#845): private keys of imported MCP identities
        // live in the platform key store; a run whose settings are not saved
        // keeps them in memory, so nothing outlives it.
        services.AddSingleton<Services.Secom.ISecomKeyStore>(sp => sp.GetRequiredService<ViewerSettings>().IsReadOnly
            ? new Services.Secom.InMemorySecomKeyStore()
            : new Services.Secom.PlatformSecomKeyStore());
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ViewerSettings>();
            var logger = sp.GetService<ILogger<App>>();
            // --secom-identity overrides the stored identity for this run (#832, #845 H2).
            EncDotNet.S100.Collections.Secom.SecomClientIdentity? commandLine = null;
            if (StartupOptions?.SecomIdentity is { Length: > 0 } identityPath)
            {
                try
                {
                    commandLine = EncDotNet.S100.Collections.Secom.SecomClientIdentity.Load(
                        identityPath, Environment.GetEnvironmentVariable(SecomIdentityPasswordVariable));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    logger?.LogError(ex, "The SECOM identity {Path} could not be loaded", identityPath);
                }
            }

            return new Services.Secom.SecomCredentials(
                settings,
                sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomRevocation>(),
                sp.GetRequiredService<Services.Secom.ISecomKeyStore>(),
                commandLine,
                logger);
        });
        services.AddSingleton(sp => sp.GetRequiredService<Services.Secom.SecomCredentials>().Trust);
        services.AddSingleton(sp => sp.GetRequiredService<Services.Secom.SecomCredentials>().Identities);
        services.AddSingleton(sp => sp.GetRequiredService<Services.Secom.SecomCredentials>().Authorities);
        services.AddSingleton<EncDotNet.S100.Viewer.Services.IRouteDialogs>(sp => new EncDotNet.S100.Viewer.Services.RouteDialogs(
            sp.GetRequiredService<ShadUI.DialogManager>(),
            sp.GetRequiredService<IFileDialogService>(),
            sp.GetRequiredService<ViewerSettings>().McpTestHooks
                ? (Func<string?>)(static () => Environment.GetEnvironmentVariable(EncDotNet.S100.Viewer.Services.Secom.KeysDialogs.TestPickVariable))
                : null,
            sp.GetRequiredService<ViewerSettings>().McpTestHooks
                ? (Func<string?>)(static () => Environment.GetEnvironmentVariable(EncDotNet.S100.Viewer.Services.RouteDialogs.TestSaveVariable))
                : null));
        services.AddSingleton<ViewModels.Keys.IKeysDialogs>(sp => new Services.Secom.KeysDialogs(
            sp.GetRequiredService<ShadUI.DialogManager>(),
            sp.GetRequiredService<IFileDialogService>(),
            sp.GetRequiredService<ViewerSettings>().McpTestHooks
                ? (Func<string?>)(static () => Environment.GetEnvironmentVariable(EncDotNet.S100.Viewer.Services.Secom.KeysDialogs.TestPickVariable))
                : null));
        services.AddSingleton(sp => new ViewModels.Keys.KeysAndCertificatesViewModel(
            sp.GetRequiredService<Services.Secom.SecomIdentityStore>(),
            sp.GetRequiredService<Services.Secom.TrustedAuthorityStore>(),
            sp.GetRequiredService<ViewModels.Keys.IKeysDialogs>()));
        services.AddSingleton(sp =>
        {
            // SECOM services (issue #804): objects get bounds once downloaded.
            var paths = sp.GetRequiredService<ViewerDataPaths>();
            var metadata = sp.GetRequiredService<IDatasetMetadataReader>();
            var trust = sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomServerTrust>();
            return new EncDotNet.S100.Collections.Indexing.SecomSourceIndexer(
                new System.Net.Http.HttpClient(trust.CreateHandler()) { Timeout = TimeSpan.FromMinutes(2) },
                paths.CollectionFeedCacheDirectory,
                paths.DownloadsDirectory,
                (path, _) => metadata.TryRead(path))
            {
                Revocation = sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomRevocation>(),
                // Each source presents, and signs SECOM 2.0 requests with, its own identity (#838, #883).
                Trust = trust,
            };
        });
        services.AddSingleton(sp =>
        {
            var metadata = sp.GetRequiredService<IDatasetMetadataReader>();
            return EncDotNet.S100.Collections.Indexing.CollectionIndexer.CreateDefault(
                probe: (path, _) => metadata.TryRead(path),
                feeds:
                [
                    sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.NoaaEncFeedIndexer>(),
                    sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.UsaceIencFeedIndexer>(),
                    sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.ChartCatalogsFeedIndexer>(),
                    sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.S100FeedIndexer>(),
                    sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.S100CatalogueFeedIndexer>(),
                    sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.S100ForecastFeedIndexer>(),
                    sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.SecomSourceIndexer>(),
                    sp.GetRequiredService<EncDotNet.S100.Collections.Weather.WeatherFeedIndexer>(),
                ]);
        });
        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<ViewerDataPaths>();
            return new EncDotNet.S100.Collections.Library.CollectionLibrary(
                sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.CollectionIndexer>(),
                new EncDotNet.S100.Collections.Library.CollectionLibraryOptions(paths.CollectionsFilePath, paths.CollectionIndexCacheDirectory)
                {
                    ReadOnly = sp.GetRequiredService<ViewerSettings>().IsReadOnly,
                    SessionCollectionName = EncDotNet.S100.Viewer.Resources.Strings.Library_SessionCollectionName,
                },
                sp.GetService<Microsoft.Extensions.Logging.ILogger<EncDotNet.S100.Collections.Library.CollectionLibrary>>());
        });
        services.AddSingleton<Library.UserCatalogueStore>();
        services.AddSingleton<Func<Uri, CancellationToken, Task<EncDotNet.S100.Collections.KnownSources.CatalogueProbe>>>(sp =>
        {
            // Recognises catalogues the user adds by URL (issue #670); SECOM is asked through the SECOM client.
            var http = new System.Net.Http.HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromSeconds(30) };
            var secom = new System.Net.Http.HttpClient(sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomServerTrust>().CreateHandler())
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            return (uri, ct) => EncDotNet.S100.Collections.KnownSources.CatalogueFormatDetector.ProbeAsync(http, uri, secom, ct);
        });
        services.AddSingleton<Library.ILibraryDownloader>(sp =>
        {
            // One managed folder per provider, chosen by download host, unless
            // the item names its own (community lists: one per list).
            var downloads = sp.GetRequiredService<ViewerDataPaths>().DownloadsDirectory;
            var http = new System.Net.Http.HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.TryParseAdd(WeatherUserAgent);
            var trust = sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomServerTrust>();
            var secom = new System.Net.Http.HttpClient(trust.CreateHandler())
            {
                Timeout = TimeSpan.FromMinutes(10),
            };
            return new Library.LibraryDownloadService(
                new EncDotNet.S100.Collections.Library.LibraryDownloads(
                    EncDotNet.S100.Collections.Library.LibraryDownloads.ManagedFolders(
                        // Signers are judged against the anchors the user chose (#845); each object
                        // presents its source's identity (#883).
                        http, downloads, secom, sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomRevocation>(), trust))
                {
                    Logger = sp.GetService<ILogger<EncDotNet.S100.Collections.Library.LibraryDownloads>>(),
                },
                sp.GetService<Services.Notifications.INotificationService>());
        });
        services.AddSingleton(sp =>
        {
            // Synced SECOM sources keep every listed object downloaded (#807);
            // a copy open in the Datasets panel is not pruned.
            var datasets = sp.GetRequiredService<DatasetsViewModel>();
            bool IsOpen(string folder)
            {
                var prefix = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return Avalonia.Threading.Dispatcher.UIThread.Invoke(() => datasets.Entries.Any(e =>
                    e.FilePath.StartsWith(prefix, StringComparison.Ordinal)
                    || string.Equals(e.FilePath, folder, StringComparison.Ordinal)));
            }

            return new EncDotNet.S100.Collections.Library.LibrarySync(
                sp.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>(),
                ((Library.LibraryDownloadService)sp.GetRequiredService<Library.ILibraryDownloader>()).Downloads,
                new EncDotNet.S100.Collections.Library.LibrarySyncOptions { IsInUse = IsOpen },
                sp.GetService<Microsoft.Extensions.Logging.ILogger<EncDotNet.S100.Collections.Library.LibrarySync>>());
        });
        services.AddSingleton(sp => new Library.LibraryShowOnMapService(
            // Sources shown on the map keep their datasets loading as you pan (#809).
            sp.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>(),
            (Library.LibraryLoadService)sp.GetRequiredService<Library.ILibraryLoader>(),
            sp.GetRequiredService<Library.ILibraryDownloader>(),
            id => sp.GetRequiredService<LibraryPanelViewModel>().SourceDisplayName(id),
            sp.GetRequiredService<EncDotNet.S100.Collections.Library.LibrarySync>()));
        services.AddSingleton<Library.ILibraryLoader>(sp => new Library.LibraryLoadService(
            sp.GetRequiredService<IExchangeSetService>(),
            sp.GetRequiredService<DatasetsViewModel>(),
            sp.GetService<Services.Notifications.INotificationService>()));
        // Reads online catalogues for the Add-to-Library dialog and the Library tools (#792).
        services.AddSingleton(sp =>
        {
            var feeds = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.NoaaEncFeedIndexer>();
            var usace = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.UsaceIencFeedIndexer>();
            var community = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.ChartCatalogsFeedIndexer>();
            var s100Feeds = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.S100FeedIndexer>();
            var s100Catalogues = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.S100CatalogueFeedIndexer>();
            var forecasts = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.S100ForecastFeedIndexer>();
            var secom = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.SecomSourceIndexer>();
            var weather = sp.GetRequiredService<EncDotNet.S100.Collections.Weather.WeatherFeedIndexer>();
            return new EncDotNet.S100.Collections.Library.LibraryCatalogueReaders
            {
                NoaaEnc = (uri, ct) => feeds.GetCatalogAsync(uri, cancellationToken: ct),
                UsaceIenc = (uri, ct) => usace.GetCatalogAsync(uri, cancellationToken: ct),
                CommunityList = (uri, ct) => community.GetCatalogAsync(uri, cancellationToken: ct),
                S100Feed = (uri, ct) => s100Feeds.GetFeedAsync(uri, cancellationToken: ct),
                S100Catalogue = (uri, ct) => s100Catalogues.GetCatalogueAsync(uri, cancellationToken: ct),
                ListS100Folders = (catalogue, folders, ct) => s100Catalogues.ListAsync(catalogue, folders, ct),
                ForecastModels = (uri, models, ct) => forecasts.GetModelsAsync(uri, models, ct),
                Secom = (uri, area, identity, ct) => secom.DescribeAsync(uri, area, identity, ct),
                CurrentMapView = () => MapViewOf(sp.GetService<IMapViewportNotifier>()?.Current),
                Weather = (source, ct) => weather.DescribeAsync(source, ct),
                MarineZones = area => EncDotNet.S100.Datasets.Cap.MarineZoneLayer.Default.CodesIn(area.South, area.West, area.North, area.East),
            };
        });
        services.AddTransient(sp => new AddToLibraryDialogViewModel(
            sp.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>(),
            sp.GetRequiredService<EncDotNet.S100.Collections.Library.LibraryCatalogueReaders>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<Func<AddToLibraryDialogViewModel>>(sp => sp.GetRequiredService<AddToLibraryDialogViewModel>);
        services.AddTransient(sp =>
        {
            var urls = sp.GetService<IUrlOpener>();
            var registry = sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomRegistry>();
            return new CatalogueDirectoryDialogViewModel(
                EncDotNet.S100.Collections.KnownSources.KnownCatalogueSources.All,
                urls is null ? null : uri => urls.Open(uri.AbsoluteUri),
                sp.GetRequiredService<Library.UserCatalogueStore>(),
                sp.GetRequiredService<Func<Uri, CancellationToken, Task<EncDotNet.S100.Collections.KnownSources.CatalogueProbe>>>(),
                loadSecomRegistry: ct => registry.GetServicesAsync(cancellationToken: ct),
                probeSecom: (uri, ct) => registry.ProbeAsync(uri, cancellationToken: ct));
        });
        // SECOM services from the MCP service registry (#822), cached with the feeds.
        services.AddSingleton(sp =>
        {
            var trust = sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomServerTrust>();
            return new EncDotNet.S100.Collections.Secom.SecomRegistry(
                new System.Net.Http.HttpClient(trust.CreateHandler()) { Timeout = TimeSpan.FromSeconds(20) },
                sp.GetRequiredService<ViewerDataPaths>().CollectionFeedCacheDirectory,
                serverTrust: trust);
        });
        // The identities SECOM sources choose from (#883).
        services.AddSingleton(sp => new Services.Secom.SecomIdentityDirectory(
            sp.GetRequiredService<Services.Secom.SecomIdentityStore>(), sp.GetRequiredService<TimeProvider>()));
        services.AddTransient(sp => new AddOnlineCatalogueWizardViewModel(
            sp.GetRequiredService<CatalogueDirectoryDialogViewModel>(),
            sp.GetRequiredService<Func<AddToLibraryDialogViewModel>>(),
            sp.GetRequiredService<Services.Secom.KeysNavigator>().Open,
            // A SECOM service's Connection step: its identity, probed with each choice (#883).
            (source, openKeys) => new ViewModels.Secom.SecomConnectionStepViewModel(
                source,
                sp.GetRequiredService<Services.Secom.SecomIdentityDirectory>(),
                (uri, identity, ct) => sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomRegistry>().ProbeAsync(uri, identity, ct),
                openKeys)));
        // The way from a SECOM certificate problem to Settings → Keys & certificates (#880).
        services.AddSingleton(sp => new Services.Secom.KeysNavigator(
            () => sp.GetRequiredService<SettingsViewModel>(),
            () => sp.GetRequiredService<MainViewModel>()));
        // Reads SECOM sources again when the identity or trusted authorities change (#880).
        services.AddSingleton(sp => new EncDotNet.S100.Collections.Library.SecomTrustRefresh(
            sp.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>(),
            sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomServerTrust>()));
        services.AddSingleton<Func<AddOnlineCatalogueWizardViewModel>>(sp => sp.GetRequiredService<AddOnlineCatalogueWizardViewModel>);
        services.AddTransient(sp => new SharedFeedDialogViewModel(
            sp.GetRequiredService<Func<Uri, CancellationToken, Task<EncDotNet.S100.Collections.KnownSources.CatalogueProbe>>>()));
        services.AddSingleton<Func<SharedFeedDialogViewModel>>(sp => sp.GetRequiredService<SharedFeedDialogViewModel>);
        services.AddSingleton<ILibraryImporter>(sp => new LibraryImportCoordinator(
            sp.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>(),
            sp.GetRequiredService<IFileDialogService>(),
            sp.GetRequiredService<ShadUI.DialogManager>(),
            sp.GetRequiredService<Func<AddToLibraryDialogViewModel>>(),
            sp.GetRequiredService<Func<AddOnlineCatalogueWizardViewModel>>(),
            sp.GetRequiredService<Func<SharedFeedDialogViewModel>>(),
            sp.GetService<IViewerUiControllerAccessor>(),
            sp.GetService<Services.Notifications.INotificationService>()));

        // Feature-catalogue parsing is shared across every dataset load
        // — the manager's parse cache must survive across factory
        // rebuilds. The resolver delegate consults the viewer-level
        // overrides service so transient CLI catalogues and persisted
        // settings remain observable even though the manager itself is
        // a singleton.
        services.AddSingleton<FeatureCatalogueOverrides>();
        services.AddSingleton<EncDotNet.S100.Features.FeatureCatalogueManager>(sp =>
        {
            var overrides = sp.GetRequiredService<FeatureCatalogueOverrides>();
            return new EncDotNet.S100.Features.FeatureCatalogueManager(
                (string spec) => overrides.Open(spec));
        });
        services.AddSingleton<EncDotNet.S100.Datasets.Pipelines.Interoperability.IInteroperabilityAuthorityProvider>(sp =>
            new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthorityProvider(
                new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthority(
                    EncDotNet.S100.Datasets.Pipelines.ProductRegistry.Default.DisplayPlanes)));
        services.AddSingleton<EncDotNet.S100.Datasets.Pipelines.Interoperability.IDisplayPlaneAuthorityProvider>(
            _ => new EncDotNet.S100.Datasets.Pipelines.Interoperability.DisplayPlaneAuthorityProvider());
        services.AddSingleton<EncDotNet.S100.Renderers.Mapsui.IPatternClipCache>(sp =>
        {
            // One process-wide disk cache shared by every S-101 processor so the
            // cold first open of a previously-seen cell skips the multi-second
            // NetTopologySuite pattern-fill clip, even across restarts. The clip
            // geometry is palette-independent and the cache key is content-hash +
            // FormatVersion stamped, so persisted entries auto-invalidate.
            var cacheDir = sp.GetRequiredService<ViewerDataPaths>().PatternClipCacheDirectory;
            const long maxBytes = 256L * 1024 * 1024;
            return new EncDotNet.S100.Renderers.Mapsui.DiskPatternClipCache(cacheDir, maxBytes);
        });
        services.AddSingleton<EncDotNet.S100.Pipelines.Vector.Caching.IPortrayalInstructionCache>(sp =>
        {
            // One process-wide disk cache shared by every S-101 and S-57 processor so a
            // fresh open of a previously-portrayed cell skips the multi-second
            // MoonSharp Part 9A Lua run, even across restarts. The cache key is
            // the portrayal-content hash (dataset bytes + FC/PC content +
            // pipeline / VM assemblies) so persisted entries auto-invalidate
            // when anything affecting the instruction list changes.
            var cacheDir = sp.GetRequiredService<ViewerDataPaths>().PortrayalInstructionCacheDirectory;
            const long maxBytes = 256L * 1024 * 1024;
            return new EncDotNet.S100.Pipelines.Vector.Caching.DiskPortrayalInstructionCache(cacheDir, maxBytes);
        });
        services.AddSingleton<EncDotNet.S100.Core.Metadata.IDatasetMetadataCache>(sp =>
        {
            // One process-wide disk cache of per-dataset "peek" metadata so a
            // previously-probed dataset (e.g. framing a loose-cell folder) costs
            // zero parse on a later session. Entries key on the source file's
            // mtime + size, so editing or replacing a dataset auto-invalidates.
            var cacheDir = sp.GetRequiredService<ViewerDataPaths>().DatasetMetadataCacheDirectory;
            const long maxBytes = 16L * 1024 * 1024;
            return new EncDotNet.S100.Core.Metadata.DiskDatasetMetadataCache(cacheDir, maxBytes);
        });
        services.AddSingleton<IDatasetMetadataReader>(sp =>
            new CachingDatasetMetadataReader(
                sp.GetRequiredService<EncDotNet.S100.Core.Metadata.IDatasetMetadataCache>()));
        services.AddSingleton<Services.Caching.IS57CatalogCache>(sp =>
        {
            // Cross-session cache of an S-57 exchange set's base-cell
            // descriptor list, keyed by the CATALOG.031 file's mtime + size,
            // so a large set re-opens (and feeds lazy registration) without
            // re-parsing the binary ISO 8211 catalogue. Descriptors are tiny,
            // so a modest cap holds many sets.
            var cacheDir = sp.GetRequiredService<ViewerDataPaths>().S57CatalogCacheDirectory;
            const long maxBytes = 16L * 1024 * 1024;
            return new Services.Caching.DiskS57CatalogCache(cacheDir, maxBytes);
        });
        services.AddSingleton<EncDotNet.S100.Datasets.Pipelines.DatasetPipelineFactory>(sp =>
            new EncDotNet.S100.Datasets.Pipelines.DatasetPipelineFactory(
                sp.GetRequiredService<PortrayalCatalogueManager>(),
                new EncDotNet.S100.Scripting.MoonSharp.MoonSharpLuaEngine(),
                new EncDotNet.S100.Crs.ProjNet.ProjNetCrsTransformFactory(),
                sp.GetRequiredService<EncDotNet.S100.Features.FeatureCatalogueManager>(),
                sp.GetRequiredService<EncDotNet.S100.Datasets.Pipelines.Interoperability.IDisplayPlaneAuthorityProvider>(),
                sp.GetRequiredService<EncDotNet.S100.Pipelines.Vector.Caching.IPortrayalInstructionCache>()));

        // The Mapsui renderer owns the processor -> ILayer conversion (issue
        // #189): it holds the process-wide pattern-clip cache and the CRS
        // transform factory used by the coverage renderers. #213 will later
        // unify it under IS100DatasetRenderer<IReadOnlyList<ILayer>>.
        services.AddSingleton<EncDotNet.S100.Renderers.Mapsui.MapsuiDatasetRenderer>(sp =>
            new EncDotNet.S100.Renderers.Mapsui.MapsuiDatasetRenderer(
                new EncDotNet.S100.Crs.ProjNet.ProjNetCrsTransformFactory(),
                sp.GetRequiredService<EncDotNet.S100.Renderers.Mapsui.IPatternClipCache>()));

        // Leaf services extracted in phase 2
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IRecentFilesService, RecentFilesService>();
        services.AddSingleton<IDataMaintenanceService, DataMaintenanceService>();
        services.AddSingleton<IApplicationControlService, ApplicationControlService>();
        services.AddSingleton<PortrayalCatalogueSeeder>();
        services.AddSingleton<EncDotNet.S100.Viewer.Tools.IMeasureOverlayAppearanceProvider, MeasureOverlayAppearanceProvider>();
        services.AddSingleton<EncDotNet.S100.Viewer.Services.IViewTime>(sp => new EncDotNet.S100.Viewer.Services.TimelineViewTime(
            sp.GetRequiredService<GlobalTimeService>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<EncDotNet.S100.Viewer.Services.RoutesService>();
        // The measure tool's path and geometry (#1130), shared by the tool, its card and MCP.
        services.AddSingleton(sp => new EncDotNet.S100.Viewer.Services.MeasureSession(sp.GetRequiredService<ViewerSettings>()));
        services.AddSingleton(sp => new EncDotNet.S100.Viewer.ViewModels.MeasureCardViewModel(
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.MeasureSession>()));
        // One set of route-check sources per set of loaded datasets, shared by every route check.
        services.AddSingleton(_ => new EncDotNet.S100.Datasets.Pipelines.Navigation.RouteCheckSourceCache(
            new EncDotNet.S100.Crs.ProjNet.ProjNetCrsTransformFactory()));
        // The Navigational warnings panel's list, as the warnings the route check reads (#957).
        services.AddSingleton(sp => new EncDotNet.S100.Viewer.Services.PassageCheck.PanelRouteWarningSource(
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.NavigationalWarningsService>()));
        // Check route (#901, #1034): one RouteChecker over the loaded datasets and the panel's
        // warnings, recomputed off the UI thread with its report delivered on it.
        services.AddSingleton<EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService>(sp =>
            new EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.RoutesService>(),
                sp.GetRequiredService<ViewerDatasetCatalog>(),
                sources: sp.GetRequiredService<EncDotNet.S100.Datasets.Pipelines.Navigation.RouteCheckSourceCache>(),
                warnings: sp.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.PanelRouteWarningSource>(),
                mariner: sp.GetService<IMarinerSettingsProvider>(),
                post: action =>
                {
                    if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                        action();
                    else
                        Avalonia.Threading.Dispatcher.UIThread.Post(action);
                }));
        // Follow passage (#904): pins the view time to the ETA at the profile cursor.
        services.AddSingleton<EncDotNet.S100.Viewer.Services.PassageCheck.FollowPassageService>(sp =>
            new EncDotNet.S100.Viewer.Services.PassageCheck.FollowPassageService(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService>(),
                sp.GetRequiredService<GlobalTimeService>()));
        // Findings list (#905): the report's findings in the viewer's wording; selecting one
        // zooms the map to it and moves the profile cursor there.
        services.AddSingleton<EncDotNet.S100.Viewer.Services.PassageCheck.RouteFindingsService>(sp =>
            new EncDotNet.S100.Viewer.Services.PassageCheck.RouteFindingsService(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.RoutesService>(),
                sp.GetRequiredService<ViewerDatasetCatalog>(),
                sp.GetRequiredService<ICapabilityAccessor<IMapViewportController>>(),
                sp.GetService<IMarinerSettingsProvider>(),
                sp.GetService<ITimeFormatProvider>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.NavigationalWarningsService>()));
        // The Check route map legend's fold, kept per user (#959, C8).
        services.AddSingleton(sp => new EncDotNet.S100.Viewer.Services.PassageCheck.RouteCheckLegendState(
            sp.GetRequiredService<ViewerSettings>()));
        // The Range & bearing tool's model, shared by the map tool, its card and
        // the MCP tools; its geometry is kept per user (#1130 D15).
        services.AddSingleton(sp => new EncDotNet.S100.Viewer.Tools.RangeBearingState(
            sp.GetRequiredService<ViewerSettings>()));
        // Loads persisted routes at startup and writes them back (debounced)
        // on change, so user/agent routes survive a restart. Eagerly
        // initialized in OnFrameworkInitializationCompleted and flushed on
        // ShutdownRequested.
        services.AddSingleton<EncDotNet.S100.Viewer.Services.RoutePersistenceService>();
        // Received routes (#960): S-421 datasets opened as routes (Import route…,
        // the Library's Open as route) become read-only routes; their check
        // settings are kept by route ID + edition.
        services.AddSingleton(sp =>
        {
            var checkSettings = new EncDotNet.S100.Viewer.Routing.Persistence.CheckSettingsStore(
                sp.GetRequiredService<ViewerSettings>().IsReadOnly ? null : sp.GetRequiredService<ViewerDataPaths>().CheckSettingsFilePath);
            checkSettings.Load();
            var datasets = sp.GetRequiredService<DatasetsViewModel>();
            return new EncDotNet.S100.Viewer.Services.ReceivedRoutesService(
                sp.GetRequiredService<IDatasetLoaderService>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.RoutesService>(),
                checkSettings,
                entry => datasets.Entries.Remove(entry),
                sp.GetService<EncDotNet.S100.Viewer.Services.Notifications.INotificationService>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.FollowPassageService>());
        });
        // Import route…, Copy to edit and Export… (#960), shared by the Routes panel and MCP.
        services.AddSingleton(sp =>
        {
            var datasets = sp.GetRequiredService<DatasetsViewModel>();
            return new EncDotNet.S100.Viewer.Services.RouteExchangeService(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.RoutesService>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.ReceivedRoutesService>(),
                // A file already open as a dataset is opened as a route as it is.
                openDataset: path => datasets.Entries.FirstOrDefault(e => e.IsLoaded && !e.IsFromExchangeSet && EncDotNet.S100.Viewer.Services.ReceivedRoutesService.SamePath(e.FilePath, path)) is { } open
                    ? Task.FromResult<DatasetEntry?>(open)
                    : datasets.LoadFromPathAsync(path),
                // As select_dataset finds a list_datasets id: by name, then by id.
                findDataset: id => datasets.Entries.FirstOrDefault(e => string.Equals(e.DisplayName, id, StringComparison.Ordinal))
                    ?? datasets.Entries.FirstOrDefault(e => string.Equals(e.Id.Value, id, StringComparison.Ordinal)));
        });

        // Phase 3 services: dataset orchestration, pick dispatch, file dialogs
        services.AddSingleton<GlobalTimeService>();
        services.AddSingleton<TimeRefreshProgress>();
        services.AddSingleton(sp => new LayerTimeCoordinator(
            sp.GetRequiredService<GlobalTimeService>(),
            sp.GetRequiredService<DatasetsViewModel>(),
            sp.GetRequiredService<TimeRefreshProgress>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ITimeFormatProvider>(),
            library: () => sp.GetService<ILibraryTimeSource>()));
        services.AddSingleton<EcdisDisplayState>(sp =>
            EcdisDisplayStatePersistence.Create(sp.GetRequiredService<ViewerSettings>()));
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        // One clock for every clock-driven view model; with --mcp-test-hooks it
        // is adjustable so set_test_clock can age forecasts and Live (#715).
        services.AddSingleton<TimeProvider>(sp =>
            sp.GetRequiredService<ViewerSettings>().McpTestHooks
                ? new AdjustableTimeProvider()
                : TimeProvider.System);
        services.AddSingleton<INotificationService, NotificationService>();

        // Feedback reporting: diagnostics capture + modal dialog plumbing.
        services.AddSingleton<EncDotNet.S100.Viewer.Diagnostics.ILastErrorTracker,
            EncDotNet.S100.Viewer.Diagnostics.LastErrorTracker>();
        services.AddSingleton<EncDotNet.S100.Viewer.Diagnostics.ICrashHistory,
            EncDotNet.S100.Viewer.Diagnostics.CrashHistory>();
        services.AddSingleton<IAppScreenshotProvider, AppScreenshotProvider>();
        services.AddSingleton<ShadUI.DialogManager>();
        services.AddSingleton<IFeedbackService, FeedbackService>();
        services.AddTransient<FeedbackDialogViewModel>();
        services.AddSingleton<Func<FeedbackDialogViewModel>>(sp =>
            sp.GetRequiredService<FeedbackDialogViewModel>);

        // About dialog + GitHub-release update check (issue #379).
        services.AddSingleton<IUrlOpener>(sp =>
            new ProcessUrlOpener(sp.GetService<ILogger<ProcessUrlOpener>>()));
        // S-100 Feature Catalogue eXaminer deep-links (issue #442).
        services.AddSingleton<IS100ExaminerLinkBuilder>(
            sp => new S100ExaminerLinkBuilder(sp.GetRequiredService<ViewerSettings>()));
        services.AddSingleton<EncDotNet.S100.Viewer.Services.Updates.IAppVersionProvider>(
            _ => new EncDotNet.S100.Viewer.Services.Updates.AssemblyAppVersionProvider());
        services.AddSingleton<EncDotNet.S100.Viewer.Services.Updates.IGitHubReleaseClient>(sp =>
            new EncDotNet.S100.Viewer.Services.Updates.GitHubReleaseClient(
                new System.Net.Http.HttpClient(EncDotNet.S100.Collections.Net.HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromSeconds(10) },
                sp.GetService<ILogger<EncDotNet.S100.Viewer.Services.Updates.GitHubReleaseClient>>()));
        services.AddSingleton<EncDotNet.S100.Viewer.Services.Updates.IUpdateService>(sp =>
            new EncDotNet.S100.Viewer.Services.Updates.UpdateService(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.Updates.IGitHubReleaseClient>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.Updates.IAppVersionProvider>(),
                sp.GetRequiredService<ViewerSettings>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetService<ILogger<EncDotNet.S100.Viewer.Services.Updates.UpdateService>>()));
        services.AddSingleton<IUpdateNotificationCoordinator, UpdateNotificationCoordinator>();
        services.AddTransient<AboutDialogViewModel>();
        services.AddSingleton<Func<AboutDialogViewModel>>(sp =>
            sp.GetRequiredService<AboutDialogViewModel>);

        services.AddSingleton<DatasetProcessorOwner>();
        services.AddSingleton<DatasetLoaderService>();
        services.AddSingleton<IDatasetLoaderService>(sp =>
            sp.GetRequiredService<DatasetLoaderService>());
        services.AddSingleton<IMapPresentationController>(sp =>
            sp.GetRequiredService<DatasetLoaderService>());
        services.AddSingleton<IPickService, PickService>();
        services.AddSingleton<IGeographicPickPresenter>(sp =>
            new DispatcherGeographicPickPresenter(sp.GetRequiredService<IPickService>()));
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.IDynamicSourcePickService>(sp =>
            new EncDotNet.S100.Viewer.Services.DynamicSources.DynamicSourcePickService(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.DynamicFeatureSourceRegistryAccessor>()));
        services.AddSingleton<IFeatureSearchService, FeatureSearchService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IExchangeSetService, ExchangeSetService>();

        // Viewport-driven lazy loading of large exchange sets (issue #458):
        // registers cells without loading them and loads only what is in view.
        services.AddSingleton<EncDotNet.S100.Viewer.Services.LazyLoading.ExchangeSetLazyLoadCoordinator>(sp =>
        {
            var datasets = sp.GetRequiredService<DatasetsViewModel>();
            var loader = sp.GetRequiredService<IDatasetLoaderService>();
            var notifier = sp.GetRequiredService<
                EncDotNet.S100.Viewer.Services.IMapViewportNotifier>();
            return new EncDotNet.S100.Viewer.Services.LazyLoading.ExchangeSetLazyLoadCoordinator(
                notifier,
                (entry, ct) => datasets.RequestLoadAsync(entry, ct),
                loader.UnloadEntry,
                logger: sp.GetService<ILogger<
                    EncDotNet.S100.Viewer.Services.LazyLoading.ExchangeSetLazyLoadCoordinator>>(),
                marshal: action =>
                {
                    if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
                    else Avalonia.Threading.Dispatcher.UIThread.Post(action);
                },
                hasVisibleTileWork: () =>
                    EncDotNet.S100.Renderers.Mapsui.S100VectorTileRenderer.HasVisibleTileWork);
        });

        // Own-ship dynamic source. The steerable driver dead-reckons a
        // moving point seeded at Solent (50.8°N 1.3°W) tracking due east
        // at 5 m/s (~9.7 kn) and exposes IOwnShipHelm so map gestures,
        // the helm panel, the MCP set_own_ship tool, and pirate mode can
        // steer it. A future real-GPS / NMEA-replay driver implements
        // IOwnShipPositionProvider instead. The source is also exposed as
        // IDynamicFeatureSource so the overlay host discovers it via
        // GetServices&lt;IDynamicFeatureSource&gt;().
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.SteerableOwnShipPositionProvider>(_ =>
            new EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.SteerableOwnShipPositionProvider(
                start: new EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipPosition(
                    Latitude: 50.8,
                    Longitude: -1.3,
                    CourseOverGround: EncDotNet.S100.Quantities.Angle.FromDegrees(90.0),
                    SpeedOverGround: EncDotNet.S100.Quantities.Speed.FromMetresPerSecond(5.0),
                    Timestamp: DateTimeOffset.UtcNow),
                cadence: TimeSpan.FromSeconds(1)));
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipPositionProvider>(sp =>
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.SteerableOwnShipPositionProvider>());
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipHelm>(sp =>
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.SteerableOwnShipPositionProvider>());
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipHelmState>(sp =>
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.SteerableOwnShipPositionProvider>());

        // Vessel geometry provider — reads user-configured dimensions
        // from ViewerSettings.OwnShip and pushes them onto every
        // DynamicFeature so OwnShipRenderer can draw a true-scale hull.
        // The settings provider is wrapped by the overridable provider so
        // pirate mode can temporarily adopt an impersonated target's
        // dimensions without persisting them.
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.SettingsOwnShipVesselGeometryProvider>();
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OverridableOwnShipVesselGeometryProvider>(sp =>
            new EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OverridableOwnShipVesselGeometryProvider(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.SettingsOwnShipVesselGeometryProvider>()));
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipVesselGeometryProvider>(sp =>
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OverridableOwnShipVesselGeometryProvider>());
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipVesselGeometryOverride>(sp =>
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OverridableOwnShipVesselGeometryProvider>());

        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipSource>(sp =>
            new EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipSource(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipPositionProvider>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipVesselGeometryProvider>(),
                initiallyEnabled: sp.GetRequiredService<ViewerSettings>().OwnShipOverlayEnabled));
        services.AddSingleton<EncDotNet.S100.DynamicSources.IDynamicFeatureSource>(sp =>
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipSource>());

        // Map-viewport notifier (singleton). Inert until MainWindow
        // calls Bind(navigator) once the MapControl exists. Used by
        // the AIS overlay's zoom-gated decorator (see
        // docs/design/ais-zoom-gated-subscription.md).
        services.AddSingleton<EncDotNet.S100.Viewer.Services.MapViewportNotifier>();
        services.AddSingleton<EncDotNet.S100.Viewer.Services.IMapViewportNotifier>(sp =>
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.MapViewportNotifier>());

        // PR-D? upgraded own-ship symbology: register OwnShipRenderer
        // under the "ownship" key so the dynamic-source host resolves
        // it for the own-ship source (RendererKey = "ownship").
        EncDotNet.S100.Renderers.Mapsui.DynamicSources.DynamicFeatureRendererServiceCollectionExtensions
            .AddDynamicFeatureRenderer<EncDotNet.S100.Renderers.Mapsui.DynamicSources.OwnShipRenderer>(
                services,
                rendererKey: EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipSource.FeatureKind);

        // PR-D3: AIS overlay. The renderer is always registered (idempotent
        // and harmless when no AIS source is active). The dynamic source
        // itself is constructed lazily via Services.DynamicSources.Ais.
        // AisOverlayServiceCollectionExtensions.AddAisOverlay so it can
        // be conditioned on settings + the API-key environment variable.
        EncDotNet.S100.Renderers.Mapsui.DynamicSources.DynamicFeatureRendererServiceCollectionExtensions
            .AddDynamicFeatureRenderer<EncDotNet.S100.Renderers.Mapsui.DynamicSources.AisVesselRenderer>(
                services,
                rendererKey: "vessel.ais");
        EncDotNet.S100.Viewer.Services.DynamicSources.Ais.AisOverlayServiceCollectionExtensions
            .AddAisOverlay(services);

        // #1070: NDBC buoy observations. Registered always and hidden until
        // the user ticks its Layer Stack row; it fetches nothing while hidden
        // or zoomed out (see Services.DynamicSources.Ndbc).
        EncDotNet.S100.Viewer.Services.DynamicSources.Ndbc.NdbcObservationsServiceCollectionExtensions
            .AddNdbcObservations(services);

        // #1073: own-ship wind and water temperature from NMEA 0183. Only
        // registered when SOUNDCHARTS_NMEA_INPUT names an input (UDP, TCP or
        // a recording); see Services.DynamicSources.Nmea.
        EncDotNet.S100.Viewer.Services.DynamicSources.Nmea.NmeaWeatherServiceCollectionExtensions
            .AddNmeaWeather(services);

        // Pirate mode: own-ship impersonates a selected live AIS target.
        // The controller reads the raw AIS source (via the exclusion
        // decorator's Inner) so it still sees the followed target, drives
        // the helm with each report, adopts the target's dimensions via
        // the geometry override, and tells the decorator to hide the
        // followed target so it is not double-drawn.
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.PirateModeController>(sp =>
            new EncDotNet.S100.Viewer.Services.DynamicSources.PirateModeController(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.Ais.ExcludingAisFeatureSource>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipHelm>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipVesselGeometryOverride>()));

        // PR-D2.1: dynamic-source registry accessor. The real registry
        // is the reusable S100DynamicSourceHost constructed in MainWindow
        // (it needs map layer capabilities, which only exist after the MapControl
        // initialises). The accessor is the indirection: view-models
        // depend on it through IS100DynamicSourceRegistry; MainWindow
        // assigns Current once the host is built. Mirrors
        // typed map capability accessors below.
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.DynamicFeatureSourceRegistryAccessor>();
        services.AddSingleton<EncDotNet.S100.Renderers.Mapsui.DynamicSources.IS100DynamicSourceRegistry>(sp =>
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.DynamicFeatureSourceRegistryAccessor>());

        // MCP server (loopback-only, off by default). The catalog adapter
        // observes the existing dataset loader and re-opens dataset files
        // for read-only MCP queries; the host owns server lifecycle.
        services.AddSingleton<ViewerDatasetCatalog>();
        services.AddSingleton<MapCapabilityAccessor<IImageRenderer>>();
        services.AddSingleton<ICapabilityAccessor<IImageRenderer>>(sp =>
            sp.GetRequiredService<MapCapabilityAccessor<IImageRenderer>>());
        services.AddSingleton<MapCapabilityAccessor<IMapViewportController>>();
        services.AddSingleton<ICapabilityAccessor<IMapViewportController>>(sp =>
            sp.GetRequiredService<MapCapabilityAccessor<IMapViewportController>>());
        services.AddSingleton<MapCapabilityAccessor<IMapCoordinateConverter>>();
        services.AddSingleton<ICapabilityAccessor<IMapCoordinateConverter>>(sp =>
            sp.GetRequiredService<MapCapabilityAccessor<IMapCoordinateConverter>>());
        services.AddSingleton<IRenderStateControllerAccessor, RenderStateControllerAccessor>();
        services.AddSingleton<IViewerUiControllerAccessor, ViewerUiControllerAccessor>();
        services.AddSingleton<EncDotNet.S100.Viewer.Diagnostics.RenderActivityMonitor>();
        services.AddSingleton<IRenderActivityMonitor>(sp =>
            sp.GetRequiredService<EncDotNet.S100.Viewer.Diagnostics.RenderActivityMonitor>());
        // Gateway over the existing GUI load/unload code path, used by the
        // mutating open_dataset / close_dataset / close_all_datasets MCP tools
        // so they reuse the same Add + LoadAsync / Remove + RemoveEntry flow
        // as the file-open command rather than a parallel loader.
        services.AddSingleton<IDatasetLoadGateway>(sp => new DatasetLoadGateway(
            sp.GetRequiredService<DatasetsViewModel>(),
            sp.GetRequiredService<IDatasetLoaderService>(),
            sp.GetRequiredService<IExchangeSetService>()));
        services.AddSingleton<McpServerHost>(sp => new McpServerHost(
            sp.GetRequiredService<ViewerDatasetCatalog>(),
            sp.GetRequiredService<ViewerSettings>(),
            sp.GetRequiredService<ICapabilityAccessor<IImageRenderer>>(),
            sp.GetRequiredService<ICapabilityAccessor<IMapViewportController>>(),
            sp.GetRequiredService<ICapabilityAccessor<IMapCoordinateConverter>>(),
            sp.GetService<ILoggerFactory>(),
            sp.GetRequiredService<IRenderStateControllerAccessor>(),
            sp.GetRequiredService<GlobalTimeService>(),
            sp.GetRequiredService<IRenderActivityMonitor>(),
            sp.GetRequiredService<IDatasetLoadGateway>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipHelm>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.RoutesService>(),
            sp.GetRequiredService<IGeographicPickPresenter>(),
            sp.GetRequiredService<IViewerUiControllerAccessor>(),
            sp.GetRequiredService<IAppScreenshotProvider>(),
            sp.GetRequiredService<MapPresentationStateProjection>(),
            new ViewerTimelineController(
                sp.GetRequiredService<GlobalTimeService>(),
                sp.GetRequiredService<TimelineViewModel>(),
                sp.GetRequiredService<DatasetsViewModel>(),
                sp.GetRequiredService<TimeProvider>()),
            new ViewerDatasetStateController(sp.GetRequiredService<DatasetsViewModel>()),
            new ViewerNotificationController(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.Notifications.INotificationService>()),
            sp.GetRequiredService<TimeProvider>() as AdjustableTimeProvider,
            sp.GetRequiredService<ViewerLibraryController>(),
            new ViewerLibraryEditor(
                sp.GetRequiredService<LibraryPanelViewModel>(),
                sp.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>(),
                sp.GetRequiredService<ViewerLibraryController>(),
                new EncDotNet.S100.Mcp.Tools.Library.LibrarySourceAdder(
                    sp.GetRequiredService<EncDotNet.S100.Collections.Library.LibraryCatalogueReaders>(),
                    () => sp.GetRequiredService<Library.UserCatalogueStore>().Sources,
                    sp.GetRequiredService<Func<Uri, CancellationToken, Task<EncDotNet.S100.Collections.KnownSources.CatalogueProbe>>>(),
                    sp.GetRequiredService<TimeProvider>())),
            // Only with --mcp-test-hooks: the ui_* tools are for scripted testing.
            sp.GetRequiredService<ViewerSettings>().McpTestHooks ? ViewerUiAutomation.ForApplication() : null,
            sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomRegistry>(),
            sp.GetRequiredService<EncDotNet.S100.Collections.Secom.SecomServerTrust>(),
            new Services.Secom.SecomIdentityReferences(sp.GetRequiredService<Services.Secom.SecomIdentityStore>()),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService>(),
            sp.GetRequiredService<ClearanceViewModel>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.RouteFindingsService>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.FollowPassageService>(),
            sp.GetRequiredService<NavigationalWarningsViewModel>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.NavigationalWarningsService>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.RouteExchangeService>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.RouteCheckLegendState>(),
            sp.GetRequiredService<EncDotNet.S100.Datasets.Pipelines.Navigation.RouteCheckSourceCache>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Diagnostics.LogBuffer>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.Depth.DepthBasisService>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DisplayCombinations.DisplayCombinationService>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.MeasureSession>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.ViewModels.MeasureCardViewModel>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Tools.RangeBearingState>()));
        services.AddSingleton(sp => new ViewerLibraryController(
            sp.GetRequiredService<LibraryPanelViewModel>(),
            () => sp.GetRequiredService<Library.UserCatalogueStore>().Sources));

        // View models
        services.AddSingleton<FeatureCataloguesViewModel>(sp => new FeatureCataloguesViewModel(
            sp.GetRequiredService<ViewerSettings>(),
            sp.GetService<IS100ExaminerLinkBuilder>(),
            sp.GetService<IUrlOpener>()));
        services.AddSingleton<PortrayalCataloguesViewModel>();
        services.AddSingleton<DatasetsViewModel>();
        services.AddSingleton(sp =>
        {
            // A shared feed's status line reports whether its server was reachable.
            var feeds = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.S100FeedIndexer>();
            var catalogues = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.S100CatalogueFeedIndexer>();
            var forecasts = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.S100ForecastFeedIndexer>();
            var panel = new LibraryPanelViewModel(
                sp.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>(),
                sp.GetRequiredService<ILibraryImporter>(),
                sp.GetRequiredService<Library.ILibraryLoader>(),
                sp.GetRequiredService<Library.ILibraryDownloader>(),
                source => source switch
                {
                    EncDotNet.S100.Collections.S100FeedSource feed => feeds.HealthOf(feed.FeedUri),
                    EncDotNet.S100.Collections.S100CatalogueFeedSource catalogue => catalogues.HealthOf(catalogue.CatalogUri),
                    EncDotNet.S100.Collections.S100ForecastFeedSource forecast => forecasts.HealthOf(forecast),
                    _ => null,
                },
                time: sp.GetRequiredService<TimeProvider>(),
                viewTime: sp.GetRequiredService<GlobalTimeService>(),
                timeFormat: sp.GetService<ITimeFormatProvider>(),
                sync: sp.GetRequiredService<EncDotNet.S100.Collections.Library.LibrarySync>());
            panel.OpenKeys = sp.GetRequiredService<Services.Secom.KeysNavigator>().Open;
            // Open as route: with Import route…, the only way a dataset becomes a route.
            panel.OpenRoute = item =>
            {
                new LibraryRouteOpener(
                    sp.GetRequiredService<Library.ILibraryLoader>(),
                    sp.GetRequiredService<ReceivedRoutesService>(),
                    () => sp.GetRequiredService<MainViewModel>().SelectTab("Routes"),
                    sp.GetService<INotificationService>()).Open(item);
                return Task.CompletedTask;
            };
            // SECOM rows name their identity; a selected source's details choose it (#883).
            panel.Identities = sp.GetRequiredService<Services.Secom.SecomIdentityDirectory>();
            return panel;
        });
        services.AddSingleton<LibraryTimeSource>(sp => new LibraryTimeSource(
            sp.GetRequiredService<LibraryPanelViewModel>(),
            sp.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>(),
            sp.GetRequiredService<Library.ILibraryLoader>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ILibraryTimeSource>(sp => sp.GetRequiredService<LibraryTimeSource>());
        services.AddSingleton<LayerStackViewModel>();
        services.AddSingleton<FeatureSearchViewModel>();
        services.AddSingleton<VesselListViewModel>(sp => new VesselListViewModel(
            sp.GetServices<EncDotNet.S100.DynamicSources.IDynamicFeatureSource>(),
            sp.GetRequiredService<ICapabilityAccessor<IMapViewportController>>(),
            sp.GetService<EncDotNet.S100.Viewer.Services.DynamicSources.PirateModeController>(),
            sp.GetService<EncDotNet.S100.Viewer.Services.DynamicSources.Ais.ExcludingAisFeatureSource>()?.Inner));
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<IMarinerSettingsProvider, MarinerSettingsProvider>();
        services.AddSingleton<MapPresentationStateProjection>();
        services.AddSingleton(sp =>
            sp.GetRequiredService<MapPresentationStateProjection>().CreateSnapshot());
        services.AddSingleton<ViewerPresentationCoordinator>();
        services.AddSingleton<PresentationRevealCoordinator>();
        services.AddSingleton<ITimeFormatProvider, TimeFormatProvider>();
        services.AddSingleton<PickReportViewModel>(sp =>
        {
            var pick = new PickReportViewModel(
                sp.GetService<ITimeFormatProvider>(),
                sp.GetService<IMarinerSettingsProvider>(),
                sp.GetService<IUrlOpener>(),
                sp.GetService<IS100ExaminerLinkBuilder>(),
                sp.GetService<EncDotNet.S100.Viewer.Services.IViewTime>());
            // The depth card's "The chart shows charted depths" line (#1191).
            if (sp.GetService<EncDotNet.S100.Viewer.Services.Depth.DepthBasisService>() is { } depthBasis)
            {
                pick.SetDepthBasis(depthBasis.Current);
                depthBasis.Changed += (_, _) => pick.SetDepthBasis(depthBasis.Current);
            }
            return pick;
        });
        services.AddSingleton<IForecastRunRefresher>(sp => new LibraryForecastRunRefresher(sp.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>()));
        services.AddSingleton<TimelineMapScope>(sp => new TimelineMapScope(
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.IMapViewportNotifier>(),
            sp.GetRequiredService<DatasetsViewModel>()));
        services.AddSingleton<ITimelineMapScope>(sp => sp.GetRequiredService<TimelineMapScope>());
        // Depth basis indicator (#1191). Chart water level adjustment (#1132)
        // replaces the always-off state with its own.
        services.AddSingleton<EncDotNet.S100.Viewer.Services.Depth.IWaterLevelAdjustmentState>(
            EncDotNet.S100.Viewer.Services.Depth.NoWaterLevelAdjustment.Instance);
        services.AddSingleton(sp => new EncDotNet.S100.Viewer.Services.Depth.DepthBasisService(
            sp.GetRequiredService<IDatasetLoaderService>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.IMapViewportNotifier>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.IViewTime>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.Depth.IWaterLevelAdjustmentState>(),
            sp.GetRequiredService<ITimelineMapScope>()));
        services.AddSingleton<TimelineViewModel>(sp =>
        {
            var timeline = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<TimelineViewModel>(sp);
            // Passage lanes and Follow passage (#904).
            timeline.AttachPassage(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.FollowPassageService>());
            return timeline;
        });
        services.AddSingleton<DisplayToolbarViewModel>();
        services.AddSingleton<TextGroupToolbarViewModel>();
        services.AddSingleton<DisplayModeToolbarViewModel>();
        services.AddSingleton<EcdisLabelOverrideProvider>();
        // Predefined display combinations (#1192): the View pill and the
        // Display controls' View section.
        services.AddSingleton(sp => new EncDotNet.S100.Viewer.Services.DisplayCombinations.DisplayCombinationService(
            sp.GetRequiredService<EcdisDisplayState>(),
            sp.GetRequiredService<IDatasetLoaderService>(),
            sp.GetRequiredService<DatasetsViewModel>(),
            sp.GetRequiredService<ViewerSettings>(),
            action => Avalonia.Threading.Dispatcher.UIThread.Post(action)));
        services.AddSingleton(sp => new DisplayCombinationsViewModel(
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DisplayCombinations.DisplayCombinationService>(),
            sp.GetRequiredService<IViewerUiControllerAccessor>()));
        services.AddSingleton<EcdisDisplayPanelViewModel>();
        services.AddSingleton<HelmViewModel>();
        services.AddSingleton<RoutesPanelViewModel>();
        services.AddSingleton(sp => new EncDotNet.S100.Viewer.Services.NavigationalWarningSeenStore(
            sp.GetRequiredService<ViewerDataPaths>().NavigationalWarningsFilePath,
            sp.GetRequiredService<ViewerSettings>().IsReadOnly,
            sp.GetService<Microsoft.Extensions.Logging.ILogger<EncDotNet.S100.Viewer.Services.NavigationalWarningSeenStore>>()));
        services.AddSingleton(sp => new EncDotNet.S100.Viewer.Services.NavigationalWarningClassificationStore(
            sp.GetRequiredService<ViewerDataPaths>().NavigationalWarningClassificationsFilePath,
            sp.GetRequiredService<ViewerSettings>().IsReadOnly,
            sp.GetService<Microsoft.Extensions.Logging.ILogger<EncDotNet.S100.Viewer.Services.NavigationalWarningClassificationStore>>()));
        services.AddSingleton<EncDotNet.S100.Viewer.Services.INavigationalWarningSources>(sp =>
        {
            // A warning's source and freshness: SECOM services and shared feeds report when they last answered (#932).
            var secom = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.SecomSourceIndexer>();
            var feeds = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.S100FeedIndexer>();
            var catalogues = sp.GetRequiredService<EncDotNet.S100.Collections.Indexing.S100CatalogueFeedIndexer>();
            return new EncDotNet.S100.Viewer.Services.LibraryNavigationalWarningSources(
                sp.GetRequiredService<EncDotNet.S100.Collections.Library.CollectionLibrary>(),
                sp.GetRequiredService<DatasetsViewModel>(),
                sp.GetRequiredService<Library.ILibraryDownloader>(),
                source => source switch
                {
                    EncDotNet.S100.Collections.SecomSource service => secom.HealthOf(service),
                    EncDotNet.S100.Collections.S100FeedSource feed => feeds.HealthOf(feed.FeedUri),
                    EncDotNet.S100.Collections.S100CatalogueFeedSource catalogue => catalogues.HealthOf(catalogue.CatalogUri),
                    _ => null,
                },
                id => sp.GetRequiredService<LibraryPanelViewModel>().SourceDisplayName(id));
        });
        services.AddSingleton(sp => new EncDotNet.S100.Viewer.Services.NavigationalWarningsService(
            sp.GetRequiredService<IDatasetLoaderService>(),
            sp.GetRequiredService<DatasetsViewModel>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.INavigationalWarningSources>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.NavigationalWarningSeenStore>(),
            sp.GetService<TimeProvider>(),
            // The NEW distance filter measures from own ship while it is shown, and the active route (#997).
            ownShip: () =>
            {
                var source = sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipSource>();
                var fix = sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipPositionProvider>().Current;
                return source.IsEnabled && fix is { } f ? new EncDotNet.S100.DataModel.GeoPosition(f.Latitude, f.Longitude) : null;
            },
            activeRoute: () => sp.GetRequiredService<EncDotNet.S100.Viewer.Services.RoutesService>().Routes.ActiveRoute?.Waypoints
                .Select(w => w.Position).ToList(),
            classifications: sp.GetRequiredService<EncDotNet.S100.Viewer.Services.NavigationalWarningClassificationStore>()));
        services.AddSingleton(sp => new NavigationalWarningsViewModel(
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.NavigationalWarningsService>(),
            sp.GetRequiredService<IPickService>(),
            sp.GetRequiredService<PickReportViewModel>(),
            sp.GetService<EncDotNet.S100.Viewer.Services.IViewTime>(),
            sp.GetService<ITimeFormatProvider>(),
            sp.GetRequiredService<ICapabilityAccessor<IMapViewportController>>(),
            sp.GetRequiredService<IDatasetLoaderService>(),
            sp.GetService<TimeProvider>())
        {
            NewOptions = new NavigationalWarningNewOptionsViewModel(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.NavigationalWarningsService>(),
                sp.GetRequiredService<ViewerSettings>(),
                sp.GetService<TimeProvider>()),
            // #959 E3: the checked route's warnings, and the findings list's inline cards' navigator.
            RouteFindings = sp.GetService<EncDotNet.S100.Viewer.Services.PassageCheck.RouteFindingsService>(),
        });
        services.AddSingleton<MainViewModel>();
        services.AddSingleton(sp => new EncDotNet.S100.Viewer.Services.DateDependentDrawingService(
            sp.GetRequiredService<IDatasetLoaderService>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.IViewTime>(),
            sp.GetService<EncDotNet.S100.Viewer.Services.ITimeFormatProvider>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            action => Avalonia.Threading.Dispatcher.UIThread.Post(action)));
        services.AddSingleton(sp =>
        {
            var drawing = new EncDotNet.S100.Viewer.Services.NavigationalWarningDrawingService(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.NavigationalWarningsService>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.IViewTime>(),
                sp.GetRequiredService<ViewerSettings>(),
                sp.GetRequiredService<IDatasetLoaderService>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                action => Avalonia.Threading.Dispatcher.UIThread.Post(action),
                sp.GetRequiredService<EcdisDisplayState>());
            drawing.Follow(sp.GetRequiredService<PickReportViewModel>());
            sp.GetRequiredService<NavigationalWarningsViewModel>().Drawing = drawing;
            return drawing;
        });

        // Activity-tab registry. Adding a new tab is a single AddActivityTab
        // line plus the VM registration above and a View under Views/ — no
        // edits to MainWindow.axaml. Ids match the legacy ActivityKind enum
        // names so existing ViewerSettings.LastSelectedActivity values
        // round-trip unchanged.
        // Activity tabs are ordered by importance via IActivityTab.Order
        // (ascending top-to-bottom). Data sources come first, then the
        // display / interaction tools, then the live overlays, then the
        // reference catalogues; Settings is pinned to the bottom
        // (order >= 1000). Tabs that can be hidden (Vessels, Helm) are
        // never persisted as last-selected so they can't be restored
        // while hidden.
        services.AddActivityTab<DatasetsViewModel, DatasetsView>(
            id: "Datasets",
            order: 10,
            title: Strings.Pane_Datasets,
            name: Strings.Label_Activity_Datasets,
            tooltip: Strings.Tooltip_Datasets,
            iconFactory: static () => new FluentIcon { Icon = Icon.Layer, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<LibraryPanelViewModel, LibraryPanelView>(
            id: LibraryImportCoordinator.LibraryPanelId,
            order: 20,
            title: Strings.Pane_Library,
            name: Strings.Label_Activity_Library,
            tooltip: Strings.Tooltip_Library,
            iconFactory: static () => new FluentIcon { Icon = Icon.Library, IconVariant = IconVariant.Regular, FontSize = 22 });
        // Navigational warnings (#932): every loaded S-124 warning, including
        // the ones with no location, which only this list can reach.
        services.AddActivityTab<NavigationalWarningsViewModel, NavigationalWarningsView>(
            id: "NavigationalWarnings",
            order: 25,
            title: Strings.Pane_NavigationalWarnings,
            name: Strings.Label_Activity_NavigationalWarnings,
            tooltip: Strings.Tooltip_NavigationalWarnings,
            iconFactory: static () => new FluentIcon { Icon = Icon.Warning, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<EcdisDisplayPanelViewModel, EcdisDisplayPanelView>(
            id: "EcdisDisplay",
            order: 30,
            title: Strings.Pane_EcdisDisplay,
            name: Strings.Label_Activity_EcdisDisplay,
            tooltip: Strings.Tooltip_EcdisDisplay,
            iconFactory: static () => new FluentIcon { Icon = Icon.Eye, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<LayerStackViewModel, LayerStackView>(
            id: "LayerStack",
            order: 40,
            title: Strings.Pane_LayerStack,
            name: Strings.Label_Activity_LayerStack,
            tooltip: Strings.Tooltip_LayerStack,
            iconFactory: static () => new FluentIcon { Icon = Icon.Stack, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<FeatureSearchViewModel, FeatureSearchView>(
            id: "Search",
            order: 50,
            title: Strings.Pane_Search,
            name: Strings.Label_Activity_Search,
            tooltip: Strings.Tooltip_Search,
            iconFactory: static () => new FluentIcon { Icon = Icon.Search, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<RoutesPanelViewModel, RoutesView>(
            id: "Routes",
            order: 55,
            title: Strings.Pane_Routes,
            name: Strings.Label_Activity_Routes,
            tooltip: Strings.Tooltip_RoutesPanel,
            iconFactory: static () => new FluentIcon { Icon = Icon.Flow, IconVariant = IconVariant.Regular, FontSize = 22 },
            persistAsLastSelected: false);
        // Vessels tab — shown only while the AIS overlay is enabled
        // (its visibility source bridges SettingsViewModel.AisEnabled).
        services.AddActivityTab<VesselListViewModel, VesselListView>(
            id: "Vessels",
            order: 60,
            title: Strings.Pane_Vessels,
            name: Strings.Label_Activity_Vessels,
            tooltip: Strings.Tooltip_Vessels,
            iconFactory: static () => new FluentIcon { Icon = Icon.VehicleShip, IconVariant = IconVariant.Regular, FontSize = 22 },
            persistAsLastSelected: false,
            visibilitySourceFactory: static sp =>
                new EncDotNet.S100.Viewer.ViewModels.Activities.AisOverlayVisibilitySource(
                    sp.GetRequiredService<SettingsViewModel>()));
        // Helm tab — shown only while own-vessel tracking is enabled
        // (its visibility source bridges SettingsViewModel.OwnShipOverlayEnabled).
        services.AddActivityTab<HelmViewModel, HelmView>(
            id: "Helm",
            order: 70,
            title: Strings.Pane_Helm,
            name: Strings.Label_Activity_Helm,
            tooltip: Strings.Tooltip_Helm,
            iconFactory: static () => new FluentIcon { Icon = Icon.TopSpeed, IconVariant = IconVariant.Regular, FontSize = 22 },
            persistAsLastSelected: false,
            visibilitySourceFactory: static sp =>
                new EncDotNet.S100.Viewer.ViewModels.Activities.OwnShipTrackingVisibilitySource(
                    sp.GetRequiredService<SettingsViewModel>()));
        services.AddActivityTab<FeatureCataloguesViewModel, FeatureCataloguesView>(
            id: "FeatureCatalogues",
            order: 80,
            title: Strings.Pane_FeatureCatalogues,
            name: Strings.Label_Activity_FeatureCatalogues,
            tooltip: Strings.Tooltip_FeatureCatalogues,
            iconFactory: static () => new FluentIcon { Icon = Icon.BookOpen, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<PortrayalCataloguesViewModel, PortrayalCataloguesView>(
            id: "PortrayalCatalogues",
            order: 90,
            title: Strings.Pane_PortrayalCatalogues,
            name: Strings.Label_Activity_PortrayalCatalogues,
            tooltip: Strings.Tooltip_PortrayalCatalogues,
            iconFactory: static () => new FluentIcon { Icon = Icon.PaintBrush, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<SettingsViewModel, SettingsView>(
            id: "Settings",
            order: 1000,
            title: Strings.Pane_Settings,
            name: Strings.Label_Activity_Settings,
            tooltip: Strings.Tooltip_Settings,
            iconFactory: static () => new FluentIcon { Icon = Icon.Settings, IconVariant = IconVariant.Regular, FontSize = 22 },
            persistAsLastSelected: false,
            fillsContent: true);

        // PR-M4: Pick Report lives in the right dock; auto-opens when a
        // feature is picked. No switcher UI; chrome bar in MainWindow
        // owns the close button. Title reuses the existing pane string.
        services.AddActivityTab<PickReportViewModel, PickReportView>(
            id: "PickReport",
            order: 10,
            title: Strings.Pick_PanelTitle,
            name: Strings.Label_Activity_PickReport,
            tooltip: Strings.Pick_PanelTitle,
            iconFactory: static () => new FluentIcon { Icon = Icon.Cursor, IconVariant = IconVariant.Regular, FontSize = 22 },
            persistAsLastSelected: false,
            dock: TabDock.Right,
            autoOpenOnContentSignal: true);

        // PR-M4: Timeline lives in the bottom dock; auto-opens when a
        // time-aware dataset is loaded.
        services.AddActivityTab<TimelineViewModel, TimelineView>(
            id: "Timeline",
            order: 10,
            title: Strings.TimelinePanel_Title,
            name: Strings.Label_Activity_Timeline,
            tooltip: Strings.TimelinePanel_Title,
            iconFactory: static () => new FluentIcon { Icon = Icon.Clock, IconVariant = IconVariant.Regular, FontSize = 22 },
            persistAsLastSelected: false,
            dock: TabDock.Bottom,
            // The Time HUD shows time-aware data on the map; the dock opens on request (#712 F5).
            autoOpenOnContentSignal: false);

        // Clearance (#902): the profile along the checked route, next to the
        // Timeline in the bottom dock while Check passage is on.
        services.AddSingleton(sp => new ClearanceViewModel(
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService>(),
            sp.GetService<IMarinerSettingsProvider>(),
            sp.GetService<ITimeFormatProvider>(),
            sp.GetService<IThemeService>()));
        services.AddActivityTab<ClearanceViewModel, ClearanceView>(
            id: MainViewModel.ClearanceTabId,
            order: 20,
            title: Strings.Clearance_Title,
            name: Strings.Label_Activity_Clearance,
            tooltip: Strings.Clearance_Title,
            iconFactory: static () => new FluentIcon { Icon = Icon.ChartMultiple, IconVariant = IconVariant.Regular, FontSize = 22 },
            persistAsLastSelected: false,
            dock: TabDock.Bottom,
            visibilitySourceFactory: static sp =>
                new EncDotNet.S100.Viewer.ViewModels.Activities.CheckModeVisibilitySource(
                    sp.GetRequiredService<EncDotNet.S100.Viewer.Services.RoutesService>()));

        // Logs (#1080): the recent entries of the always-on log buffer, next to
        // the Timeline and Clearance. Always visible; it never opens by itself.
        services.AddSingleton(sp => new LogsViewModel(
            sp.GetRequiredService<EncDotNet.S100.Viewer.Diagnostics.LogBuffer>(),
            showFeedback: () => sp.GetRequiredService<MainViewModel>().ShowFeedbackCommand.Execute(null),
            timeFormat: sp.GetService<ITimeFormatProvider>(),
            time: sp.GetRequiredService<TimeProvider>()));
        services.AddActivityTab<LogsViewModel, LogsView>(
            id: MainViewModel.LogsTabId,
            order: 30,
            title: Strings.Pane_Logs,
            name: Strings.Label_Activity_Logs,
            tooltip: Strings.Tooltip_Logs,
            iconFactory: static () => new FluentIcon { Icon = Icon.TextBulletListSquare, IconVariant = IconVariant.Regular, FontSize = 22 },
            persistAsLastSelected: false,
            dock: TabDock.Bottom);

        // Main window — receives only the StartupOptions plus the small set
        // of cross-cutting services it still owns directly. Per-dataset
        // orchestration lives in IDatasetLoaderService / IPickService.
        services.AddSingleton<MainWindow>(sp => new MainWindow(
            StartupOptions,
            sp.GetRequiredService<MainViewModel>(),
            sp.GetRequiredService<ILibraryImporter>(),
            sp.GetRequiredService<IRecentFilesService>(),
            sp.GetRequiredService<IDatasetLoaderService>(),
            sp.GetRequiredService<IPickService>(),
            sp.GetRequiredService<IFileDialogService>(),
            sp.GetRequiredService<IExchangeSetService>(),
            sp.GetRequiredService<IUpdateNotificationCoordinator>()));

        return services.BuildServiceProvider();
    }

    private static void LogCrash(string label, string message)
    {
        Console.Error.WriteLine($"[{label}] {message}");
        CrashLog.Append(label, message);
        _lastErrorTracker?.Record(label, message);
    }

    /// <summary>
    /// Configures the unclean-shutdown sentinel for this run, then begins
    /// a session. When a previous run's marker survived (an unclean
    /// termination), captures every detected crash — with a tail of the
    /// crash log — into the dedicated <see cref="Diagnostics.ICrashHistory"/>
    /// so the feedback report always carries it, and stashes the list on
    /// <see cref="PreviousUncleanShutdowns"/> for the main window to report.
    /// </summary>
    /// <remarks>
    /// Crashes are deliberately routed to the sticky crash history rather
    /// than the single-slot <see cref="Diagnostics.ILastErrorTracker"/>: a
    /// crash is a stronger signal than an ordinary runtime exception and must
    /// not be evicted by a later, non-fatal error before the user sends
    /// feedback.
    /// </remarks>
    private static void DetectPreviousUncleanShutdown()
    {
        var options = StartupOptions;

        // Skip ephemeral automation runs: they must leave no marker behind
        // and must not surface a stale crash.
        var enabled = !(options?.Ephemeral == true);

        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        // Route crash markers under the active data directory so an
        // isolated --data-dir instance keeps them self-contained.
        var markersDir = _services?.GetService<ViewerDataPaths>()?.CrashMarkersDirectory;
        EncDotNet.S100.Viewer.Diagnostics.UncleanShutdownSentinel.Configure(enabled, markersDir);
        var crashed = EncDotNet.S100.Viewer.Diagnostics.UncleanShutdownSentinel.BeginSession(
            version, CaptureCrashes);
        if (crashed.Count == 0)
            return;

        PreviousUncleanShutdowns = crashed;
    }

    /// <summary>
    /// Captures every detected crash (with a tail of the crash log and of
    /// the most recent crashed session's rolling log) into the sticky crash
    /// history so the feedback report always includes it, independent of
    /// any runtime errors recorded later this session.
    /// </summary>
    /// <remarks>
    /// Runs before the sentinel removes the crash markers: while they exist,
    /// another running viewer's log cleanup keeps the crashed session's
    /// newest log file, so the tail read here can't have been pruned.
    /// </remarks>
    private static void CaptureCrashes(
        IReadOnlyList<EncDotNet.S100.Viewer.Diagnostics.PreviousSession> crashed)
    {
        var tail = CrashLog.ReadTail(8000);
        var mostRecent = crashed[^1];
        var sessionLogTail = _services?.GetService<ViewerDataPaths>() is { } paths
            ? EncDotNet.S100.Viewer.Diagnostics.RollingFileLoggerProvider.ReadSessionTail(
                paths.LogsDirectory, mostRecent.Pid, mostRecent.StartedUtc, 16000)
            : null;
        _services?.GetService<EncDotNet.S100.Viewer.Diagnostics.ICrashHistory>()
            ?.Capture(crashed, tail, sessionLogTail);
    }
}
