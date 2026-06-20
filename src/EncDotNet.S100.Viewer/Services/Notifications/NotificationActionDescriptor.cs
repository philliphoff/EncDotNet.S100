namespace EncDotNet.S100.Viewer.Services.Notifications;

/// <summary>
/// Describes a single action button on a notification. Supplied by callers
/// through <see cref="INotificationBuilder.WithAction"/> or
/// <see cref="INotificationHandle.SetActions"/>.
/// </summary>
/// <param name="Label">The button caption (caller-localized).</param>
/// <param name="Invoke">The callback run when the button is clicked.</param>
/// <param name="IsPrimary">
/// When <see langword="true"/> the button is styled as the primary action.
/// </param>
/// <param name="DismissOnInvoke">
/// When <see langword="true"/> the notification is dismissed immediately after
/// <paramref name="Invoke"/> runs. Defaults to <see langword="true"/>.
/// </param>
internal sealed record NotificationActionDescriptor(
    string Label,
    Action Invoke,
    bool IsPrimary = false,
    bool DismissOnInvoke = true);
