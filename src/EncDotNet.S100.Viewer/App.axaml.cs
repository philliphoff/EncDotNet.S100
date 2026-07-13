using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
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

    private static IServiceProvider? s_services;

    /// <summary>
    /// Records the most recent unhandled error so the feedback reporter
    /// can include it. Assigned once the service container is built; the
    /// global exception handlers funnel through <see cref="LogCrash"/>
    /// which forwards here.
    /// </summary>
    private static EncDotNet.S100.Viewer.Diagnostics.ILastErrorTracker? s_lastErrorTracker;

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
        s_services ?? throw new InvalidOperationException(
            "Service provider has not been initialized yet.");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ConfigureMacApplicationMenu();
    }

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

        s_services = ConfigureServices();

        // Now that the container exists, route recorded crashes into the
        // feedback reporter's last-error tracker (the global handlers above
        // funnel through LogCrash).
        s_lastErrorTracker = s_services.GetRequiredService<
            EncDotNet.S100.Viewer.Diagnostics.ILastErrorTracker>();

        // Re-root the warm tile disk cache under the active data directory
        // when one is in use (--data-dir / S100_DATA_DIR), so an isolated
        // instance keeps its tile cache self-contained. An explicit
        // S100_VECTOR_TILE_DISK_DIR env var still wins (the setter is a
        // no-op when env-pinned). Must run before the tile renderer creates
        // its shared disk cache below.
        if (s_services.GetRequiredService<ViewerDataPaths>().TileDiskCacheDirectory is { } tileDir)
        {
            EncDotNet.S100.Renderers.Mapsui.RenderingOptimizations.TileDiskDirectory = tileDir;
        }

        // Detect an unclean shutdown from the previous run (native crash,
        // FailFast, kill, … — none of which the managed handlers above can
        // catch) and route it into the last-error tracker so the feedback
        // reporter and the startup notification can surface it. Disabled
        // for ephemeral / one-shot screenshot runs so automation never
        // writes a marker or reports a stale one.
        DetectPreviousUncleanShutdown();

        // ShadUI resolves custom dialog content by an explicit
        // view/context-view-model registration on the DialogManager (a
        // DataTemplate alone is not sufficient). Register the feedback
        // dialog now that the singleton manager exists.
        s_services.GetRequiredService<ShadUI.DialogManager>()
            .Register<Views.FeedbackDialogView, ViewModels.FeedbackDialogViewModel>();
        s_services.GetRequiredService<ShadUI.DialogManager>()
            .Register<Views.AboutDialogView, ViewModels.AboutDialogViewModel>();

        // Interpose the translation-invariant vector path cache (solid
        // polygons + solid-stroked, resolution-simplified lines) before
        // instrumentation wraps the renderer dictionary, so the cache sits
        // inside the counting wrapper and pans reuse projected paths instead
        // of rebuilding and re-stroking them every frame.
        EncDotNet.S100.Renderers.Mapsui.CachedVectorStyleRenderer.Register();

        // Register the picture-snapshot custom layer renderer (registration is
        // unconditional; the fast path is gated live by
        // RenderingOptimizations.VectorSnapshotEnabled, default on, bound by the
        // Settings → Map section). It resolves Mapsui's style renderers by
        // reflection, so it must register after the cached vector renderer is in
        // place; it reads the renderer dictionary lazily on first paint, by which
        // time instrumentation (below) has also wrapped it.
        EncDotNet.S100.Renderers.Mapsui.S100VectorSnapshotRenderer.Register();

        // Register the TiledScene ("B") custom layer renderer too, so a layer
        // tagged for it portrays when that subsystem is the active
        // RenderingOptimizations.RenderSubsystem. Idempotent; the takeover is
        // gated by the flag at layer-build time, not by registration.
        EncDotNet.S100.Renderers.Mapsui.S100VectorSceneRenderer.Register();
        EncDotNet.S100.Renderers.Mapsui.S100VectorTileRenderer.Register();

        EncDotNet.S100.Viewer.Diagnostics.MapPaintInstrumentation.Install();

        // The viewer uses a plain ServiceCollection (no generic IHost),
        // so the IHostedService registered by AddOpenTelemetry() never
        // runs — meaning the TracerProvider / MeterProvider would
        // otherwise stay un-built and no ActivityListener / MeterListener
        // would ever subscribe. Resolving them here forces construction
        // and wires up the OTel pipeline before any instrumented code
        // runs (dataset open, pipeline process, render, etc.).
        _ = s_services.GetService(typeof(OpenTelemetry.Trace.TracerProvider));
        _ = s_services.GetService(typeof(OpenTelemetry.Metrics.MeterProvider));

        // Hook the logger factory into the static BeginCommand path so
        // each viewer command also emits a structured log entry.
        ViewerObservability.AttachLoggerFactory(
            s_services.GetRequiredService<ILoggerFactory>());

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
            var logger = s_services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("EncDotNet.S100.Viewer");
            logger.LogInformation(
                "EncDotNet.S100.Viewer started (version {Version}).",
                typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        }

        // Wire the S-128 catalog source into the aggregator. Done here (and
        // not in MainWindow) so the registration is independent of the view.
        s_services.GetRequiredService<DatasetCatalogAggregator>()
            .Add(s_services.GetRequiredService<S128DatasetCatalogSource>());

        // Start (or leave disabled) the MCP server based on persisted settings.
        // Failures are logged but never block app startup.
        var mcpHost = s_services.GetRequiredService<McpServerHost>();
        var settingsVm = s_services.GetRequiredService<SettingsViewModel>();
        settingsVm.McpSettingsChanged += () =>
        {
            _ = mcpHost.Apply().ContinueWith(t =>
            {
                if (t.Exception is not null)
                    LogCrash("McpServerHost", t.Exception.GetBaseException().ToString());
            }, TaskScheduler.Default);
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
        var themeService = s_services.GetRequiredService<IThemeService>();
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
        var ownShipGeom = s_services.GetService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.SettingsOwnShipVesselGeometryProvider>();
        if (ownShipGeom is not null)
        {
            settingsVm.OwnShipGeometryChanged += () => ownShipGeom.NotifyChanged();
        }

        // Toggle the simulated own-ship overlay live from Settings. The
        // source's IsEnabled gate empties (or republishes) the feature,
        // so flipping the checkbox shows/hides the glyph without a
        // restart.
        var ownShipSource = s_services.GetService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipSource>();
        if (ownShipSource is not null)
        {
            settingsVm.OwnShipOverlayEnabledChanged += enabled => ownShipSource.IsEnabled = enabled;
        }

        // Pirate mode: engage when the user picks "Take the helm of this
        // vessel" on an AIS hit in the pick report. The coordinator opens
        // the visibility gates, persists the source selection, and starts
        // the controller following. Routing the overlay-enable through
        // settingsVm keeps the checkbox + persisted flag in sync.
        var pirateController = s_services.GetService<EncDotNet.S100.Viewer.Services.DynamicSources.PirateModeController>();
        var pickReport = s_services.GetService<PickReportViewModel>();
        if (pirateController is not null)
        {
            var pirateCoordinator = new EncDotNet.S100.Viewer.Services.DynamicSources.PirateModeCoordinator(
                pirateController,
                s_services.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.IDynamicFeatureSourceRegistry>(),
                s_services.GetRequiredService<ViewerSettings>(),
                enabled => settingsVm.OwnShipOverlayEnabled = enabled);

            if (pickReport is not null)
            {
                pickReport.TakeHelmRequested += (_, mmsi) => pirateCoordinator.Engage(mmsi);
            }

            // Take/release the helm from the Vessels panel. Engage hides the
            // followed target, so after engaging refresh the list and select
            // the own-ship row (HandleHelmEngaged) to keep the Release button
            // reachable; disengage just refreshes the label/command state.
            var vesselList = s_services.GetService<VesselListViewModel>();
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
                s_services.GetRequiredService<EncDotNet.S100.Viewer.Services.RoutePersistenceService>();
            routePersistence.Initialize();

            desktop.MainWindow = s_services.GetRequiredService<MainWindow>();

            // Drain the tiled renderer's background Skia workers before the
            // process tears down. Avalonia raises ShutdownRequested on every
            // exit path (explicit Shutdown(), last-window-close, OS quit), so
            // hooking it here covers --exit-after-screenshot and normal quit
            // alike. Without this, the managed runtime can begin destroying
            // libSkiaSharp while a worker is mid-rasterise → native SIGSEGV.
            desktop.ShutdownRequested += (_, _) =>
            {
                // Flush any pending debounced route save so the last edit is
                // not lost to the debounce window.
                routePersistence.Flush();
                EncDotNet.S100.Renderers.Mapsui.S100VectorTileRenderer.ShutdownAndDrain(TimeSpan.FromSeconds(5));
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // OpenTelemetry tracing/metrics/logging — opt-in via OTEL_* env vars.
        // CLI --log-file / --verbose add a file sink and lower the log
        // floor for agent runs.
        services.AddS100Observability(
            logFilePath: StartupOptions?.LogFile,
            verbose: StartupOptions?.Verbose ?? false);

        // Resolved on-disk locations for this run (settings + caches),
        // honouring --data-dir / S100_DATA_DIR (and --settings for the
        // settings file). A single shared instance drives the settings
        // file, the disk caches, and the crash-marker directory.
        services.AddSingleton<ViewerDataPaths>(_ => ViewerDataPaths.Resolve(StartupOptions));

        // Persisted user settings, with any command-line overrides
        // (settings path / --ephemeral / MCP / palette / display
        // category) layered on top for this run only.
        services.AddSingleton<ViewerSettings>(sp =>
            StartupSettingsFactory.Create(StartupOptions, sp.GetRequiredService<ViewerDataPaths>()));

        // Shared application-level state
        services.AddSingleton<PortrayalCatalogueManager>();
        services.AddSingleton<DatasetCatalogAggregator>();
        services.AddSingleton<S128DatasetCatalogSource>();
        services.AddSingleton<IDatasetCatalogSource>(
            sp => sp.GetRequiredService<DatasetCatalogAggregator>());

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
                new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthority()));
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
            // One process-wide disk cache shared by every S-101 processor so a
            // fresh open of a previously-portrayed cell skips the multi-second
            // MoonSharp Part 9A Lua run, even across restarts. The cache key is
            // the portrayal-content hash (dataset bytes + FC/PC content +
            // pipeline / VM assemblies) so persisted entries auto-invalidate
            // when anything affecting the instruction list changes.
            var cacheDir = sp.GetRequiredService<ViewerDataPaths>().PortrayalInstructionCacheDirectory;
            const long maxBytes = 256L * 1024 * 1024;
            return new EncDotNet.S100.Pipelines.Vector.Caching.DiskPortrayalInstructionCache(cacheDir, maxBytes);
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
        services.AddSingleton<ScreenshotService>();
        services.AddSingleton<EncDotNet.S100.Viewer.Tools.IMeasureOverlayAppearanceProvider, MeasureOverlayAppearanceProvider>();
        services.AddSingleton<EncDotNet.S100.Viewer.Services.RoutesService>();
        // Loads persisted routes at startup and writes them back (debounced)
        // on change, so user/agent routes survive a restart. Eagerly
        // initialized in OnFrameworkInitializationCompleted and flushed on
        // ShutdownRequested.
        services.AddSingleton<EncDotNet.S100.Viewer.Services.RoutePersistenceService>();

        // Phase 3 services: dataset orchestration, pick dispatch, file dialogs
        services.AddSingleton<GlobalTimeService>();
        services.AddSingleton<EcdisDisplayState>(sp =>
        {
            var settings = sp.GetRequiredService<ViewerSettings>();
            var state = new EcdisDisplayState();

            // Seed the viewer's default viewing-group visibility once so
            // the noisy S-101 mariner-selector patterns (shallow water
            // pattern, survey accuracy/quality, low-accuracy marker)
            // start off even in the "All" category. Mariner choices made
            // afterwards persist and are never re-forced.
            if (EcdisDisplayDefaults.Apply(settings))
            {
                try { settings.Save(); } catch { /* best-effort */ }
            }

            var category = Enum.TryParse<EncDotNet.S100.Datasets.Pipelines.EcdisDisplayCategory>(
                settings.EcdisDisplayCategory, ignoreCase: true, out var c)
                ? c
                : EncDotNet.S100.Datasets.Pipelines.EcdisDisplayCategory.Standard;
            var hidden = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in settings.EcdisHiddenViewingGroups)
            {
                var ids = new HashSet<int>();
                foreach (var token in (kv.Value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (int.TryParse(token, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id))
                        ids.Add(id);
                }
                if (ids.Count > 0) hidden[kv.Key] = ids;
            }
            // Hydrate hidden display planes
            var hiddenPlanes = new HashSet<EncDotNet.S100.Pipelines.Vector.DisplayPlane>();
            foreach (var token in (settings.EcdisHiddenDisplayPlanes ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<EncDotNet.S100.Pipelines.Vector.DisplayPlane>(token, ignoreCase: true, out var plane))
                    hiddenPlanes.Add(plane);
            }
            // Hydrate explicit per-spec display-mode selections (§11.7).
            var displayModes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in settings.EcdisActiveDisplayModes)
            {
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    displayModes[kv.Key] = kv.Value;
            }
            state.Hydrate(category, hidden, hiddenPlanes.Count > 0 ? hiddenPlanes : null,
                displayModes.Count > 0 ? displayModes : null);

            // Persist on every change so a crash doesn't lose the user's
            // ECDIS preferences. Cheap because settings.json is small.
            state.Changed += () =>
            {
                settings.EcdisDisplayCategory = state.Category.ToString();
                var snap = state.Snapshot();
                settings.EcdisHiddenViewingGroups.Clear();
                foreach (var kv in snap.HiddenViewingGroups)
                {
                    settings.EcdisHiddenViewingGroups[kv.Key] =
                        string.Join(",", kv.Value.OrderBy(i => i));
                }
                settings.EcdisHiddenDisplayPlanes =
                    string.Join(",", snap.HiddenDisplayPlanes.OrderBy(p => p));
                settings.EcdisActiveDisplayModes.Clear();
                foreach (var kv in snap.ActiveDisplayModes)
                {
                    if (!string.IsNullOrEmpty(kv.Value))
                        settings.EcdisActiveDisplayModes[kv.Key] = kv.Value;
                }
                try { settings.Save(); } catch { /* best-effort */ }
            };
            return state;
        });
        services.AddSingleton<IStatusPresenter, StatusPresenter>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton(TimeProvider.System);
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
        services.AddSingleton<EncDotNet.S100.Viewer.Services.Updates.IAppVersionProvider>(
            _ => new EncDotNet.S100.Viewer.Services.Updates.AssemblyAppVersionProvider());
        services.AddSingleton<EncDotNet.S100.Viewer.Services.Updates.IGitHubReleaseClient>(sp =>
            new EncDotNet.S100.Viewer.Services.Updates.GitHubReleaseClient(
                new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) },
                sp.GetService<ILogger<EncDotNet.S100.Viewer.Services.Updates.GitHubReleaseClient>>()));
        services.AddSingleton<EncDotNet.S100.Viewer.Services.Updates.IUpdateService>(sp =>
            new EncDotNet.S100.Viewer.Services.Updates.UpdateService(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.Updates.IGitHubReleaseClient>(),
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.Updates.IAppVersionProvider>(),
                sp.GetRequiredService<ViewerSettings>(),
                sp.GetRequiredService<TimeProvider>()));
        services.AddTransient<AboutDialogViewModel>();
        services.AddSingleton<Func<AboutDialogViewModel>>(sp =>
            sp.GetRequiredService<AboutDialogViewModel>);

        services.AddSingleton<IDatasetLoaderService, DatasetLoaderService>();
        services.AddSingleton<IPickService, PickService>();
        services.AddSingleton<IGeographicPickPresenter>(sp =>
            new DispatcherGeographicPickPresenter(sp.GetRequiredService<IPickService>()));
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.IDynamicSourcePickService>(sp =>
            new EncDotNet.S100.Viewer.Services.DynamicSources.DynamicSourcePickService(
                sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.DynamicFeatureSourceRegistryAccessor>()));
        services.AddSingleton<IFeatureSearchService, FeatureSearchService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IExchangeSetService, ExchangeSetService>();

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
        // under the "ownship" key so DynamicSourceOverlayHost resolves
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
        // is the DynamicSourceOverlayHost constructed in MainWindow
        // (it needs IMapHost, which only exists after the MapControl
        // initialises). The accessor is the indirection: view-models
        // depend on it through IDynamicFeatureSourceRegistry; MainWindow
        // assigns Current once the host is built. Mirrors
        // IMapHostAccessor / MapHostAccessor below.
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.DynamicFeatureSourceRegistryAccessor>();
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.IDynamicFeatureSourceRegistry>(sp =>
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.DynamicFeatureSourceRegistryAccessor>());

        // MCP server (loopback-only, off by default). The catalog adapter
        // observes the existing dataset loader and re-opens dataset files
        // for read-only MCP queries; the host owns server lifecycle.
        services.AddSingleton<ViewerDatasetCatalog>();
        services.AddSingleton<IMapHostAccessor, MapHostAccessor>();
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
            sp.GetRequiredService<IMapHostAccessor>(),
            sp.GetService<ILoggerFactory>(),
            sp.GetRequiredService<IRenderStateControllerAccessor>(),
            sp.GetRequiredService<GlobalTimeService>(),
            sp.GetRequiredService<IRenderActivityMonitor>(),
            sp.GetRequiredService<IDatasetLoadGateway>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipHelm>(),
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.RoutesService>(),
            sp.GetRequiredService<IGeographicPickPresenter>(),
            sp.GetRequiredService<IViewerUiControllerAccessor>(),
            sp.GetRequiredService<IAppScreenshotProvider>()));

        // View models
        services.AddSingleton<FeatureCataloguesViewModel>();
        services.AddSingleton<PortrayalCataloguesViewModel>();
        services.AddSingleton<DatasetsViewModel>();
        services.AddSingleton<CatalogPanelViewModel>();
        services.AddSingleton<LayerStackViewModel>();
        services.AddSingleton<FeatureSearchViewModel>();
        services.AddSingleton<VesselListViewModel>(sp => new VesselListViewModel(
            sp.GetServices<EncDotNet.S100.DynamicSources.IDynamicFeatureSource>(),
            sp.GetRequiredService<IMapHostAccessor>(),
            sp.GetService<EncDotNet.S100.Viewer.Services.DynamicSources.PirateModeController>(),
            sp.GetService<EncDotNet.S100.Viewer.Services.DynamicSources.Ais.ExcludingAisFeatureSource>()?.Inner));
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<IMarinerSettingsProvider, MarinerSettingsProvider>();
        services.AddSingleton<ITimeFormatProvider, TimeFormatProvider>();
        services.AddSingleton<PickReportViewModel>();
        services.AddSingleton<TimelineViewModel>();
        services.AddSingleton<DisplayToolbarViewModel>();
        services.AddSingleton<TextGroupToolbarViewModel>();
        services.AddSingleton<DisplayModeToolbarViewModel>();
        services.AddSingleton<EcdisLabelOverrideProvider>();
        services.AddSingleton<EcdisDisplayPanelViewModel>();
        services.AddSingleton<HelmViewModel>();
        services.AddSingleton<RoutesPanelViewModel>();
        services.AddSingleton<MainViewModel>();

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
            tooltip: Strings.Tooltip_Datasets,
            iconFactory: static () => new FluentIcon { Icon = Icon.Layer, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<CatalogPanelViewModel, CatalogPanelView>(
            id: "Catalog",
            order: 20,
            title: Strings.Pane_Catalog,
            tooltip: Strings.Tooltip_Catalog,
            iconFactory: static () => new FluentIcon { Icon = Icon.Library, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<EcdisDisplayPanelViewModel, EcdisDisplayPanelView>(
            id: "EcdisDisplay",
            order: 30,
            title: Strings.Pane_EcdisDisplay,
            tooltip: Strings.Tooltip_EcdisDisplay,
            iconFactory: static () => new FluentIcon { Icon = Icon.Eye, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<LayerStackViewModel, LayerStackView>(
            id: "LayerStack",
            order: 40,
            title: Strings.Pane_LayerStack,
            tooltip: Strings.Tooltip_LayerStack,
            iconFactory: static () => new FluentIcon { Icon = Icon.Stack, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<FeatureSearchViewModel, FeatureSearchView>(
            id: "Search",
            order: 50,
            title: Strings.Pane_Search,
            tooltip: Strings.Tooltip_Search,
            iconFactory: static () => new FluentIcon { Icon = Icon.Search, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<RoutesPanelViewModel, RoutesView>(
            id: "Routes",
            order: 55,
            title: Strings.Pane_Routes,
            tooltip: Strings.Tooltip_RoutesPanel,
            iconFactory: static () => new FluentIcon { Icon = Icon.Flow, IconVariant = IconVariant.Regular, FontSize = 22 },
            persistAsLastSelected: false);
        // Vessels tab — shown only while the AIS overlay is enabled
        // (its visibility source bridges SettingsViewModel.AisEnabled).
        services.AddActivityTab<VesselListViewModel, VesselListView>(
            id: "Vessels",
            order: 60,
            title: Strings.Pane_Vessels,
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
            tooltip: Strings.Tooltip_FeatureCatalogues,
            iconFactory: static () => new FluentIcon { Icon = Icon.BookOpen, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<PortrayalCataloguesViewModel, PortrayalCataloguesView>(
            id: "PortrayalCatalogues",
            order: 90,
            title: Strings.Pane_PortrayalCatalogues,
            tooltip: Strings.Tooltip_PortrayalCatalogues,
            iconFactory: static () => new FluentIcon { Icon = Icon.PaintBrush, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<SettingsViewModel, SettingsView>(
            id: "Settings",
            order: 1000,
            title: Strings.Pane_Settings,
            tooltip: Strings.Tooltip_Settings,
            iconFactory: static () => new FluentIcon { Icon = Icon.Settings, IconVariant = IconVariant.Regular, FontSize = 22 },
            persistAsLastSelected: false);

        // PR-M4: Pick Report lives in the right dock; auto-opens when a
        // feature is picked. No switcher UI; chrome bar in MainWindow
        // owns the close button. Title reuses the existing pane string.
        services.AddActivityTab<PickReportViewModel, PickReportView>(
            id: "PickReport",
            order: 10,
            title: Strings.Pick_PanelTitle,
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
            tooltip: Strings.TimelinePanel_Title,
            iconFactory: static () => new FluentIcon { Icon = Icon.Clock, IconVariant = IconVariant.Regular, FontSize = 22 },
            persistAsLastSelected: false,
            dock: TabDock.Bottom,
            autoOpenOnContentSignal: true);

        // Main window — receives only the StartupOptions plus the small set
        // of cross-cutting services it still owns directly. Per-dataset
        // orchestration lives in IDatasetLoaderService / IPickService.
        services.AddSingleton<MainWindow>(sp => new MainWindow(
            StartupOptions,
            sp.GetRequiredService<MainViewModel>(),
            sp.GetRequiredService<DatasetCatalogAggregator>(),
            sp.GetRequiredService<IRecentFilesService>(),
            sp.GetRequiredService<ScreenshotService>(),
            sp.GetRequiredService<IDatasetLoaderService>(),
            sp.GetRequiredService<IPickService>(),
            sp.GetRequiredService<IFileDialogService>(),
            sp.GetRequiredService<IExchangeSetService>()));

        return services.BuildServiceProvider();
    }

    private static void LogCrash(string label, string message)
    {
        Console.Error.WriteLine($"[{label}] {message}");
        CrashLog.Append(label, message);
        s_lastErrorTracker?.Record(label, message);
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

        // Skip ephemeral / one-shot screenshot automation runs: they must
        // leave no marker behind and must not surface a stale crash.
        var enabled = !(options?.Ephemeral == true || options?.ExitAfterScreenshot == true);

        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        // Route crash markers under the active data directory so an
        // isolated --data-dir instance keeps them self-contained.
        var markersDir = s_services?.GetService<ViewerDataPaths>()?.CrashMarkersDirectory;
        EncDotNet.S100.Viewer.Diagnostics.UncleanShutdownSentinel.Configure(enabled, markersDir);
        var crashed =
            EncDotNet.S100.Viewer.Diagnostics.UncleanShutdownSentinel.BeginSession(version);
        if (crashed.Count == 0)
            return;

        PreviousUncleanShutdowns = crashed;

        // Capture every detected crash (with a tail of the crash log) into the
        // sticky crash history so the feedback report always includes it,
        // independent of any runtime errors recorded later this session.
        var tail = CrashLog.ReadTail(8000);
        s_services?.GetService<EncDotNet.S100.Viewer.Diagnostics.ICrashHistory>()
            ?.Capture(crashed, tail);
    }
}
