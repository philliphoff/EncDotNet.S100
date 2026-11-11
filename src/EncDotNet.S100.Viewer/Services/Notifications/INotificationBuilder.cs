namespace EncDotNet.S100.Viewer.Services.Notifications;

/// <summary>
/// Fluent builder for a notification. Obtained from
/// <see cref="INotificationService.Create"/>; terminate the chain with
/// <see cref="Show"/> to display the notification and obtain a handle for
/// later mutation or dismissal.
/// </summary>
/// <remarks>
/// Severity, lifetime, and content regions are orthogonal: any combination is
/// valid. Calling a region method more than once overwrites the prior value
/// for that region.
/// </remarks>
internal interface INotificationBuilder
{
    /// <summary>Sets the severity (default <see cref="NotificationSeverity.Info"/>).</summary>
    INotificationBuilder WithSeverity(NotificationSeverity severity);

    /// <summary>Sets the optional body text. Ignored when null or empty.</summary>
    INotificationBuilder WithContent(string? message);

    /// <summary>
    /// Marks the notification ephemeral, auto-dismissing after the given delay.
    /// This is the default when neither <see cref="AutoDismiss"/> nor
    /// <see cref="Persistent"/> is called (using a severity-derived delay).
    /// </summary>
    INotificationBuilder AutoDismiss(TimeSpan after);

    /// <summary>
    /// Marks the notification persistent: it stays until the user closes it or
    /// the caller dismisses it through the returned handle.
    /// </summary>
    INotificationBuilder Persistent();

    /// <summary>Adds a determinate or indeterminate progress region.</summary>
    /// <param name="value">Initial progress in the range 0..1.</param>
    /// <param name="indeterminate">When <see langword="true"/> the bar animates without a value.</param>
    INotificationBuilder AsProgress(double value = 0d, bool indeterminate = false);

    /// <summary>Adds an action button.</summary>
    /// <param name="label">The button caption (caller-localized).</param>
    /// <param name="invoke">The callback run on click.</param>
    /// <param name="isPrimary">When <see langword="true"/> the button is styled as primary.</param>
    /// <param name="dismissOnInvoke">When <see langword="true"/> the notification dismisses after the callback.</param>
    INotificationBuilder WithAction(
        string label,
        Action invoke,
        bool isPrimary = false,
        bool dismissOnInvoke = true);

    /// <summary>Adds caller-supplied custom content rendered via data templates.</summary>
    INotificationBuilder WithCustomContent(object content);

    /// <summary>Displays the notification and returns a handle to it.</summary>
    INotificationHandle Show();
}
