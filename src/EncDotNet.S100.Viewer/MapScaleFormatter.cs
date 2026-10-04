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

        var latitudeDegrees = MercatorYToLatitudeDegrees(mercatorCenterY);
        if (ResolutionToScaleDenominator(mercatorResolution, latitudeDegrees) is not { } denominator)
            return Placeholder;

        var rounded = RoundToSignificant(denominator);

        // Space-grouped thousands, e.g. "180 000", to read cleanly on charts.
        return "1:" + rounded.ToString("#,0", SpaceGroupFormat);
    }

    /// <summary>
    /// Converts a 1:N scale denominator at <paramref name="latitudeDegrees"/>
    /// into the EPSG:3857 resolution that shows it — the inverse of the
    /// status-bar conversion, so a viewport set this way reads back as
    /// <paramref name="scaleDenominator"/>.
    /// </summary>
    /// <param name="scaleDenominator">The scale denominator (e.g. <c>50000</c>); must be positive.</param>
    /// <param name="latitudeDegrees">The viewport centre latitude in decimal degrees.</param>
    /// <returns>Resolution in mercator metres per pixel.</returns>
    public static double ScaleDenominatorToResolution(double scaleDenominator, double latitudeDegrees)
    {
        // Floor the cosine so a near-polar centre cannot divide by zero; the
        // Web Mercator limit (±85.05°) keeps real inputs well above it.
        var cos = Math.Max(Math.Cos(latitudeDegrees * Math.PI / 180.0), 1e-6);
        return scaleDenominator * PixelSizeMeters / cos;
    }

    /// <summary>
    /// Converts an EPSG:3857 resolution at <paramref name="latitudeDegrees"/>
    /// into an unrounded 1:N scale denominator.
    /// </summary>
    /// <param name="mercatorResolution">Resolution in mercator metres per pixel.</param>
    /// <param name="latitudeDegrees">The viewport centre latitude in decimal degrees.</param>
    /// <returns>The scale denominator, or <see langword="null"/> when it cannot be computed.</returns>
    public static double? ResolutionToScaleDenominator(double mercatorResolution, double latitudeDegrees)
    {
        if (!double.IsFinite(mercatorResolution) || mercatorResolution <= 0)
            return null;

        var groundMetersPerPixel = mercatorResolution * Math.Cos(latitudeDegrees * Math.PI / 180.0);
        return double.IsFinite(groundMetersPerPixel) && groundMetersPerPixel > 0
            ? groundMetersPerPixel / PixelSizeMeters
            : null;
    }

    /// <summary>Converts an EPSG:3857 Y coordinate to a latitude in decimal degrees.</summary>
    /// <param name="mercatorY">The mercator Y coordinate in metres.</param>
    /// <returns>The latitude in decimal degrees.</returns>
    public static double MercatorYToLatitudeDegrees(double mercatorY) =>
        Math.Atan(Math.Sinh(mercatorY / EarthRadiusMeters)) * 180.0 / Math.PI;

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
