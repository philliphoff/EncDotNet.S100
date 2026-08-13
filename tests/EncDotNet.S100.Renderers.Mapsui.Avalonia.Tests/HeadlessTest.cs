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
        // WaitAsync bounds the wait and unwraps the body's exception in a single
        // step; GetResult then bridges it back to this synchronous test entry.
        WaitForDispatch(session.Dispatch(body, CancellationToken.None))
            .GetAwaiter()
            .GetResult();
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

    public static Task<T> RunAsync<T>(Func<Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessTest).Assembly);
        return WaitForDispatch(session.Dispatch(body, CancellationToken.None));
    }

    private static async Task<T> WaitForDispatch<T>(Task<T> dispatch)
    {
        try
        {
            return await dispatch.WaitAsync(DispatchTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw BrokenSessionTimeout(exception);
        }
    }

    private static async Task WaitForDispatch(Task dispatch)
    {
        try
        {
            await dispatch.WaitAsync(DispatchTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw BrokenSessionTimeout(exception);
        }
    }

    private static TimeoutException BrokenSessionTimeout(TimeoutException inner) =>
        new($"The Avalonia headless dispatch did not complete within {DispatchTimeout.TotalSeconds:0}s. " +
            "The per-assembly UI dispatch loop has most likely died on an earlier test's " +
            "Avalonia setup/teardown exception, which would otherwise hang the run indefinitely.",
            inner);
}
