using System.Globalization;

namespace EncDotNet.S100.DataModel;

/// <summary>
/// Parses the date and time values S-100 date ranges carry: a date
/// (<c>S100_TruncatedDate</c>, written as <c>2026-03-01</c> or
/// <c>20260301</c>) with an optional time of day (<c>Time</c>, written as
/// <c>06:30:00Z</c> or <c>063000Z</c>), as in the <c>fixedDateRange</c>
/// complex attribute's <c>dateStart</c>/<c>timeOfDayStart</c> and
/// <c>dateEnd</c>/<c>timeOfDayEnd</c>.
/// </summary>
/// <remarks>
/// Times without a zone are taken as UTC, as S-124 Ed 2.0.0 requires. A
/// date given as a full date-time is accepted too, as some Edition 1.0.0
/// datasets write one. Truncated dates without a year (such as
/// <c>--03-01</c>) aren't moments and return <c>null</c>.
/// </remarks>
public static class S100DateTimes
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyyMMdd"];

    private static readonly string[] TimeFormats =
    [
        "HH:mm:ss", "HH:mm:ss'Z'", "HH:mm:ss.FFFFFFF", "HH:mm:ss.FFFFFFF'Z'",
        "HHmmss", "HHmmss'Z'", "HH:mm", "HH:mm'Z'",
    ];

    /// <summary>
    /// The UTC moment a date and optional time of day name: the start of the
    /// day when <paramref name="timeOfDay"/> is <c>null</c> or empty.
    /// </summary>
    /// <param name="date">The date, or a full date-time.</param>
    /// <param name="timeOfDay">The time of day, or <c>null</c>.</param>
    /// <returns>The moment, or <c>null</c> when either value doesn't parse.</returns>
    public static DateTimeOffset? Combine(string? date, string? timeOfDay)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return null;
        }

        date = date.Trim();
        if (DateOnly.TryParseExact(date, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            if (string.IsNullOrWhiteSpace(timeOfDay))
            {
                return new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            }

            return TimeOnly.TryParseExact(timeOfDay.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                ? new DateTimeOffset(day.ToDateTime(time), TimeSpan.Zero)
                : null;
        }

        // A full date-time (no separate time of day applies).
        return date.Contains('T', StringComparison.Ordinal)
            && DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment)
            ? moment
            : null;
    }
}
