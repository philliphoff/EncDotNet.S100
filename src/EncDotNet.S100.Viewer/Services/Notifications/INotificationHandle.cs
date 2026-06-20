namespace EncDotNet.S100.Viewer.Services.Notifications;

/// <summary>
/// A live handle to a displayed notification, returned by
/// <see cref="INotificationBuilder.Show"/>. Lets the caller mutate the
/// notification's content over its lifetime and dismiss it programmatically.
/// All members are safe to call from any thread (mutations are marshaled onto
/// the UI thread) and become no-ops once the notification is dismissed.
/// </summary>
internal interface INotificationHandle
{
    /// <summary>Stable identity shared with the underlying view-model.</summary>
    Guid Id { get; }

    /// <summary><see langword="true"/> once the notification has been dismissed.</summary>
    bool IsDismissed { get; }

    /// <summary>Raised once when the notification is dismissed (by any cause).</summary>
    event EventHandler? Dismissed;

    /// <summary>
    /// Updates one or more common fields. Arguments left <see langword="null"/>
    /// are unchanged.
    /// </summary>
    void Update(
        string? title = null,
        string? message = null,
        NotificationSeverity? severity = null);

    /// <summary>Shows the progress region and sets a determinate value (0..1).</summary>
    void Report(double value);

    /// <summary>Shows the progress region in indeterminate mode.</summary>
    void SetIndeterminate(bool indeterminate);

    /// <summary>Hides the progress region (e.g. on completion).</summary>
    void ClearProgress();

    /// <summary>Replaces the action buttons (pass none to clear them).</summary>
    void SetActions(params NotificationActionDescriptor[] actions);

    /// <summary>Replaces the custom content region.</summary>
    void SetCustomContent(object? content);

    /// <summary>
    /// Schedules the notification to auto-dismiss after the given delay,
    /// replacing any existing schedule. Use to transition a persistent progress
    /// notification into an auto-dismissing terminal state.
    /// </summary>
    void ScheduleAutoDismiss(TimeSpan after);

    /// <summary>Cancels any pending auto-dismiss, keeping the notification visible.</summary>
    void CancelAutoDismiss();

    /// <summary>Dismisses the notification, moving it into history.</summary>
    void Dismiss();
}
