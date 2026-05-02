using System.ComponentModel;
using Avalonia;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Viewer;

internal sealed class ViewerCommandSettings : CommandSettings
{
    [CommandOption("--screenshot <PATH>")]
    [Description("Capture a screenshot of the map to the specified file path after loading.")]
    public string? ScreenshotPath { get; set; }

    [CommandOption("-f|--fc <PATH>")]
    [Description("Feature catalogue XML file path. The product spec is detected automatically. May be specified multiple times.")]
    public string[]? FeatureCatalogues { get; set; }

    [CommandOption("-p|--pc <PATH>")]
    [Description("Portrayal catalogue folder path. The product spec is detected automatically. May be specified multiple times.")]
    public string[]? PortrayalCatalogues { get; set; }

    [CommandArgument(0, "[datasets]")]
    [Description("One or more dataset file paths to open.")]
    public string[]? Datasets { get; set; }
}

internal sealed class ViewerCommand : Command<ViewerCommandSettings>
{
    public override int Execute(CommandContext context, ViewerCommandSettings settings)
    {
        App.StartupOptions = settings;

        try
        {
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime([]);
        }
        catch (System.Exception ex)
        {
            System.Console.Error.WriteLine($"[FATAL] {ex}");
            try { System.IO.File.AppendAllText("/tmp/viewer-crash.log", $"{System.DateTime.Now:O}\n{ex}\n\n"); }
            catch { }
            throw;
        }

        return 0;
    }
}
