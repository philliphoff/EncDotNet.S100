using System.Globalization;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Geodesy;

/// <summary>Why <see cref="MeasureFormat.TryParseRange"/> refused a range.</summary>
internal enum RangeParseError
{
    /// <summary>The range parsed.</summary>
    None,

    /// <summary>The text was empty.</summary>
    Empty,

    /// <summary>The text wasn't a number with an optional unit.</summary>
    NotARange,

    /// <summary>
    /// The unit was a bare <c>M</c>, which charts use for nautical miles and
    /// SI uses for metres.
    /// </summary>
    AmbiguousM,

    /// <summary>The range was zero or negative.</summary>
    NotPositive,
}

/// <summary>
/// Text for the measure and range &amp; bearing tools: distances in the
/// Distance unit setting and the other unit system, bearings, and parsing
/// of typed ranges and bearings.
/// </summary>
/// <remarks>
/// Distances show to 0.01 NM, 0.01 mi, 0.001 km or 1 m and bearings to
/// 0.1°, so display rounding takes at most 9 m and 0.05° of the S-164
/// accuracy tolerance. Typed numbers are read with the invariant culture,
/// as typed positions are (<see cref="LatLonFormatter.TryParse"/>).
/// </remarks>
internal static class MeasureFormat
{
    /// <summary>Metres in one statute mile.</summary>
    public const double MetresPerStatuteMile = 1609.344;

    private const double MetresPerNauticalMile = PassageGeodesy.MetersPerNauticalMile;

    /// <summary>
    /// A distance in the primary unit: <c>17.92 NM</c>, <c>33.194 km</c>
    /// (<c>950 m</c> below 1 km) or <c>20.62 mi</c>.
    /// </summary>
    public static string Distance(double metres, DistanceUnit unit, IFormatProvider? provider = null) =>
        unit switch
        {
            DistanceUnit.Metric => Metric(metres, provider),
            DistanceUnit.Miles => string.Format(provider ?? CultureInfo.CurrentCulture, "{0:#,0.00} mi", metres / MetresPerStatuteMile),
            _ => NauticalMiles(metres, provider),
        };

    /// <summary>
    /// The same distance in the other unit system, shown under the primary:
    /// metric under nautical or statute miles, nautical miles under metric.
    /// </summary>
    public static string OtherDistance(double metres, DistanceUnit unit, IFormatProvider? provider = null) =>
        unit == DistanceUnit.Metric ? NauticalMiles(metres, provider) : Metric(metres, provider);

    /// <summary>
    /// A true bearing to 0.1°, zero-padded and wrapped so it never reads
    /// 360: <c>295.7°</c>, <c>005.0°</c>.
    /// </summary>
    public static string Bearing(double degrees, IFormatProvider? provider = null)
    {
        var rounded = Math.Round(NormalizeBearing(degrees), 1, MidpointRounding.AwayFromZero);
        if (rounded >= 360.0)
        {
            rounded -= 360.0;
        }

        return string.Format(provider ?? CultureInfo.CurrentCulture, "{0:000.0}°", rounded);
    }

    /// <summary>
    /// Reads a typed range. A number on its own is in the primary unit
    /// (kilometres when metric). A suffix overrides it: <c>m</c>,
    /// <c>km</c>, <c>NM</c> or <c>mi</c>, case-insensitive except that a
    /// bare <c>M</c> is refused as ambiguous. Thousands separators are
    /// allowed: <c>2,000 km</c>.
    /// </summary>
    public static bool TryParseRange(string? text, DistanceUnit unit, out double metres, out RangeParseError error)
    {
        metres = 0;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = RangeParseError.Empty;
            return false;
        }

        var unitStart = trimmed.Length;
        while (unitStart > 0 && char.IsLetter(trimmed[unitStart - 1]))
        {
            unitStart--;
        }

        var suffix = trimmed[unitStart..];
        var number = trimmed[..unitStart].TrimEnd();
        if (suffix == "M")
        {
            error = RangeParseError.AmbiguousM;
            return false;
        }

        double? metresPerUnit = suffix.ToLowerInvariant() switch
        {
            "" => unit switch
            {
                DistanceUnit.Metric => 1000.0,
                DistanceUnit.Miles => MetresPerStatuteMile,
                _ => MetresPerNauticalMile,
            },
            "m" => 1.0,
            "km" => 1000.0,
            "nm" or "nmi" => MetresPerNauticalMile,
            "mi" => MetresPerStatuteMile,
            _ => null,
        };

        if (metresPerUnit is null
            || !double.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value))
        {
            error = RangeParseError.NotARange;
            return false;
        }

        if (value <= 0)
        {
            error = RangeParseError.NotPositive;
            return false;
        }

        metres = value * metresPerUnit.Value;
        error = RangeParseError.None;
        return true;
    }

    /// <summary>
    /// Reads a typed true bearing from 0 to 360, with an optional
    /// <c>°</c> or <c>°T</c>. 360 reads as 0.
    /// </summary>
    public static bool TryParseBearing(string? text, out double degrees)
    {
        degrees = 0;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.EndsWith('T') || trimmed.EndsWith('t'))
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        if (trimmed.EndsWith('°'))
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value is < 0 or > 360)
        {
            return false;
        }

        degrees = value == 360 ? 0 : value;
        return true;
    }

    private static string NauticalMiles(double metres, IFormatProvider? provider) =>
        string.Format(provider ?? CultureInfo.CurrentCulture, "{0:#,0.00} NM", metres / MetresPerNauticalMile);

    private static string Metric(double metres, IFormatProvider? provider)
    {
        // Round first so 999.6 m reads 1.000 km, not 1,000 m.
        var wholeMetres = Math.Round(metres, MidpointRounding.AwayFromZero);
        return wholeMetres < 1000.0
            ? string.Format(provider ?? CultureInfo.CurrentCulture, "{0:0} m", wholeMetres)
            : string.Format(provider ?? CultureInfo.CurrentCulture, "{0:#,0.000} km", metres / 1000.0);
    }

    private static double NormalizeBearing(double degrees) => ((degrees % 360.0) + 360.0) % 360.0;
}
