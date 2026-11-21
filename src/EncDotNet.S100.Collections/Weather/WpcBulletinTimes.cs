using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Collections.Weather;

/// <summary>
/// The times of a WPC coded surface bulletin, read from its first lines for
/// the Library (design note <c>vector-weather.md</c> §6.4, §8.3): enough to
/// date an item without decoding the fronts, which the dataset reader does.
/// </summary>
/// <param name="Issued">The WMO heading's time (<c>ASUS02 KWBC 052100</c>), UTC.</param>
/// <param name="FirstValid">The first analysis or forecast's valid time, UTC.</param>
/// <param name="LastValid">The last one's, UTC.</param>
/// <param name="FirstLeadHours">The first forecast's lead time in hours, or null for an analysis.</param>
public sealed partial record WpcBulletinTimes(DateTimeOffset Issued, DateTimeOffset FirstValid, DateTimeOffset LastValid, int? FirstLeadHours)
{
    /// <summary>
    /// The time a forecast bulletin's lead times count from (the first
    /// forecast's valid time less its lead time), or the analysis's valid time.
    /// </summary>
    public DateTimeOffset Run => FirstLeadHours is { } hours ? FirstValid.AddHours(-hours) : FirstValid;

    /// <summary>
    /// Reads the times of the bulletin <paramref name="text"/>. A heading
    /// gives the day of the month only, so <paramref name="reference"/> (the
    /// file's last-modified time, or now) supplies the month and year: the
    /// latest such day not more than a day after it.
    /// </summary>
    /// <param name="text">The bulletin's text, or at least its first lines and every <c>VALID</c> line.</param>
    /// <param name="reference">A time shortly after the bulletin was issued.</param>
    /// <returns>The times, or null when the text has no WMO heading.</returns>
    public static WpcBulletinTimes? TryRead(string text, DateTimeOffset reference)
    {
        ArgumentNullException.ThrowIfNull(text);
        var heading = Heading().Match(text);
        if (!heading.Success)
            return null;
        var issued = Resolve(Int(heading.Groups["d"]), Int(heading.Groups["h"]), Int(heading.Groups["m"]), reference);
        if (issued is null)
            return null;

        // Forecast sections: "12HR PROG VALID 060600Z" (DDHHMM). An analysis has
        // none; its valid time is the heading's (the HR bulletin's own "VALID
        // 110521Z" is MMDDHH, the standard one's DDHHMM, so the heading is the
        // one form both share).
        var sections = Prog().Matches(text)
            .Select(m => (Hours: Int(m.Groups["lead"]), Valid: Resolve(Int(m.Groups["d"]), Int(m.Groups["h"]), Int(m.Groups["m"]), issued.Value.AddDays(3))))
            .Where(s => s.Valid is not null)
            .Select(s => (s.Hours, Valid: s.Valid!.Value))
            .ToArray();
        if (sections.Length == 0)
            return new WpcBulletinTimes(issued.Value, issued.Value, issued.Value, null);

        var first = sections.MinBy(s => s.Valid);
        return new WpcBulletinTimes(issued.Value, first.Valid, sections.Max(s => s.Valid), first.Hours);
    }

    private static DateTimeOffset? Resolve(int day, int hour, int minute, DateTimeOffset reference)
    {
        if (day is < 1 or > 31 || hour > 23 || minute > 59)
            return null;
        var limit = reference.ToUniversalTime().AddDays(1);
        for (var back = 0; back < 3; back++)
        {
            var month = new DateTime(limit.Year, limit.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-back);
            if (day > DateTime.DaysInMonth(month.Year, month.Month))
                continue;
            var candidate = new DateTimeOffset(month.Year, month.Month, day, hour, minute, 0, TimeSpan.Zero);
            if (candidate <= limit)
                return candidate;
        }

        return null;
    }

    private static int Int(Group group) => int.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^[A-Z]{4}\d{2}\s+[A-Z]{4}\s+(?<d>\d{2})(?<h>\d{2})(?<m>\d{2})\b", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*(?<lead>\d{1,3})\s*HR\s+PROG\s+VALID\s+(?<d>\d{2})(?<h>\d{2})(?<m>\d{2})Z", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Prog();
}
