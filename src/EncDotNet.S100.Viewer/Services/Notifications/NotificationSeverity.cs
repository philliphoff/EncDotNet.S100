namespace EncDotNet.S100.Viewer.Services.Notifications;

/// <summary>
/// Severity of a notification. Drives the accent colour, icon, and
/// accessibility label of the notification card. Severity is orthogonal to
/// both lifetime (ephemeral vs. persistent) and class
/// (<see cref="NotificationKind"/>): any class can carry any severity.
/// </summary>
internal enum NotificationSeverity
{
    /// <summary>Neutral, informational message.</summary>
    Info,

    /// <summary>A successful or completed operation.</summary>
    Success,

    /// <summary>A non-fatal problem the user should be aware of.</summary>
    Warning,

    /// <summary>A failure the user likely needs to act on.</summary>
    Error,
}
