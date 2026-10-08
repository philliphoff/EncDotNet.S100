using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// How a forecast run's times read in the Library (#685, #730). The run facts
/// themselves are <see cref="Collections.Library.ForecastRuns"/>.
/// </summary>
internal static class ForecastRunText
{
    /// <summary>A run time in UTC: the culture's short date and <c>HH:mmZ</c>.</summary>
    public static string FormatRun(DateTimeOffset time) => LibraryTextFormat.Run(time);

    /// <summary>A run time as the user reads it: their Local/UTC setting (#730).</summary>
    public static string FormatRun(DateTimeOffset time, TimeFormat format, TimeZoneInfo zone) =>
        LibraryTextFormat.Run(time, ToLibrary(format), zone);

    /// <summary>"39 h left", or "Ended 9 h ago".</summary>
    public static string TimeLeft(DateTimeOffset end, DateTimeOffset now) => LibraryTextFormat.TimeLeft(end, now);

    /// <summary>"39 h left" (or days past 72 h), or "Ended 9 h ago" for a negative span.</summary>
    public static string TimeLeft(TimeSpan span) => LibraryTextFormat.TimeLeft(span);

    /// <summary>The Library core's equivalent of the viewer's <see cref="TimeFormat"/>.</summary>
    public static LibraryTimeFormat ToLibrary(TimeFormat format) =>
        format == TimeFormat.Local ? LibraryTimeFormat.Local : LibraryTimeFormat.Utc;
}
