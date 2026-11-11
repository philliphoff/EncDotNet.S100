using Avalonia.Threading;

namespace EncDotNet.S100.Viewer.Services.Notifications;

/// <summary>
/// Default <see cref="IUiDispatcher"/> backed by Avalonia's
/// <see cref="Dispatcher.UIThread"/>.
/// </summary>
internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public bool IsOnUiThread => Dispatcher.UIThread.CheckAccess();

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Dispatcher.UIThread.Post(action);
    }
}
