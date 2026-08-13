using Avalonia.Headless;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia.Tests;

internal static class HeadlessTest
{
    // A dispatched body runs on Avalonia's headless UI thread. That thread is a
    // single per-assembly dispatch loop; if any dispatch's Avalonia setup or
    // teardown throws (Avalonia only swallows OperationCanceledException in the
    // loop), the loop thread dies and every later Dispatch returns a task that
    // never completes — a silent, unbounded hang that has stalled CI for hours.
    // Cap the wait so a dead session surfaces as a fast, diagnosable failure
    // instead. Real bodies finish in milliseconds, so the ceiling is only ever
    // hit by a broken session, never by a slow-but-healthy test.
    private static readonly TimeSpan DispatchTimeout = TimeSpan.FromMinutes(2);

    public static void Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessTest).Assembly);
        WaitWithTimeout(session.Dispatch(body, CancellationToken.None));
    }

    public static T Run<T>(Func<T> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var result = default(T);
        Run(() =>
        {
            result = body();
        });
        return result!;
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessTest).Assembly);
        var dispatch = session.Dispatch(body, CancellationToken.None);
        if (await Task.WhenAny(dispatch, Task.Delay(DispatchTimeout)).ConfigureAwait(false) != dispatch)
        {
            throw BrokenSessionTimeout();
        }

        return await dispatch.ConfigureAwait(false);
    }

    private static void WaitWithTimeout(Task dispatch)
    {
        if (!dispatch.Wait(DispatchTimeout))
        {
            throw BrokenSessionTimeout();
        }

        // Re-await the completed task so its exception (if any) propagates
        // unwrapped rather than as an AggregateException from Wait.
        dispatch.GetAwaiter().GetResult();
    }

    private static TimeoutException BrokenSessionTimeout() =>
        new($"The Avalonia headless dispatch did not complete within {DispatchTimeout.TotalSeconds:0}s. " +
            "The per-assembly UI dispatch loop has most likely died on an earlier test's " +
            "Avalonia setup/teardown exception, which would otherwise hang the run indefinitely.");
}
