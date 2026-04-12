using Avalonia;
using Spectre.Console.Cli;
using System;

namespace EncDotNet.S100.Viewer;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new CommandApp<ViewerCommand>();
        app.Configure(config => config.SetApplicationName("EncDotNet.S100.Viewer"));
        return app.Run(args);
    }

    // Avalonia design-time preview support
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
