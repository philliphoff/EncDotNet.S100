namespace EncDotNet.S100.Viewer.Services.Notifications;

/// <summary>
/// Minimal abstraction over the UI dispatcher so the notification service can
/// marshal all view-model and collection mutations onto the UI thread while
/// remaining unit-testable with a synchronous test double.
/// </summary>
internal interface IUiDispatcher
{
    /// <summary>
    /// <see langword="true"/> when the calling thread is the UI thread and
    /// work can run inline without marshaling.
    /// </summary>
    bool IsOnUiThread { get; }

    /// <summary>Queues <paramref name="action"/> to run on the UI thread.</summary>
    void Post(Action action);
}
