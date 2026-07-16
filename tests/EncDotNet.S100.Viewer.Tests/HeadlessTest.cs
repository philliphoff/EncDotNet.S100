using Avalonia.Headless;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Runs a test body on the Avalonia headless dispatcher thread.
/// <para>
/// Avalonia 12 reworked the dispatcher so
/// <see cref="Avalonia.Threading.Dispatcher.UIThread"/> is bound to a single
/// thread and only pumped there. View models that marshal work via
/// <c>Dispatcher.UIThread</c> (e.g. <c>LayerStackViewModel</c> rebuilding in
/// response to events) therefore need a real, pumped dispatcher to observe
/// the result synchronously. This helper dispatches the body onto the
/// headless session's UI thread, where <c>CheckAccess()</c> is <c>true</c>.
/// </para>
/// <para>
/// We use the base <c>Avalonia.Headless</c> session API rather than
/// <c>Avalonia.Headless.XUnit</c>'s <c>[AvaloniaFact]</c> because that
/// package targets xunit v3 while this test suite uses xunit v2.
/// </para>
/// </summary>
internal static class HeadlessTest
{
    public static void Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessTest).Assembly);
        session.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();
    }
}
