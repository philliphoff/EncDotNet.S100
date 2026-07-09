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

    /// <summary>
    /// The full EPSG:3857 world width (and height at the projection limits), in
    /// metres: one 360° trip around the equator (<c>2·π·<see cref="EarthRadius"/></c>
    /// ≈ 4.0075×10⁷ m). Used by seam-aware auto-fit to shift longitudes that wrap
    /// the ±180° antimeridian into a contiguous world-X window.
    /// </summary>
    public const double Circumference = 2.0 * Math.PI * EarthRadius;

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
    public static (double Longitude, double Latitude) ToLonLat(double x, double y) =>
        ToLonLat(x, y, clampLatitude: true);

    /// <summary>
    /// Inverse of <see cref="FromLonLat"/>: converts EPSG:3857 metres
    /// (x = easting, y = northing) back to a WGS-84 (longitude, latitude) pair
    /// in degrees.
    /// </summary>
    /// <param name="x">Easting in EPSG:3857 metres.</param>
    /// <param name="y">Northing in EPSG:3857 metres.</param>
    /// <param name="clampLatitude">
    /// <see langword="true"/> to clamp latitude to ±<see cref="MaxLatitude"/>
    /// (the practical Web-Mercator display limit). Pass <see langword="false"/>
    /// when the result is only used to reconstruct the exact EPSG:3857 bounds
    /// via <see cref="FromLonLat"/> — for example when building a render
    /// viewport from world-metre bounds. Clamping there is <b>lossy</b>: a
    /// viewport edge (or per-tile gutter) whose northing exceeds
    /// ±<c>π·<see cref="EarthRadius"/></c> would be pulled back to the pole
    /// limit, so the round-tripped span no longer matches the true world span
    /// and the projected geometry drifts poleward, increasingly so with
    /// latitude (seen when a high-latitude dataset such as the US NWS S-411
    /// sea-ice product is zoomed out). Because a normal in-range viewport never
    /// reaches the limit, disabling the clamp is a no-op except in that
    /// overflow case, where it is the correct behaviour.
    /// </param>
    public static (double Longitude, double Latitude) ToLonLat(double x, double y, bool clampLatitude)
    {
        double longitude = x / EarthRadius * RadToDeg;
        double latitude = (2.0 * Math.Atan(Math.Exp(y / EarthRadius)) - Math.PI / 2.0) * RadToDeg;
        if (double.IsNaN(latitude))
        {
            latitude = -MaxLatitude;
        }
        else if (clampLatitude)
        {
            if (latitude < -MaxLatitude) latitude = -MaxLatitude;
            else if (latitude > MaxLatitude) latitude = MaxLatitude;
        }

        return (longitude, latitude);
    }
}
