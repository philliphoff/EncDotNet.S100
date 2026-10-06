using Avalonia;
using Avalonia.Headless;
using EncDotNet.S100.Viewer.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// Avalonia headless uses a process-global session that is not parallel-safe; serializing
// the assembly avoids dispatcher/thread-pool contention that destabilizes timing-sensitive CI tests.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Headless Avalonia application for <c>[AvaloniaFact]</c> tests. Avalonia 12's
/// dispatcher rework means <see cref="Avalonia.Threading.Dispatcher.UIThread"/>
/// is only marshaled (and pumped) on a real dispatcher thread, so view-model
/// tests that exercise the <c>Dispatcher.UIThread</c> path, and view tests,
/// run under the headless platform.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<HeadlessViewerApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            // As the app does (Program.BuildAvaloniaApp): the same embedded font on
            // every OS, so text lays out and renders alike in CI and locally.
            .WithInterFont();
}

/// <summary>
/// The viewer's real <see cref="App"/> with only its XAML loaded: every style,
/// theme (ShadUI plus the S-100 Day/Dusk/Night chrome variants) and resource in
/// <c>App.axaml</c>, so a view under test looks up the same templates and brushes
/// as in the app.
/// </summary>
/// <remarks>
/// <see cref="App.OnFrameworkInitializationCompleted"/> is skipped on purpose: it
/// builds the full service container, starts the MCP host and reads and writes
/// crash markers in the user's data directory. Tests compose the view models
/// they need themselves. Developer tools are not attached either: the session
/// builds an app per test, and a second attach throws, which stops the
/// session's dispatch loop and hangs every later test in Debug builds.
/// </remarks>
public sealed class HeadlessViewerApp : App
{
    protected override bool AttachesDeveloperTools => false;

    public override void OnFrameworkInitializationCompleted()
    {
    }
}
