namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// Spherical Web-Mercator (EPSG:3857) forward projection used by the shared
/// vector rendering core. Reimplemented here so the core has no dependency on
/// Mapsui (whose <c>SphericalMercator.FromLonLat</c> this matches).
/// </summary>
/// <remarks>
/// Uses the WGS-84 semi-major axis as the sphere radius
/// (<c>6378137 m</c>), the standard EPSG:3857 definition. A numeric parity
/// test asserts agreement with Mapsui's projection within a tight metre
/// tolerance, so refactoring the Mapsui path onto this implementation does not
/// shift the visual-regression baselines.
/// </remarks>
public static class WebMercator
{
    /// <summary>EPSG:3857 sphere radius (WGS-84 semi-major axis), in metres.</summary>
    public const double EarthRadius = 6378137.0;

    private const double DegToRad = Math.PI / 180.0;

    /// <summary>
    /// Projects a WGS-84 (longitude, latitude) pair, in degrees, to EPSG:3857
    /// metres (x = easting, y = northing).
    /// </summary>
    public static (double X, double Y) FromLonLat(double longitude, double latitude)
    {
        double x = EarthRadius * longitude * DegToRad;
        double y = EarthRadius * Math.Log(Math.Tan(Math.PI / 4.0 + latitude * DegToRad / 2.0));
        return (x, y);
    }
}
