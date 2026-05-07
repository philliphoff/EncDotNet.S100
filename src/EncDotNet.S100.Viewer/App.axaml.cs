using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
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
            state.Hydrate(category, hidden);

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
                try { settings.Save(); } catch { /* best-effort */ }
            };
            return state;
        });
        services.AddSingleton<IStatusPresenter, StatusPresenter>();
        services.AddSingleton<IDatasetLoaderService, DatasetLoaderService>();
        services.AddSingleton<IPickService, PickService>();
        services.AddSingleton<IFeatureSearchService, FeatureSearchService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();

        // View models
        services.AddSingleton<FeatureCataloguesViewModel>();
        services.AddSingleton<PortrayalCataloguesViewModel>();
        services.AddSingleton<DatasetsViewModel>();
        services.AddSingleton<CatalogPanelViewModel>();
        services.AddSingleton<FeatureSearchViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<PickReportViewModel>();
        services.AddSingleton<TimelineViewModel>();
        services.AddSingleton<MainViewModel>();

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
            sp.GetRequiredService<IFileDialogService>()));

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
