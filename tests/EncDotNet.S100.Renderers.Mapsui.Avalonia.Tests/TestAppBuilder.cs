using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(
    typeof(EncDotNet.S100.Renderers.Mapsui.Avalonia.Tests.TestAppBuilder))]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
