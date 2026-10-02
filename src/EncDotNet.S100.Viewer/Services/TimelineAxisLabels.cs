using System.Globalization;
using System.Text.RegularExpressions;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>What an axis label marks.</summary>
internal enum AxisLabelKind
{
    /// <summary>A collapsed gap: <c>⋯ 6 wk ⋯</c>.</summary>
    Gap,

    /// <summary>The start of a day.</summary>
    Day,

    /// <summary>A 6-hour mark.</summary>
    Hour,
}

/// <summary>A label under the Timeline's axis, at a normalized <see cref="Position"/>.</summary>
internal readonly record struct AxisLabel(double Position, string Text, AxisLabelKind Kind);

/// <summary>
/// Lays out the labels under the Timeline's axis (#708, handoff C3): gap
/// labels first, then days, then 6-hour marks, each kept a minimum distance
/// from the labels already placed. Days and hours are in the user's zone.
/// </summary>
internal static partial class TimelineAxisLabels
{
    /// <summary>Minimum spacing between a day label and its neighbours, as a fraction of the axis.</summary>
    internal const double DaySpacing = 0.06;

    /// <summary>Minimum spacing between an hour label and its neighbours.</summary>
    internal const double HourSpacing = 0.045;

    /// <summary>Days at least this wide are labelled with their weekday ("Fri 02.10").</summary>
    internal const double WeekdayWidth = 0.09;

    /// <summary>Lays out the labels for <paramref name="axis"/>.</summary>
    public static IReadOnlyList<AxisLabel> Layout(TimelineAxisMap axis, TimeZoneInfo zone, CultureInfo culture)
    {
        if (axis.IsDegenerate)
            return [];
        var placed = new List<AxisLabel>();

        foreach (var gap in axis.Gaps)
            placed.Add(new AxisLabel(gap.Start + gap.Width / 2, string.Format(culture, Strings.TimelinePanel_GapFormat, GapLength(gap.Length, culture)), AxisLabelKind.Gap));

        var dayPattern = DayMonthPattern(culture);
        var days = Midnights(axis.Start, axis.End, zone).ToArray();
        var dayWidth = days.Length > 1 ? axis.ToPosition(days[1]) - axis.ToPosition(days[0]) : 1;
        foreach (var day in days)
        {
            if (axis.IsInCollapsedGap(day))
                continue;
            var local = TimeZoneInfo.ConvertTimeFromUtc(day, zone);
            var text = dayWidth >= WeekdayWidth
                ? $"{local.ToString("ddd", culture)} {local.ToString(dayPattern, culture)}"
                : local.ToString(dayPattern, culture);
            TryPlace(placed, new AxisLabel(axis.ToPosition(day), text, AxisLabelKind.Day), DaySpacing);
        }

        foreach (var hour in SixHourMarks(axis.Start, axis.End, zone))
        {
            if (axis.IsInCollapsedGap(hour))
                continue;
            var local = TimeZoneInfo.ConvertTimeFromUtc(hour, zone);
            TryPlace(placed, new AxisLabel(axis.ToPosition(hour), local.ToString("t", culture), AxisLabelKind.Hour), HourSpacing);
        }

        return [.. placed.OrderBy(l => l.Position)];
    }

    /// <summary>"5 h", "3 d", "6 wk", "3 mo": a gap's length for its label.</summary>
    internal static string GapLength(TimeSpan length, CultureInfo culture) =>
        length.TotalDays < 2 ? string.Format(culture, Strings.TimelinePanel_HoursFormat, (int)Math.Round(length.TotalHours))
        : length.TotalDays < 14 ? string.Format(culture, Strings.TimelinePanel_DaysFormat, (int)Math.Round(length.TotalDays))
        : length.TotalDays < 63 ? string.Format(culture, Strings.TimelinePanel_WeeksFormat, (int)Math.Round(length.TotalDays / 7))
        : string.Format(culture, Strings.TimelinePanel_MonthsFormat, (int)Math.Round(length.TotalDays / 30.44));

    /// <summary>The culture's short date pattern without the year: "dd.MM", "M/d", "MM-dd".</summary>
    internal static string DayMonthPattern(CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var stripped = YearPattern().Replace(pattern, string.Empty).Trim();
        return stripped.Length > 0 ? stripped : "M/d";
    }

    private static void TryPlace(List<AxisLabel> placed, AxisLabel label, double spacing)
    {
        if (label.Position < 0 || label.Position > 1)
            return;
        if (placed.Any(p => Math.Abs(p.Position - label.Position) < spacing))
            return;
        placed.Add(label);
    }

    private static IEnumerable<DateTime> Midnights(DateTime start, DateTime end, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(start, zone).Date;
        for (var day = local; ; day = day.AddDays(1))
        {
            var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), zone);
            if (utc > end)
                yield break;
            if (utc >= start)
                yield return utc;
        }
    }

    private static IEnumerable<DateTime> SixHourMarks(DateTime start, DateTime end, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(start, zone).Date;
        for (var mark = local; ; mark = mark.AddHours(6))
        {
            if (mark.Hour == 0)
                continue;
            var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(mark, DateTimeKind.Unspecified), zone);
            if (utc > end)
                yield break;
            if (utc >= start)
                yield return utc;
        }
    }

    // A year token and the separator next to it: "yyyy", "/yyyy", "yyyy-", ".yy".
    [GeneratedRegex(@"[^dMy]?y+[^dMy]?")]
    private static partial Regex YearPattern();
}
