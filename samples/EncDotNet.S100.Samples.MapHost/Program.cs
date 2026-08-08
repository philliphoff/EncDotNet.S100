using Avalonia;

namespace EncDotNet.S100.Samples.MapHost;

/// <summary>
/// Desktop entry point for the sample host. Nothing here is S-100-specific; it
/// is the standard Avalonia bootstrap. The S-100 wiring lives in
/// <see cref="MainWindow"/>.
/// </summary>
internal sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless CI-runnable check of the reusable session, no window.
        if (args.Contains("--smoke"))
        {
            return SmokeTest.RunAsync().GetAwaiter().GetResult();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration; also used by the design-time previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
