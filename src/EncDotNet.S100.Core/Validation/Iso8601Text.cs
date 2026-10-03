using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Validation;

/// <summary>
/// Recognises ISO 8601 calendar dates, times of day and date-times written in
/// either the <em>basic</em> format (<c>20260902</c>, <c>105406Z</c>,
/// <c>20260902T105406+0000</c>) or the <em>extended</em> format
/// (<c>2026-09-02</c>, <c>10:54:06Z</c>, <c>2026-09-02T10:54:06+00:00</c>).
/// </summary>
/// <remarks>
/// S-100 Part 10c writes HDF5 date and time attributes (<c>issueDate</c>,
/// <c>issueTime</c>, <c>dateTimeOfFirstRecord</c>, <c>timePoint</c>) in the
/// basic format, while some producers use the extended one; validation rules
/// must accept both. Times may carry a decimal fraction of a second and an
/// optional zone designator (<c>Z</c>, <c>±hh</c>, <c>±hhmm</c> or
/// <c>±hh:mm</c>); a time without one is an ISO 8601 local time. Mixing the two
/// formats inside a date-time (<c>20260902T10:54:06Z</c>) is accepted because
/// producers write it and readers tolerate it.
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
    public static bool IsCalendarDate(string? text)
        => !string.IsNullOrWhiteSpace(text)
            && DateOnly.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>
    /// <see langword="true"/> when <paramref name="text"/> is an ISO 8601 time of
    /// day in the basic (<c>hhmmss</c>, <c>hhmm</c>, <c>hh</c>) or extended
    /// (<c>hh:mm:ss</c>, <c>hh:mm</c>) format, with an optional decimal fraction
    /// of a second and an optional zone designator.
    /// </summary>
    /// <param name="text">The text to test; surrounding white space is ignored.</param>
    public static bool IsTimeOfDay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = TimeOfDayPattern().Match(text.Trim());
        if (!match.Success)
            return false;

        int hour = Field(match, "h");
        int minute = Field(match, "m");
        int second = Field(match, "s");
        bool hasFraction = match.Groups["f"].Success && match.Groups["f"].Value.Any(c => c != '0');
        if (hour == 24)
        {
            // 24:00:00 is ISO 8601's "end of day"; nothing past it.
            if (minute != 0 || second != 0 || hasFraction)
                return false;
        }
        else if (hour > 23)
        {
            return false;
        }

        // Second 60 admits a leap second.
        if (minute > 59 || second > 60)
            return false;

        return !match.Groups["zh"].Success
            || (Field(match, "zh") <= 23 && Field(match, "zm") <= 59);
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="text"/> is an ISO 8601
    /// calendar date (see <see cref="IsCalendarDate"/>) or a date-time made of
    /// such a date, the designator <c>T</c>, and a time of day (see
    /// <see cref="IsTimeOfDay"/>).
    /// </summary>
    /// <param name="text">The text to test; surrounding white space is ignored.</param>
    public static bool IsDateOrDateTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        int t = trimmed.IndexOfAny(['T', 't']);
        return t < 0
            ? IsCalendarDate(trimmed)
            : IsCalendarDate(trimmed[..t]) && IsTimeOfDay(trimmed[(t + 1)..]);
    }

    private static int Field(Match match, string group)
        => match.Groups[group].Success
            ? int.Parse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : 0;

    // Basic: hh[mm[ss[.f]]]; extended: hh:mm[:ss[.f]]; then an optional
    // Z / ±hh / ±hhmm / ±hh:mm zone designator.
    [GeneratedRegex(
        @"^(?<h>\d{2})(?:(?<m>\d{2})(?:(?<s>\d{2})(?:[.,](?<f>\d+))?)?|:(?<m>\d{2})(?::(?<s>\d{2})(?:[.,](?<f>\d+))?)?)?" +
        @"(?:[Zz]|[+-](?<zh>\d{2})(?::?(?<zm>\d{2}))?)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimeOfDayPattern();
}
