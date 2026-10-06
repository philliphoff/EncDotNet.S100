using System.Globalization;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// How a forecast run's times read in the Library (#685, #730). The run facts
/// themselves are <see cref="Collections.Library.ForecastRuns"/>.
/// </summary>
internal static class ForecastRunText
{
    /// <summary>"30.09.2026 12:00Z" (UTC).</summary>
    public static string FormatRun(DateTimeOffset time) =>
        time.UtcDateTime.ToString("d", CultureInfo.CurrentCulture) + " " + time.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + "Z";

    /// <summary>
    /// A run time as the user reads it (#730): "30.09.2026 12:00Z" in UTC, the
    /// local date and short time ("30.09.2026 05:00") in Local.
    /// </summary>
    /// <param name="time">The run time.</param>
    /// <param name="format">The user's Local/UTC setting.</param>
    /// <param name="zone">The machine's zone.</param>
    public static string FormatRun(DateTimeOffset time, TimeFormat format, TimeZoneInfo zone)
    {
        if (format == TimeFormat.Utc)
            return FormatRun(time);
        var local = TimeZoneInfo.ConvertTimeFromUtc(time.UtcDateTime, zone);
        return local.ToString("d", CultureInfo.CurrentCulture) + " " + local.ToString("t", CultureInfo.CurrentCulture);
    }

    /// <summary>"39 h left" / "4 d left" before <paramref name="end"/>, "Ended 9 h ago" after it.</summary>
    public static string TimeLeft(DateTimeOffset end, DateTimeOffset now) => TimeLeft(end - now);

    /// <summary>"39 h left" / "4 d left" for a positive <paramref name="span"/>, "Ended 9 h ago" for a negative one.</summary>
    public static string TimeLeft(TimeSpan span)
    {
        var c = CultureInfo.CurrentCulture;
        var past = span < TimeSpan.Zero;
        var hours = (int)Math.Floor(Math.Abs(span.TotalHours));
        var amount = hours >= 72
            ? string.Format(c, Strings.Library_Forecast_DaysFormat, hours / 24)
            : string.Format(c, Strings.Library_Forecast_HoursFormat, Math.Max(hours, past ? 0 : 1));
        return string.Format(c, past ? Strings.Library_Forecast_EndedFormat : Strings.Library_Forecast_LeftFormat, amount);
    }
}
