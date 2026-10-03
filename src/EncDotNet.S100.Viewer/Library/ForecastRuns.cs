using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Forecast-run facts of a Library dataset from an S-100 forecast feed (#685,
/// NOAA's S-111): its model, run time and valid window, and how they read.
/// </summary>
internal static class ForecastRuns
{
    /// <summary>True when <paramref name="item"/> is one run's dataset from a forecast feed.</summary>
    public static bool IsForecast(CollectionItem item) =>
        item.Properties.ContainsKey(S100ForecastFeedIndexer.RunProperty);

    /// <summary>The model the item belongs to (e.g. <c>cbofs</c>).</summary>
    public static string? ModelOf(CollectionItem item) => item.Properties.GetValueOrDefault(S100ForecastFeedIndexer.ModelProperty);

    /// <summary>How far ahead of its run the item forecasts.</summary>
    public static TimeSpan? Horizon(CollectionItem item) =>
        S100ForecastFeedIndexer.RunOf(item) is { } run && S100ForecastFeedIndexer.ValidToOf(item) is { } to ? to - run : null;

    /// <summary>
    /// The S-102 tile in the same grid cell as an S-111 tile (#685): NOAA names
    /// both on one tile grid, so <c>111US00_CBOFS_US4VA1DD</c> pairs with
    /// <c>102US004VA1DD</c>. <see langword="null"/> for a name with no tile cell.
    /// </summary>
    public static string? BathymetryTwinOf(string tileName)
    {
        ArgumentNullException.ThrowIfNull(tileName);
        var cell = tileName[(tileName.LastIndexOf('_') + 1)..];
        return cell.Length == 8 && cell.StartsWith("US", StringComparison.Ordinal) && char.IsAsciiDigit(cell[2])
            ? "102US00" + cell[2..]
            : null;
    }

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
