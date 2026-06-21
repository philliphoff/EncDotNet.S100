namespace EncDotNet.S100.Rendering.Scene;

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

    /// <summary>
    /// The practical EPSG:3857 latitude limit (degrees) where the projection is
    /// defined; the Web-Mercator northing diverges towards the poles, so values
    /// are clamped to this range (the standard ±85.05112878°).
    /// </summary>
    public const double MaxLatitude = 85.05112878;

    private const double RadToDeg = 180.0 / Math.PI;

    /// <summary>
    /// Inverse of <see cref="FromLonLat"/>: converts EPSG:3857 metres
    /// (x = easting, y = northing) back to a WGS-84 (longitude, latitude) pair
    /// in degrees. Latitude is clamped to ±<see cref="MaxLatitude"/>.
    /// </summary>
    public static (double Longitude, double Latitude) ToLonLat(double x, double y)
    {
        double longitude = x / EarthRadius * RadToDeg;
        double latitude = (2.0 * Math.Atan(Math.Exp(y / EarthRadius)) - Math.PI / 2.0) * RadToDeg;
        if (double.IsNaN(latitude) || latitude < -MaxLatitude) latitude = -MaxLatitude;
        else if (latitude > MaxLatitude) latitude = MaxLatitude;
        return (longitude, latitude);
    }
}
