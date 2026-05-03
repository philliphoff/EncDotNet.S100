using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = s_services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

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

        // View models
        services.AddSingleton<FeatureCataloguesViewModel>();
        services.AddSingleton<PortrayalCataloguesViewModel>();
        services.AddSingleton<DatasetsViewModel>();
        services.AddSingleton<CatalogPanelViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<PickReportViewModel>();
        services.AddSingleton<MainViewModel>();

        // Main window — receives StartupOptions (CLI args parsed by Spectre)
        // along with services it still owns directly. Phase 3 will move the
        // dataset-loader and pick-service concerns out next.
        services.AddSingleton<MainWindow>(sp => new MainWindow(
            StartupOptions,
            sp.GetRequiredService<ViewerSettings>(),
            sp.GetRequiredService<PortrayalCatalogueManager>(),
            sp.GetRequiredService<DatasetCatalogAggregator>(),
            sp.GetRequiredService<S128DatasetCatalogSource>(),
            sp.GetRequiredService<MainViewModel>(),
            sp.GetRequiredService<PortrayalCatalogueSeeder>(),
            sp.GetRequiredService<IRecentFilesService>(),
            sp.GetRequiredService<ScreenshotService>()));

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
