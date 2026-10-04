using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Validation;

/// <summary>
/// Recognises and parses ISO 8601 calendar dates, times of day and date-times
/// written in either the <em>basic</em> format (<c>20260902</c>,
/// <c>105406Z</c>, <c>20260902T105406+0000</c>) or the <em>extended</em> format
/// (<c>2026-09-02</c>, <c>10:54:06Z</c>, <c>2026-09-02T10:54:06+00:00</c>).
/// </summary>
/// <remarks>
/// <para>
/// S-100 Part 10c writes HDF5 date and time attributes (<c>issueDate</c>,
/// <c>issueTime</c>, <c>dateTimeOfFirstRecord</c>, <c>timePoint</c>) in the
/// basic format, while some producers use the extended one; validation rules
/// and readers must accept both. Times may carry a decimal fraction of a second
/// and an optional zone designator (<c>Z</c>, <c>±hh</c>, <c>±hhmm</c> or
/// <c>±hh:mm</c>); a time without one is an ISO 8601 local time. Mixing the two
/// formats inside a date-time (<c>20260902T10:54:06Z</c>) is accepted because
/// producers write it and readers tolerate it.
/// </para>
/// <para>
/// The <c>Is…</c> checks are defined in terms of the <c>TryParse…</c> methods,
/// so a value that validates is a value the readers can read.
/// </para>
/// </remarks>
public static partial class Iso8601Text
{
    private static readonly string[] DateFormats = ["yyyyMMdd", "yyyy-MM-dd"];

    /// <summary>
    /// <see langword="true"/> when <paramref name="text"/> is a complete ISO 8601
    /// calendar date in the basic (<c>YYYYMMDD</c>) or extended
    /// (<c>YYYY-MM-DD</c>) format that names a real day.
    /// </summary>
    /// <param name="text">The text to test; surrounding white space is ignored.</param>
    public static bool IsCalendarDate(string? text) => TryParseCalendarDate(text, out _);

    /// <summary>
    /// <see langword="true"/> when <paramref name="text"/> is an ISO 8601 time of
    /// day in the basic (<c>hhmmss</c>, <c>hhmm</c>, <c>hh</c>) or extended
    /// (<c>hh:mm:ss</c>, <c>hh:mm</c>) format, with an optional decimal fraction
    /// of a second and an optional zone designator.
    /// </summary>
    /// <param name="text">The text to test; surrounding white space is ignored.</param>
    public static bool IsTimeOfDay(string? text) => TryParseTimeOfDay(text, out _, out _);

    /// <summary>
    /// <see langword="true"/> when <paramref name="text"/> is an ISO 8601
    /// calendar date (see <see cref="IsCalendarDate"/>) or a date-time made of
    /// such a date, the designator <c>T</c>, and a time of day (see
    /// <see cref="IsTimeOfDay"/>).
    /// </summary>
    /// <param name="text">The text to test; surrounding white space is ignored.</param>
    public static bool IsDateOrDateTime(string? text) => TryParseDateOrDateTimeParts(text, out _, out _, out _);

    /// <summary>
    /// Parses an ISO 8601 calendar date (see <see cref="IsCalendarDate"/>).
    /// </summary>
    /// <param name="text">The text to parse; surrounding white space is ignored.</param>
    /// <param name="date">The date, when parsing succeeds.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a calendar date.</returns>
    public static bool TryParseCalendarDate(string? text, out DateOnly date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(text)
            && DateOnly.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>
    /// Parses an ISO 8601 time of day (see <see cref="IsTimeOfDay"/>) into the
    /// clock reading and its zone designator.
    /// </summary>
    /// <param name="text">The text to parse; surrounding white space is ignored.</param>
    /// <param name="timeOfDay">
    /// The clock reading as written, before any offset is applied. A fraction
    /// of a second is kept to the nearest tick below; <c>24:00:00</c> (end of
    /// day) is one whole day, and a leap second (<c>23:59:60</c>) runs into the
    /// next minute.
    /// </param>
    /// <param name="utcOffset">
    /// The offset from UTC: <see cref="TimeSpan.Zero"/> for <c>Z</c>, the signed
    /// offset for <c>±hh</c> / <c>±hhmm</c> / <c>±hh:mm</c>, or
    /// <see langword="null"/> when the time carries no zone (an ISO 8601 local time).
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="text"/> is a time of day.</returns>
    public static bool TryParseTimeOfDay(string? text, out TimeSpan timeOfDay, out TimeSpan? utcOffset)
    {
        timeOfDay = default;
        utcOffset = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = TimeOfDayPattern().Match(text.Trim());
        if (!match.Success)
            return false;

        int hour = Field(match, "h");
        int minute = Field(match, "m");
        int second = Field(match, "s");
        long fractionTicks = FractionTicks(match.Groups["f"]);
        if (hour == 24)
        {
            // 24:00:00 is ISO 8601's "end of day"; nothing past it.
            if (minute != 0 || second != 0 || fractionTicks != 0)
                return false;
        }
        else if (hour > 23)
        {
            return false;
        }

        // Second 60 admits a leap second.
        if (minute > 59 || second > 60)
            return false;

        TimeSpan? offset = null;
        if (match.Groups["zh"].Success)
        {
            int zoneHours = Field(match, "zh");
            int zoneMinutes = Field(match, "zm");
            if (zoneHours > 23 || zoneMinutes > 59)
                return false;
            offset = new TimeSpan(zoneHours, zoneMinutes, 0);
            if (match.Groups["zs"].Value == "-")
                offset = -offset;
        }
        else if (match.Groups["z"].Success)
        {
            offset = TimeSpan.Zero;
        }

        timeOfDay = new TimeSpan(hour, minute, second) + TimeSpan.FromTicks(fractionTicks);
        utcOffset = offset;
        return true;
    }

    /// <summary>
    /// Parses an ISO 8601 calendar date or date-time (see
    /// <see cref="IsDateOrDateTime"/>) into a UTC time.
    /// </summary>
    /// <param name="text">The text to parse; surrounding white space is ignored.</param>
    /// <param name="utc">
    /// The time in UTC, with <see cref="DateTimeKind.Utc"/>. A date alone is
    /// 00:00 of that day; a time without a zone designator is taken as UTC,
    /// which is what S-100 producers mean by it.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="text"/> is a date or
    /// date-time whose UTC time lies within the range of <see cref="DateTime"/>.
    /// </returns>
    public static bool TryParseDateOrDateTime(string? text, out DateTime utc)
    {
        utc = default;
        if (!TryParseDateOrDateTimeParts(text, out var date, out var timeOfDay, out var utcOffset))
            return false;

        long ticks = date.ToDateTime(TimeOnly.MinValue).Ticks + timeOfDay.Ticks - (utcOffset ?? TimeSpan.Zero).Ticks;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            return false;

        utc = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }

    private static bool TryParseDateOrDateTimeParts(
        string? text, out DateOnly date, out TimeSpan timeOfDay, out TimeSpan? utcOffset)
    {
        date = default;
        timeOfDay = default;
        utcOffset = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        int t = trimmed.IndexOfAny(['T', 't']);
        return t < 0
            ? TryParseCalendarDate(trimmed, out date)
            : TryParseCalendarDate(trimmed[..t], out date) && TryParseTimeOfDay(trimmed[(t + 1)..], out timeOfDay, out utcOffset);
    }

    private static int Field(Match match, string group)
        => match.Groups[group].Success
            ? int.Parse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : 0;

    /// <summary>A decimal fraction of a second as ticks, truncating digits past the seventh.</summary>
    private static long FractionTicks(Group fraction)
    {
        if (!fraction.Success)
            return 0;

        var digits = fraction.Value.Length > 7 ? fraction.Value[..7] : fraction.Value.PadRight(7, '0');
        return long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    // Basic: hh[mm[ss[.f]]]; extended: hh:mm[:ss[.f]]; then an optional
    // Z / ±hh / ±hhmm / ±hh:mm zone designator. [0-9] rather than \d, which
    // would also match non-ASCII digits.
    [GeneratedRegex(
        @"^(?<h>[0-9]{2})(?:(?<m>[0-9]{2})(?:(?<s>[0-9]{2})(?:[.,](?<f>[0-9]+))?)?|:(?<m>[0-9]{2})(?::(?<s>[0-9]{2})(?:[.,](?<f>[0-9]+))?)?)?" +
        @"(?:(?<z>[Zz])|(?<zs>[+-])(?<zh>[0-9]{2})(?::?(?<zm>[0-9]{2}))?)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimeOfDayPattern();
}
