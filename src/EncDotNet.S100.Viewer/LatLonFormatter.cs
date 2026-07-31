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
    /// not over the map). Empty so the status bar simply shows nothing in
    /// that state.
    /// </summary>
    public const string Placeholder = "";

    /// <summary>
    /// Formats a (latitude, longitude) pair in degrees-decimal-minutes form.
    /// Degrees are zero-padded (latitude to two digits, longitude to three)
    /// so the readout keeps a stable width when rendered with tabular figures.
    /// </summary>
    public static string Format(double latitude, double longitude) =>
        $"{FormatDegMin(latitude, 'N', 'S', 2)}  {FormatDegMin(longitude, 'E', 'W', 3)}";

    /// <summary>Formats just the latitude component, e.g. <c>"50°46.024'N"</c>.</summary>
    public static string FormatLatitude(double latitude) => FormatDegMin(latitude, 'N', 'S', 2);

    /// <summary>Formats just the longitude component, e.g. <c>"001°15.558'W"</c>.</summary>
    public static string FormatLongitude(double longitude) => FormatDegMin(longitude, 'E', 'W', 3);

    /// <summary>
    /// Formats a (latitude, longitude) pair as signed decimal degrees,
    /// e.g. <c>"47.601234, -122.334567"</c>. This machine-friendly form is
    /// what the pick panel places on the clipboard so the coordinate can be
    /// pasted straight into tools and scripts during debugging.
    /// </summary>
    public static string FormatDecimal(double latitude, double longitude) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{latitude:0.000000}, {longitude:0.000000}");

    private static string FormatDegMin(double value, char positive, char negative, int degWidth)
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

        var degText = deg.ToString(CultureInfo.InvariantCulture).PadLeft(degWidth, '0');
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{degText}°{min.ToString("00.000", CultureInfo.InvariantCulture)}'{hemi}");
    }
}
