using System.Globalization;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Formats and parses passage values for the Routes panel. Numbers use the
/// app's locale (a decimal comma where the culture has one); depths (draft,
/// squat, required UKC) use the mariner's depth unit; cross-track limits are
/// always metres. Parsing accepts a decimal point or the culture's decimal
/// separator, and an optional unit.
/// </summary>
internal static class PassageText
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>A depth in <paramref name="unit"/>, e.g. <c>11,5 m</c>.</summary>
    public static string Depth(double metres, DepthUnit unit)
        => unit == DepthUnit.FathomsFeet
            ? DepthFormatting.Format(metres, unit)
            : $"{DepthFormatting.ToDisplay(metres, unit).ToString("0.0", Culture)} {DepthFormatting.UnitAbbreviation(unit)}";

    /// <summary>Parses a depth typed in <paramref name="unit"/> (or with another depth unit's suffix).</summary>
    public static bool TryParseDepth(string? text, DepthUnit unit, out double metres)
    {
        metres = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return DepthFormatting.TryParse(Invariant(text), unit, out metres) && metres >= 0 && double.IsFinite(metres);
    }

    /// <summary>The Draft · squat field: <c>11,5 m · 0,4 m</c>, or just the draft without squat.</summary>
    public static string DraftSquat(double? draft, double? squat, DepthUnit unit)
        => (draft, squat) switch
        {
            (null, null) => string.Empty,
            ({ } d, null) => Depth(d, unit),
            (null, { } s) => $"– · {Depth(s, unit)}",
            ({ } d, { } s) => $"{Depth(d, unit)} · {Depth(s, unit)}",
        };

    /// <summary>
    /// Parses the Draft · squat field: a draft, optionally followed by
    /// <c>·</c>, <c>+</c> or <c>/</c> and a squat. Empty text clears both.
    /// </summary>
    public static bool TryParseDraftSquat(string? text, DepthUnit unit, out double? draft, out double? squat)
    {
        draft = null;
        squat = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var parts = text.Split(['·', '+', '/'], StringSplitOptions.TrimEntries);
        if (parts.Length > 2)
            return false;
        if (!IsDash(parts[0]))
        {
            if (!TryParseDepth(parts[0], unit, out var d))
                return false;
            draft = d;
        }
        if (parts.Length == 2 && !IsDash(parts[1]))
        {
            if (!TryParseDepth(parts[1], unit, out var s))
                return false;
            squat = s;
        }
        return draft is not null || squat is not null;
    }

    /// <summary>A speed, e.g. <c>12,0 kn</c>.</summary>
    public static string Speed(double knots) => $"{knots.ToString("0.0", Culture)} kn";

    /// <summary>Parses a speed in knots (greater than zero); <c>kn</c>, <c>kt</c> or <c>knots</c> may follow.</summary>
    public static bool TryParseSpeed(string? text, out double knots)
    {
        knots = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var trimmed = StripSuffix(text.Trim(), "knots", "kn", "kt", "kts");
        return TryParseNumber(trimmed, out knots) && knots > 0;
    }

    /// <summary>Metres, e.g. <c>150 m</c> or <c>87,5 m</c>.</summary>
    public static string Metres(double metres) => $"{metres.ToString("0.#", Culture)} m";

    /// <summary>The Cross-track field: <c>150 m</c> when both sides match, else <c>150 / 120 m</c>.</summary>
    public static string CrossTrack(double? port, double? starboard)
    {
        if (port is null && starboard is null)
            return string.Empty;
        if (port == starboard)
            return Metres(port!.Value);
        string Side(double? v) => v is { } x ? x.ToString("0.#", Culture) : "–";
        return $"{Side(port)} / {Side(starboard)} m";
    }

    /// <summary>
    /// Parses the Cross-track field: one value for both sides, or
    /// <c>port / starboard</c>. A side written as <c>–</c> is unset. Empty
    /// text clears both.
    /// </summary>
    public static bool TryParseCrossTrack(string? text, out double? port, out double? starboard)
    {
        port = null;
        starboard = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var parts = StripSuffix(text.Trim(), "m").Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length > 2)
            return false;

        double? ParseSide(string side)
        {
            side = StripSuffix(side, "m");
            return IsDash(side) ? null : TryParseNumber(side, out var v) && v >= 0 ? v : double.NaN;
        }

        port = ParseSide(parts[0]);
        starboard = parts.Length == 2 ? ParseSide(parts[1]) : port;
        if (port is double.NaN || starboard is double.NaN)
        {
            port = starboard = null;
            return false;
        }
        return port is not null || starboard is not null;
    }

    /// <summary>A departure in the user's time format: the culture's short date and time locally, or UTC.</summary>
    public static string Departure(DateTimeOffset departure, TimeFormat format)
        => format == TimeFormat.Utc
            ? departure.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : departure.LocalDateTime.ToString("g", Culture);

    /// <summary>A departure's time of day, for the collapsed card summary.</summary>
    public static string DepartureTime(DateTimeOffset departure, TimeFormat format)
        => format == TimeFormat.Utc
            ? departure.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : departure.LocalDateTime.ToString("t", Culture);

    /// <summary>
    /// Parses a departure: a date and time, or a time alone (on
    /// <paramref name="today"/>'s date), read in the user's time format;
    /// a trailing <c>UTC</c> or <c>Z</c> forces UTC. Empty text clears it.
    /// </summary>
    public static bool TryParseDeparture(string? text, TimeFormat format, DateTimeOffset today, out DateTimeOffset? departure)
    {
        departure = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var trimmed = text.Trim();
        var utc = format == TimeFormat.Utc;
        foreach (var suffix in new[] { "UTC", "Z" })
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^suffix.Length].TrimEnd();
                utc = true;
            }
        }

        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault;
        if (!DateTime.TryParse(trimmed, Culture, styles, out var parsed)
            && !DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, styles, out parsed))
        {
            return false;
        }

        // A time alone parses onto 0001-01-01: put it on today's date in the
        // zone it was typed in.
        if (parsed.Date == DateTime.MinValue.Date)
        {
            var day = utc ? today.UtcDateTime.Date : today.ToLocalTime().Date;
            parsed = day + parsed.TimeOfDay;
        }

        var unspecified = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        departure = utc
            ? new DateTimeOffset(unspecified, TimeSpan.Zero)
            : new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
        return true;
    }

    /// <summary>Parses a non-negative number with a decimal point or the culture's decimal separator.</summary>
    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return double.TryParse(Invariant(text), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    /// <summary>A number in the app's locale, e.g. <c>11,5</c>, for examples in messages.</summary>
    public static string Number(double value) => value.ToString("0.0", Culture);

    private static string Invariant(string text)
    {
        var separator = Culture.NumberFormat.NumberDecimalSeparator;
        var trimmed = text.Trim();
        return separator == "." ? trimmed.Replace(',', '.') : trimmed.Replace(separator, ".");
    }

    private static bool IsDash(string text) => text is "-" or "–" or "—";

    private static string StripSuffix(string text, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return text[..^suffix.Length].TrimEnd();
        }
        return text;
    }
}
