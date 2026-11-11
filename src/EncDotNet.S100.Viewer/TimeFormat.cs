using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Selects how date/time values are formatted across the viewer UI.
/// Persisted to <see cref="ViewerSettings.TimeFormat"/>.
/// </summary>
internal enum TimeFormat
{
    /// <summary>
    /// Render timestamps in the user's local time zone using the current
    /// culture's short date/time conventions. No UTC indicator is shown.
    /// </summary>
    Local,

    /// <summary>
    /// Render timestamps in UTC with an explicit "UTC" suffix
    /// (ISO-8601-ish: <c>yyyy-MM-dd HH:mm:ss UTC</c>, invariant culture).
    /// </summary>
    Utc,
}

internal static class TimeFormatExtensions
{
    /// <summary>Human-readable display name for use in the settings UI.</summary>
    public static string DisplayName(this TimeFormat format) => format switch
    {
        TimeFormat.Local => Strings.Settings_TimeFormat_Local,
        TimeFormat.Utc => Strings.Settings_TimeFormat_Utc,
        _ => format.ToString(),
    };
}
