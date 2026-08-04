using Avalonia.Headless;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia.Tests;

internal static class HeadlessTest
{
    public static void Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessTest).Assembly);
        session.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();
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
        return session.Dispatch(body, CancellationToken.None);
    }
}
