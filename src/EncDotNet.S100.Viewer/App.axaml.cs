using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using EncDotNet.S100.Viewer.Views;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer;

public partial class App : Application
{
    internal static ViewerCommandSettings? StartupOptions { get; set; }

    private static IServiceProvider? s_services;

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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = s_services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // OpenTelemetry tracing/metrics/logging — opt-in via OTEL_* env vars.
        services.AddS100Observability();

        // Persisted user settings
        services.AddSingleton<ViewerSettings>(_ => ViewerSettings.Load());

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
        services.AddSingleton<EncDotNet.S100.Datasets.Pipelines.DatasetPipelineFactory>(sp =>
            new EncDotNet.S100.Datasets.Pipelines.DatasetPipelineFactory(
                sp.GetRequiredService<PortrayalCatalogueManager>(),
                new EncDotNet.S100.Scripting.MoonSharp.MoonSharpLuaEngine(),
                new EncDotNet.S100.Renderers.Mapsui.ProjNetCrsTransformFactory(),
                sp.GetRequiredService<EncDotNet.S100.Features.FeatureCatalogueManager>(),
                sp.GetRequiredService<EncDotNet.S100.Datasets.Pipelines.Interoperability.IInteroperabilityAuthorityProvider>()));

        // Leaf services extracted in phase 2
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IRecentFilesService, RecentFilesService>();
        services.AddSingleton<PortrayalCatalogueSeeder>();
        services.AddSingleton<ScreenshotService>();
        services.AddSingleton<EncDotNet.S100.Viewer.Tools.IMeasureOverlayAppearanceProvider, MeasureOverlayAppearanceProvider>();

        // Phase 3 services: dataset orchestration, pick dispatch, file dialogs
        services.AddSingleton<GlobalTimeService>();
        services.AddSingleton<EcdisDisplayState>(sp =>
        {
            var settings = sp.GetRequiredService<ViewerSettings>();
            var state = new EcdisDisplayState();
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
            state.Hydrate(category, hidden, hiddenPlanes.Count > 0 ? hiddenPlanes : null);

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
                try { settings.Save(); } catch { /* best-effort */ }
            };
            return state;
        });
        services.AddSingleton<IStatusPresenter, StatusPresenter>();
        services.AddSingleton<ShadUI.ToastManager>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IDatasetLoaderService, DatasetLoaderService>();
        services.AddSingleton<IPickService, PickService>();
        services.AddSingleton<IFeatureSearchService, FeatureSearchService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IExchangeSetService, ExchangeSetService>();

        // Own-ship dynamic source (PR-D2). Synthetic driver scaffolds
        // a moving point at Solent (50.8°N 1.3°W) tracking due east
        // at 5 m/s (~9.7 kn); a future real-GPS / NMEA-replay driver
        // implements IOwnShipPositionProvider instead. The source is
        // also exposed as IDynamicFeatureSource so the overlay host
        // discovers it via GetServices&lt;IDynamicFeatureSource&gt;().
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipPositionProvider>(_ =>
            new EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.SyntheticOwnShipPositionProvider(
                start: new EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipPosition(
                    Latitude: 50.8,
                    Longitude: -1.3,
                    CourseOverGroundDeg: 90.0,
                    SpeedOverGroundMs: 5.0,
                    Timestamp: DateTimeOffset.UtcNow),
                cadence: TimeSpan.FromSeconds(1)));
        services.AddSingleton<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipSource>();
        services.AddSingleton<EncDotNet.S100.DynamicSources.IDynamicFeatureSource>(sp =>
            sp.GetRequiredService<EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.OwnShipSource>());

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
        services.AddSingleton<McpServerHost>(sp => new McpServerHost(
            sp.GetRequiredService<ViewerDatasetCatalog>(),
            sp.GetRequiredService<ViewerSettings>(),
            sp.GetRequiredService<IMapHostAccessor>(),
            sp.GetService<ILoggerFactory>()));

        // View models
        services.AddSingleton<FeatureCataloguesViewModel>();
        services.AddSingleton<PortrayalCataloguesViewModel>();
        services.AddSingleton<DatasetsViewModel>();
        services.AddSingleton<CatalogPanelViewModel>();
        services.AddSingleton<LayerStackViewModel>();
        services.AddSingleton<FeatureSearchViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<IMarinerSettingsProvider, MarinerSettingsProvider>();
        services.AddSingleton<ITimeFormatProvider, TimeFormatProvider>();
        services.AddSingleton<PickReportViewModel>();
        services.AddSingleton<TimelineViewModel>();
        services.AddSingleton<DisplayToolbarViewModel>();
        services.AddSingleton<TextGroupToolbarViewModel>();
        services.AddSingleton<EcdisLabelOverrideProvider>();
        services.AddSingleton<EcdisDisplayPanelViewModel>();
        services.AddSingleton<MainViewModel>();

        // Activity-tab registry. Adding a new tab is a single AddActivityTab
        // line plus the VM registration above and a View under Views/ — no
        // edits to MainWindow.axaml. Ids match the legacy ActivityKind enum
        // names so existing ViewerSettings.LastSelectedActivity values
        // round-trip unchanged.
        services.AddActivityTab<FeatureCataloguesViewModel, FeatureCataloguesView>(
            id: "FeatureCatalogues",
            order: 10,
            title: Strings.Pane_FeatureCatalogues,
            tooltip: Strings.Tooltip_FeatureCatalogues,
            iconFactory: static () => new FluentIcon { Icon = Icon.BookOpen, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<PortrayalCataloguesViewModel, PortrayalCataloguesView>(
            id: "PortrayalCatalogues",
            order: 20,
            title: Strings.Pane_PortrayalCatalogues,
            tooltip: Strings.Tooltip_PortrayalCatalogues,
            iconFactory: static () => new FluentIcon { Icon = Icon.PaintBrush, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<DatasetsViewModel, DatasetsView>(
            id: "Datasets",
            order: 30,
            title: Strings.Pane_Datasets,
            tooltip: Strings.Tooltip_Datasets,
            iconFactory: static () => new FluentIcon { Icon = Icon.Layer, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<CatalogPanelViewModel, CatalogPanelView>(
            id: "Catalog",
            order: 40,
            title: Strings.Pane_Catalog,
            tooltip: Strings.Tooltip_Catalog,
            iconFactory: static () => new FluentIcon { Icon = Icon.Library, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<LayerStackViewModel, LayerStackView>(
            id: "LayerStack",
            order: 50,
            title: Strings.Pane_LayerStack,
            tooltip: Strings.Tooltip_LayerStack,
            iconFactory: static () => new FluentIcon { Icon = Icon.Stack, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<FeatureSearchViewModel, FeatureSearchView>(
            id: "Search",
            order: 60,
            title: Strings.Pane_Search,
            tooltip: Strings.Tooltip_Search,
            iconFactory: static () => new FluentIcon { Icon = Icon.Search, IconVariant = IconVariant.Regular, FontSize = 22 });
        services.AddActivityTab<EcdisDisplayPanelViewModel, EcdisDisplayPanelView>(
            id: "EcdisDisplay",
            order: 70,
            title: Strings.Pane_EcdisDisplay,
            tooltip: Strings.Tooltip_EcdisDisplay,
            iconFactory: static () => new FluentIcon { Icon = Icon.Eye, IconVariant = IconVariant.Regular, FontSize = 22 });
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
        var line = $"[{label}] {message}";
        Console.Error.WriteLine(line);
        try { System.IO.File.AppendAllText("/tmp/viewer-crash.log", $"{DateTime.Now:O} {line}\n\n"); }
        catch { }
    }
}
