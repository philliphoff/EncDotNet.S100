using System;
using System.Globalization;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Formats a web-mercator viewport resolution as a representative map-scale
/// denominator (e.g. <c>"1:180 000"</c>) for display in the status bar. The
/// scale is approximate: it corrects for web-mercator latitude distortion and
/// assumes the OGC standardized rendering pixel size of 0.28&#160;mm.
/// </summary>
internal static class MapScaleFormatter
{
    /// <summary>Empty when no scale is available.</summary>
    public const string Placeholder = "";

    private const double EarthRadiusMeters = 6378137.0;

    // OGC standardized rendering pixel size (0.28 mm), the conventional basis
    // for converting ground-meters-per-pixel into a 1:N map-scale denominator.
    internal const double PixelSizeMeters = 0.00028;

    /// <summary>
    /// Formats the scale denominator for the given EPSG:3857 viewport.
    /// </summary>
    /// <param name="mercatorResolution">Resolution in mercator meters per pixel.</param>
    /// <param name="mercatorCenterY">Mercator Y of the viewport center, used to correct for latitude distortion.</param>
    /// <returns>A string like <c>"1:180 000"</c>, or <see cref="Placeholder"/> when no scale is available.</returns>
    public static string Format(double mercatorResolution, double mercatorCenterY)
    {
        if (double.IsNaN(mercatorResolution) || mercatorResolution <= 0)
            return Placeholder;

        var latitudeRadians = Math.Atan(Math.Sinh(mercatorCenterY / EarthRadiusMeters));
        var groundMetersPerPixel = mercatorResolution * Math.Cos(latitudeRadians);
        if (groundMetersPerPixel <= 0 || double.IsNaN(groundMetersPerPixel))
            return Placeholder;

        var denominator = groundMetersPerPixel / PixelSizeMeters;
        var rounded = RoundToSignificant(denominator);

        // Space-grouped thousands, e.g. "180 000", to read cleanly on charts.
        return "1:" + rounded.ToString("#,0", SpaceGroupFormat);
    }

    private static double RoundToSignificant(double value)
    {
        if (value <= 0)
            return 0;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)) - 2);
        return Math.Round(value / magnitude) * magnitude;
    }

    private static readonly NumberFormatInfo SpaceGroupFormat = new()
    {
        NumberGroupSeparator = "\u00A0",
        NumberGroupSizes = [3],
    };
}
