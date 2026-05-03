using System;
using System.Globalization;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Mariner-friendly formatting for geographic coordinates. The format is
/// degrees-decimal-minutes (DDM), e.g. <c>"12°34.567'N  056°12.345'W"</c>,
/// matching the convention typically used on nautical charts.
/// </summary>
internal static class LatLonFormatter
{
    /// <summary>
    /// Placeholder used when no coordinate is available (e.g. the cursor is
    /// not over the map).
    /// </summary>
    public const string Placeholder = "—";

    /// <summary>
    /// Formats a (latitude, longitude) pair in degrees-decimal-minutes form.
    /// Latitude is rendered with two integer degree digits, longitude with
    /// three.
    /// </summary>
    public static string Format(double latitude, double longitude) =>
        $"{FormatDegMin(latitude, 'N', 'S', 2)}  {FormatDegMin(longitude, 'E', 'W', 3)}";

    private static string FormatDegMin(double value, char positive, char negative, int degDigits)
    {
        var hemi = value >= 0 ? positive : negative;
        var abs = Math.Abs(value);
        var deg = (int)Math.Floor(abs);
        var min = (abs - deg) * 60.0;
        // Guard against floating-point rounding pushing minutes to 60.000.
        if (min >= 60.0)
        {
            deg += 1;
            min = 0.0;
        }

        var degFmt = "D" + degDigits;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{deg.ToString(degFmt, CultureInfo.InvariantCulture)}°{min.ToString("00.000", CultureInfo.InvariantCulture)}'{hemi}");
    }
}
