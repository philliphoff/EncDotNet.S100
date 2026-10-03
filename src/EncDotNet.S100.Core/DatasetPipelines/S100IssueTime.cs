using System.Globalization;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Combines an S-100 HDF5 dataset's <c>issueDate</c> and <c>issueTime</c> root
/// attributes (S-100 Part 10c) into one UTC time. Producers write them in
/// several forms: <c>20210414</c> / <c>2026-10-03</c> for the date, and
/// <c>135227Z</c>, <c>13:52:27</c> or <c>17:36:13.871916</c> for the time, with
/// an optional <c>Z</c> or UTC offset.
/// </summary>
/// <remarks>
/// The issue time is when the producer issued the file, which for a forecast
/// is usually some time after the model run it carries (NOAA's 12:00Z cbofs
/// run is issued around 13:45Z).
/// </remarks>
public static class S100IssueTime
{
    private static readonly string[] DateFormats = ["yyyyMMdd", "yyyy-MM-dd"];

    private static readonly string[] TimeFormats =
    [
        "HHmmss", "HHmm", "HH:mm:ss", "HH:mm", "HH:mm:ss.FFFFFFF", "HHmmss.FFFFFFF",
    ];

    /// <summary>
    /// The UTC issue time, or <see langword="null"/> when the date is missing
    /// or unreadable. A missing or unreadable time is taken as 00:00Z of the date.
    /// </summary>
    /// <param name="issueDate">The <c>issueDate</c> attribute.</param>
    /// <param name="issueTime">The <c>issueTime</c> attribute, if any.</param>
    public static DateTime? Parse(string? issueDate, string? issueTime)
    {
        if (string.IsNullOrWhiteSpace(issueDate)
            || !DateTime.TryParseExact(issueDate.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }
        var utc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        return TimeOfDay(issueTime) is { } time ? utc + time : utc;
    }

    /// <summary>The time of day in UTC, honouring a <c>Z</c> or <c>±hh[:]mm</c> suffix.</summary>
    private static TimeSpan? TimeOfDay(string? issueTime)
    {
        if (string.IsNullOrWhiteSpace(issueTime))
            return null;
        var text = issueTime.Trim();
        var offset = TimeSpan.Zero;
        if (text.EndsWith('Z') || text.EndsWith('z'))
        {
            text = text[..^1];
        }
        else if (text.LastIndexOfAny(['+', '-']) is var sign and > 0)
        {
            var zone = text[(sign + 1)..].Replace(":", string.Empty, StringComparison.Ordinal);
            if (zone.Length is 2 or 4
                && int.TryParse(zone[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
                && int.TryParse(zone.Length == 4 ? zone[2..] : "0", NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            {
                offset = new TimeSpan(hours, minutes, 0) * (text[sign] == '-' ? -1 : 1);
                text = text[..sign];
            }
        }
        return DateTime.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.TimeOfDay - offset
            : null;
    }
}
