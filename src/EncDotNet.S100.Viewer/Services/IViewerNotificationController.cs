using Avalonia.Threading;
using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Lists and dismisses the viewer's notifications for agents (MCP
/// <c>list_notifications</c> / <c>dismiss_notification</c>, #715). Dismissing
/// uses each notification's own close command, as the user's × does.
/// </summary>
internal interface IViewerNotificationController
{
    /// <summary>Lists the notifications on screen, oldest first.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The active notifications.</returns>
    Task<IReadOnlyList<ViewerNotification>> ListAsync(CancellationToken ct = default);

    /// <summary>Dismisses the notification with <paramref name="id"/>, or every one when null.</summary>
    /// <param name="id">The notification id, or null for all.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The ids dismissed; empty when <paramref name="id"/> matched none.</returns>
    Task<IReadOnlyList<Guid>> DismissAsync(Guid? id, CancellationToken ct = default);
}

/// <summary>A notification as the user sees it.</summary>
/// <param name="Id">The notification id.</param>
/// <param name="Severity">"info", "success", "warning" or "error".</param>
/// <param name="Title">The title.</param>
/// <param name="Message">The body text, or null.</param>
/// <param name="CreatedUtc">When it was raised.</param>
/// <param name="Persistent">True when it stays until dismissed.</param>
/// <param name="Actions">The labels of its action buttons.</param>
internal sealed record ViewerNotification(
    Guid Id,
    string Severity,
    string Title,
    string? Message,
    DateTimeOffset CreatedUtc,
    bool Persistent,
    IReadOnlyList<string> Actions);

/// <summary>Default <see cref="IViewerNotificationController"/> over <see cref="INotificationService"/>.</summary>
internal sealed class ViewerNotificationController : IViewerNotificationController
{
    private readonly INotificationService _notifications;
    private readonly Func<Action, Task> _dispatch;

    public ViewerNotificationController(INotificationService notifications, Func<Action, Task>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        _notifications = notifications;
        _dispatch = dispatch ?? (action => Dispatcher.UIThread.InvokeAsync(action).GetTask());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ViewerNotification>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<ViewerNotification> list = [];
        await _dispatch(() => list = _notifications.Active
            .OrderBy(n => n.CreatedUtc)
            .Select(n => new ViewerNotification(
                n.Id,
                n.Severity.ToString().ToLowerInvariant(),
                n.Title,
                n.Message,
                n.CreatedUtc,
                n.IsPersistent,
                n.Actions.Select(a => a.Label).ToArray()))
            .ToArray()).ConfigureAwait(false);
        return list;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> DismissAsync(Guid? id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<Guid> dismissed = [];
        await _dispatch(() =>
        {
            var targets = _notifications.Active.Where(n => id is null || n.Id == id).ToArray();
            foreach (var notification in targets)
                notification.CloseCommand.Execute(null);
            dismissed = targets.Select(n => n.Id).ToArray();
        }).ConfigureAwait(false);
        return dismissed;
    }
}
