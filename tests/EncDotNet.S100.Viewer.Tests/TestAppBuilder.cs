using Avalonia;
using Avalonia.Headless;
using EncDotNet.S100.Viewer.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// Avalonia headless uses a process-global session that is not parallel-safe; serializing
// the assembly avoids dispatcher/thread-pool contention that destabilizes timing-sensitive CI tests.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Minimal headless Avalonia application used by <c>[AvaloniaFact]</c>
/// tests. Avalonia 12's dispatcher rework means
/// <see cref="Avalonia.Threading.Dispatcher.UIThread"/> is only marshaled
/// (and pumped) on a real dispatcher thread; view-model tests that exercise
/// the <c>Dispatcher.UIThread</c> path therefore run under the headless
/// platform so the dispatcher is available and pumped per test.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
