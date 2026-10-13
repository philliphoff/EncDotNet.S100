using System.Globalization;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Viewer.Routing;

/// <summary>The kind of quantity a passage field holds, which sets its unit.</summary>
internal enum PassageQuantity
{
    /// <summary>A vertical measure (draft, squat, UKC): metres, or feet when the depth unit isn't metres.</summary>
    Depth,

    /// <summary>A speed, in knots.</summary>
    Speed,

    /// <summary>A horizontal distance (cross-track), in metres.</summary>
    Distance,
}

/// <summary>
/// Formats and parses passage values for the Passage card and leg detail,
/// in the app's locale. Values are stored in metres and knots; draft, squat
/// and UKC show in the Mariner Settings depth unit (feet for any non-metre
/// unit, since fathoms don't suit a draft).
/// </summary>
internal static class PassageFormatting
{
    /// <summary>The unit abbreviation <paramref name="quantity"/> shows in.</summary>
    public static string Unit(PassageQuantity quantity, DepthUnit depthUnit) => quantity switch
    {
        PassageQuantity.Depth => depthUnit == DepthUnit.Metres ? "m" : "ft",
        PassageQuantity.Speed => "kn",
        _ => "m",
    };

    /// <summary>Formats <paramref name="value"/> as a number without its unit, e.g. <c>11,5</c>.</summary>
    public static string Number(double value, PassageQuantity quantity, DepthUnit depthUnit)
    {
        var display = quantity == PassageQuantity.Depth && depthUnit != DepthUnit.Metres
            ? DepthFormatting.ToDisplay(value, DepthUnit.Feet)
            : value;
        var format = quantity == PassageQuantity.Distance ? "0" : "0.0";
        return display.ToString(format, CultureInfo.CurrentCulture);
    }

    /// <summary>Formats <paramref name="value"/> with its unit, e.g. <c>11,5 m</c>.</summary>
    public static string Format(double value, PassageQuantity quantity, DepthUnit depthUnit)
        => Number(value, quantity, depthUnit) + " " + Unit(quantity, depthUnit);

    /// <summary>Formats <paramref name="value"/> with its unit, or returns <c>null</c> when unset.</summary>
    public static string? FormatOrNull(double? value, PassageQuantity quantity, DepthUnit depthUnit)
        => value is { } v ? Format(v, quantity, depthUnit) : null;

    /// <summary>
    /// Parses a typed value. An empty entry parses as <c>null</c> (unset). A
    /// trailing unit (<c>m</c>, <c>ft</c>, <c>kn</c>) is accepted and ignored,
    /// and the app's decimal separator and <c>.</c> are both accepted.
    /// </summary>
    /// <returns><c>false</c> for anything that isn't a non-negative number.</returns>
    public static bool TryParse(string? text, PassageQuantity quantity, DepthUnit depthUnit, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var trimmed = text.Trim().TrimEnd('.').TrimEnd();
        var end = trimmed.Length;
        while (end > 0 && char.IsLetter(trimmed[end - 1]))
            end--;
        var number = trimmed[..end].Trim();

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            && !double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            return false;
        if (!double.IsFinite(parsed) || parsed < 0)
            return false;

        value = quantity == PassageQuantity.Depth && depthUnit != DepthUnit.Metres
            ? DepthFormatting.ToMetres(parsed, DepthUnit.Feet)
            : parsed;
        return true;
    }

    /// <summary>
    /// Formats a departure in the user's time preference: local time in the
    /// app's short date/time pattern, or <c>yyyy-MM-dd HH:mm UTC</c>.
    /// </summary>
    public static string FormatDeparture(DateTimeOffset value, TimeFormat format) => format == TimeFormat.Utc
        ? value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
        : value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>
    /// Parses a typed departure, read in the user's time preference unless it
    /// says <c>UTC</c>/<c>Z</c> or carries an offset. Empty parses as <c>null</c>.
    /// </summary>
    public static bool TryParseDeparture(string? text, TimeFormat format, out DateTimeOffset? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var trimmed = text.Trim();
        var utc = format == TimeFormat.Utc;
        if (trimmed.EndsWith("UTC", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^3].Trim();
            utc = true;
        }

        var styles = utc
            ? DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
            : DateTimeStyles.AssumeLocal;
        if (DateTimeOffset.TryParse(trimmed, CultureInfo.CurrentCulture, styles, out var parsed)
            || DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, styles, out parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }
}
