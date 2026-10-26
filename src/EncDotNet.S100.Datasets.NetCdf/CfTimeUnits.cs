using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// CF time units: a unit and a reference time, for example
/// <c>"hours since 2026-10-25 00:00:00"</c> (CF Conventions §4.4, "Time
/// Coordinate"). Converts a coordinate value to a UTC time.
/// </summary>
/// <remarks>
/// <para>
/// The units are seconds, minutes, hours or days, in the UDUNITS spellings
/// (<c>s</c>, <c>sec</c>, <c>min</c>, <c>h</c>, <c>hr</c>, <c>d</c>, plural
/// or singular). Months and years fail: their length varies, and CF advises
/// against them. The reference time is <c>YYYY-MM-DD</c>, optionally
/// followed by a time (<c>hh</c>, <c>hh:mm</c> or <c>hh:mm:ss.sss</c>, after
/// a space or a <c>T</c>) and a time zone (<c>Z</c>, <c>UTC</c> or an offset
/// such as <c>+00:00</c> or <c>-5</c>); a time with no zone is UTC.
/// </para>
/// <para>
/// Only the <c>standard</c> (the default), <c>gregorian</c> and
/// <c>proleptic_gregorian</c> calendars are supported; for the dates weather
/// data uses they are the same. Any other calendar (<c>noleap</c>,
/// <c>360_day</c>, <c>julian</c>, …) fails with a
/// <see cref="CfConventionsException"/>.
/// </para>
/// </remarks>
public sealed partial class CfTimeUnits
{
    private CfTimeUnits(TimeSpan unit, DateTime epoch, string text)
    {
        Unit = unit;
        Epoch = epoch;
        Text = text;
    }

    /// <summary>The length of one unit, for example one hour.</summary>
    public TimeSpan Unit { get; }

    /// <summary>The reference time, in UTC.</summary>
    public DateTime Epoch { get; }

    /// <summary>The units as the file gives them.</summary>
    public string Text { get; }

    /// <summary>Parses CF time units.</summary>
    /// <param name="units">The <c>units</c> attribute, for example <c>"seconds since 1970-01-01 00:00:00"</c>.</param>
    /// <param name="calendar">The <c>calendar</c> attribute, or <see langword="null"/> for the default (<c>standard</c>).</param>
    /// <returns>The parsed units.</returns>
    /// <exception cref="CfConventionsException">
    /// The units aren't "&lt;unit&gt; since &lt;time&gt;", the unit isn't
    /// seconds, minutes, hours or days, the time can't be read, or the
    /// calendar isn't supported.
    /// </exception>
    public static CfTimeUnits Parse(string units, string? calendar = null)
    {
        ArgumentNullException.ThrowIfNull(units);
        RequireSupportedCalendar(calendar);

        var match = SincePattern().Match(units);
        if (!match.Success)
        {
            throw new CfConventionsException(
                $"The time units '{units}' aren't CF time units ('<unit> since <reference time>', for example 'hours since 2026-10-25 00:00:00').");
        }

        var unitName = match.Groups["unit"].Value.ToLowerInvariant();
        var unit = unitName switch
        {
            "s" or "sec" or "secs" or "second" or "seconds" => TimeSpan.FromSeconds(1),
            "min" or "mins" or "minute" or "minutes" => TimeSpan.FromMinutes(1),
            "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.FromHours(1),
            "d" or "day" or "days" => TimeSpan.FromDays(1),
            "month" or "months" or "year" or "years" or "yr" or "yrs" or "common_year" or "common_years" =>
                throw new CfConventionsException(
                    $"The time units '{units}' count {unitName}, whose length varies; only seconds, minutes, hours and days are supported."),
            _ => throw new CfConventionsException(
                $"The time units '{units}' count '{unitName}', which isn't a time unit this reader knows (seconds, minutes, hours or days)."),
        };

        var reference = match.Groups["reference"].Value;
        var epoch = ParseTime(reference)
            ?? throw new CfConventionsException(
                $"The reference time '{reference}' in the time units '{units}' can't be read; expected 'YYYY-MM-DD hh:mm:ss' with an optional time zone.");
        return new CfTimeUnits(unit, epoch, units);
    }

    /// <summary>Converts a coordinate value to a UTC time, to the nearest millisecond.</summary>
    /// <param name="value">The coordinate value, in <see cref="Unit"/>s since <see cref="Epoch"/>.</param>
    /// <returns>The time, in UTC.</returns>
    /// <exception cref="CfConventionsException">The value isn't finite, or the time is out of range.</exception>
    public DateTime ToUtc(double value)
    {
        if (!double.IsFinite(value))
            throw new CfConventionsException($"A time coordinate value is {value.ToString(CultureInfo.InvariantCulture)}, not a time.");

        double milliseconds = Math.Round(value * Unit.TotalMilliseconds);
        try
        {
            return DateTime.SpecifyKind(Epoch.AddMilliseconds(milliseconds), DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new CfConventionsException(
                string.Create(CultureInfo.InvariantCulture, $"The time coordinate value {value} {Text} is outside the dates .NET can represent."),
                ex);
        }
    }

    /// <summary>Returns the units as the file gives them.</summary>
    public override string ToString() => Text;

    /// <summary>
    /// Parses an ISO 8601-style time as CF and ACDD write it
    /// (<c>2026-10-25 00:00:00</c>, <c>2026-10-25T06:00:00Z</c>,
    /// <c>2026-10-25</c>), returning it in UTC, or <see langword="null"/> when
    /// it can't be read.
    /// </summary>
    /// <param name="text">The time.</param>
    public static DateTime? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = TimePattern().Match(text.Trim());
        if (!match.Success)
            return null;

        try
        {
            int year = Int(match, "year");
            int month = Int(match, "month");
            int day = Int(match, "day");
            int hour = Int(match, "hour");
            int minute = Int(match, "minute");
            double second = match.Groups["second"].Success
                ? double.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture)
                : 0;
            var time = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc)
                .AddHours(hour)
                .AddMinutes(minute)
                .AddMilliseconds(Math.Round(second * 1000));

            if (match.Groups["offset"].Success)
            {
                int sign = match.Groups["sign"].Value == "-" ? -1 : 1;
                int offsetHours = Int(match, "offsetHour");
                int offsetMinutes = Int(match, "offsetMinute");
                time -= sign * new TimeSpan(offsetHours, offsetMinutes, 0);
            }

            return DateTime.SpecifyKind(time, DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="calendar"/> is one this reader supports (empty means <c>standard</c>).</summary>
    /// <param name="calendar">The <c>calendar</c> attribute.</param>
    public static bool IsSupportedCalendar(string? calendar) =>
        string.IsNullOrWhiteSpace(calendar) ||
        calendar.Trim().ToLowerInvariant() is "standard" or "gregorian" or "proleptic_gregorian";

    private static void RequireSupportedCalendar(string? calendar)
    {
        if (!IsSupportedCalendar(calendar))
        {
            throw new CfConventionsException(
                $"The time coordinate uses the '{calendar!.Trim()}' calendar; only the standard, gregorian and proleptic_gregorian calendars are supported.");
        }
    }

    private static int Int(Match match, string group) =>
        match.Groups[group].Success && match.Groups[group].Length > 0
            ? int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture)
            : 0;

    [GeneratedRegex(@"^\s*(?<unit>[A-Za-z_]+)\s+(?:since|after|from)\s+(?<reference>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SincePattern();

    [GeneratedRegex(
        @"^(?<year>\d{1,4})-(?<month>\d{1,2})-(?<day>\d{1,2})" +
        @"(?:[T ]+(?<hour>\d{1,2})(?::(?<minute>\d{1,2})(?::(?<second>\d{1,2}(?:\.\d*)?))?)?)?" +
        @"\s*(?:Z|UTC|GMT|(?<offset>(?<sign>[+-])(?<offsetHour>\d{1,2})(?::?(?<offsetMinute>\d{2}))?))?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TimePattern();
}
