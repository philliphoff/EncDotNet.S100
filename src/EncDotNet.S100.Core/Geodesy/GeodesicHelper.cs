namespace EncDotNet.S100.Geodesy;

/// <summary>
/// Spherical geodesic helpers for tessellating augmented line geometry
/// (S-100 Part 9A §11.5 <c>AugmentedRay</c>, <c>ArcByRadius</c>).
/// </summary>
/// <remarks>
/// Uses a spherical Earth model with the WGS-84 mean radius. For the
/// short distances involved in sector-light portrayal (typically &lt; 50 NM),
/// the error vs. an ellipsoidal (Vincenty) solution is &lt; 0.3 %.
/// </remarks>
public static class GeodesicHelper
{
    /// <summary>WGS-84 mean Earth radius in metres.</summary>
    private const double EarthRadiusMetres = 6_371_008.8;

    private const double DegToRadFactor = Math.PI / 180.0;
    private const double RadToDegFactor = 180.0 / Math.PI;

    /// <summary>
    /// Number of tessellation segments per full 360° circle.
    /// Partial arcs use a proportional count (minimum 2 segments).
    /// </summary>
    private const int SegmentsPerFullCircle = 72;

    /// <summary>
    /// Solves the geodesic direct problem on a sphere: given an origin
    /// point, true bearing, and distance, computes the destination point.
    /// </summary>
    /// <param name="latDeg">Origin latitude in degrees.</param>
    /// <param name="lonDeg">Origin longitude in degrees.</param>
    /// <param name="bearingDeg">Initial true bearing in degrees (clockwise from north).</param>
    /// <param name="distanceMetres">Distance along the great circle in metres.</param>
    /// <returns>Destination (latitude, longitude) in degrees.</returns>
    public static (double Latitude, double Longitude) DirectProblem(
        double latDeg, double lonDeg, double bearingDeg, double distanceMetres)
    {
        double phi1 = latDeg * DegToRadFactor;
        double lambda1 = lonDeg * DegToRadFactor;
        double theta = bearingDeg * DegToRadFactor;
        double delta = distanceMetres / EarthRadiusMetres; // angular distance

        double sinPhi1 = Math.Sin(phi1);
        double cosPhi1 = Math.Cos(phi1);
        double sinDelta = Math.Sin(delta);
        double cosDelta = Math.Cos(delta);

        double phi2 = Math.Asin(sinPhi1 * cosDelta + cosPhi1 * sinDelta * Math.Cos(theta));
        double lambda2 = lambda1 + Math.Atan2(
            Math.Sin(theta) * sinDelta * cosPhi1,
            cosDelta - sinPhi1 * Math.Sin(phi2));

        // Normalise longitude to [-180, 180].
        double lonResult = lambda2 * RadToDegFactor;
        lonResult = ((lonResult + 540.0) % 360.0) - 180.0;

        return (phi2 * RadToDegFactor, lonResult);
    }

    /// <summary>
    /// Tessellates a circular arc around an origin point into a polyline
    /// of (latitude, longitude) coordinates.
    /// </summary>
    /// <param name="centreLat">Centre latitude in degrees.</param>
    /// <param name="centreLon">Centre longitude in degrees.</param>
    /// <param name="radiusMetres">Arc radius in metres.</param>
    /// <param name="startBearingDeg">Start bearing in degrees (clockwise from north).</param>
    /// <param name="sweepDeg">
    /// Sweep angle in degrees. Positive = clockwise. A sweep of 360 produces
    /// a closed circle.
    /// </param>
    /// <returns>
    /// An ordered list of coordinates approximating the arc. For a full circle
    /// the first and last points coincide.
    /// </returns>
    public static IReadOnlyList<(double Latitude, double Longitude)> TessellateArc(
        double centreLat, double centreLon, double radiusMetres,
        double startBearingDeg, double sweepDeg)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radiusMetres);

        bool isFullCircle = Math.Abs(Math.Abs(sweepDeg) - 360.0) < 0.01;

        int segments = Math.Max(2, (int)Math.Ceiling(
            SegmentsPerFullCircle * Math.Abs(sweepDeg) / 360.0));

        double step = sweepDeg / segments;
        var points = new List<(double, double)>(segments + 1);

        for (int i = 0; i <= segments; i++)
        {
            double bearing = startBearingDeg + i * step;
            points.Add(DirectProblem(centreLat, centreLon, bearing, radiusMetres));
        }

        // For a full circle, snap the last point to the first to guarantee closure.
        if (isFullCircle && points.Count >= 2)
        {
            points[^1] = points[0];
        }

        return points;
    }

    /// <summary>
    /// Tessellates a geodesic ray (great-circle segment) from an origin
    /// along a bearing for a given distance.
    /// </summary>
    /// <param name="originLat">Origin latitude in degrees.</param>
    /// <param name="originLon">Origin longitude in degrees.</param>
    /// <param name="bearingDeg">True bearing in degrees (clockwise from north).</param>
    /// <param name="distanceMetres">Total ray length in metres.</param>
    /// <returns>
    /// An ordered list of at least two coordinates from origin to endpoint.
    /// Short rays (&lt; 10 km) are returned as a simple two-point segment;
    /// longer rays are tessellated for great-circle fidelity.
    /// </returns>
    public static IReadOnlyList<(double Latitude, double Longitude)> TessellateRay(
        double originLat, double originLon, double bearingDeg, double distanceMetres)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(distanceMetres);

        // For short rays (< 10 km), a straight segment is visually identical
        // to the great-circle path at any reasonable zoom level.
        const double shortRayThreshold = 10_000.0;
        if (distanceMetres <= shortRayThreshold)
        {
            var end = DirectProblem(originLat, originLon, bearingDeg, distanceMetres);
            return [(originLat, originLon), end];
        }

        // Longer rays: tessellate at ~5 km intervals.
        const double segmentLength = 5_000.0;
        int segments = Math.Max(2, (int)Math.Ceiling(distanceMetres / segmentLength));
        double stepDist = distanceMetres / segments;

        var points = new List<(double, double)>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            points.Add(DirectProblem(originLat, originLon, bearingDeg, i * stepDist));
        }

        return points;
    }
}
