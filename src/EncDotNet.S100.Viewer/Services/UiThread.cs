using Avalonia.Threading;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The app's ways onto Avalonia's UI thread, passed to services and view models
/// that take a dispatch delegate. Those types require the delegate so tests supply
/// their own (usually inline); the app passes one of these.
/// </summary>
internal static class UiThread
{
    /// <summary>Runs <paramref name="action"/> now on the UI thread, else posts it there.</summary>
    public static void RunOrPost(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// Runs <paramref name="action"/> now on the UI thread, else posts it there at
    /// background priority.
    /// </summary>
    public static void RunOrPostBackground(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }

    /// <summary>Posts <paramref name="action"/> to the UI thread, even from it.</summary>
    public static void Post(Action action) => Dispatcher.UIThread.Post(action);

    /// <summary>Posts <paramref name="action"/> to the UI thread at background priority.</summary>
    public static void PostBackground(Action action) => Dispatcher.UIThread.Post(action, DispatcherPriority.Background);

    /// <summary>Runs <paramref name="action"/> on the UI thread; completes when it has run.</summary>
    public static Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    /// <summary>Runs <paramref name="work"/> on the UI thread; completes when it has.</summary>
    public static Task InvokeAsync(Func<Task> work) => Dispatcher.UIThread.InvokeAsync(work);
}
