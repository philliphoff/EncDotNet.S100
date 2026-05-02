using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace EncDotNet.S100.Viewer;

public partial class App : Application
{
    internal static ViewerCommandSettings? StartupOptions { get; set; }

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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(StartupOptions);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void LogCrash(string label, string message)
    {
        var line = $"[{label}] {message}";
        Console.Error.WriteLine(line);
        try { System.IO.File.AppendAllText("/tmp/viewer-crash.log", $"{DateTime.Now:O} {line}\n\n"); }
        catch { }
    }
}
