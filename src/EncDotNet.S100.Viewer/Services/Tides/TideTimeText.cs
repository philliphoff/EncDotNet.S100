using System.Globalization;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// Times on the tide cards: the time of day, and "Today 16:20" or
/// "Tomorrow 04:50", in the Local/UTC setting (never the station's own zone).
/// </summary>
internal static class TideTimeText
{
    /// <summary>A time as "Today 16:20", "Tomorrow 04:50", "Yesterday 23:05" or "Fri 13 Nov 04:50".</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <param name="format">The Local/UTC setting.</param>
    /// <param name="nowUtc">Now (UTC), which "Today" is relative to.</param>
    /// <returns>The text.</returns>
    public static string DayAndClock(DateTime utc, TimeFormat format, DateTime nowUtc)
    {
        var day = InZone(utc, format).Date;
        var today = InZone(nowUtc, format).Date;
        var clock = TimeFormatting.FormatClock(utc, format);
        var offset = (day - today).Days;
        var dayText = offset switch
        {
            0 => Strings.Tides_Day_Today,
            1 => Strings.Tides_Day_Tomorrow,
            -1 => Strings.Tides_Day_Yesterday,
            _ => day.ToString("ddd d MMM", CultureInfo.CurrentCulture),
        };
        return string.Format(CultureInfo.CurrentCulture, Strings.Tides_DayAndClockFormat, dayText, clock);
    }

    private static DateTime InZone(DateTime utc, TimeFormat format)
    {
        var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return format == TimeFormat.Utc ? u : u.ToLocalTime();
    }
}
