namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// Conversions between S-100 true-scale denominators and EPSG:3857
/// resolutions, shared by the compositor and its host adapters.
/// </summary>
internal static class ScaleResolution
{
    /// <summary>
    /// Ground metres per display pixel per unit of scale denominator: S-100
    /// Part&#160;9 takes 1&#160;px = 0.28&#160;mm on the nominal display.
    /// </summary>
    internal const double DenomToResolutionMetres = 0.00028;

    // Earth radius (m) of the EPSG:3857 web-mercator sphere.
    private const double WebMercatorEarthRadius = 6378137.0;

    /// <summary>Converts an EPSG:3857 northing (metres) to its latitude in radians.</summary>
    internal static double WebMercatorYToLatitudeRadians(double y)
        => 2.0 * Math.Atan(Math.Exp(y / WebMercatorEarthRadius)) - Math.PI / 2.0;

    /// <summary>
    /// Converts a true-scale denominator to the EPSG:3857 resolution (metres
    /// per pixel at the equator) at <paramref name="latitudeRadians"/>:
    /// <c>denominator × 0.00028 / cos φ</c>.
    /// </summary>
    internal static double DenominatorToResolution(double scaleDenominator, double latitudeRadians)
    {
        var cos = Math.Cos(latitudeRadians);
        if (cos < 1e-6)
        {
            // Guard against the poles / invalid latitudes (EPSG:3857 is clamped
            // to ±85.06°, where cos ≈ 0.087, so this only trips on bad input).
            cos = 1e-6;
        }

        return scaleDenominator * DenomToResolutionMetres / cos;
    }
}
