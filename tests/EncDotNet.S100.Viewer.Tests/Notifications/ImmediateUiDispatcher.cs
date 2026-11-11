using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.Tests.Notifications;

/// <summary>
/// Synchronous <see cref="IUiDispatcher"/> test double: it reports the
/// current thread as the UI thread and runs posted callbacks inline, so the
/// notification service can be exercised headless without an Avalonia app.
/// </summary>
internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => true;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }
}
