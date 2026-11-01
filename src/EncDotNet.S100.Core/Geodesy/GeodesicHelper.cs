using EncDotNet.S100.DataModel;
namespace EncDotNet.S100.Geodesy;

/// <summary>
/// Geodesic helpers: the direct problem along a geodesic or a rhumb line,
/// and tessellation of augmented line geometry (S-100 Part 9A §11.5
/// <c>AugmentedRay</c>, <c>ArcByRadius</c>).
/// </summary>
/// <remarks>
/// The direct problems are solved on the WGS-84 ellipsoid by
/// <see cref="Wgs84Geodesy"/>.
/// </remarks>
public static class GeodesicHelper
{
    /// <summary>
    /// WGS-84 mean Earth radius in metres, for local approximations such as
    /// metres per degree or a Web Mercator scale. Distances and bearings
    /// between positions come from the ellipsoid (<see cref="Wgs84Geodesy"/>).
    /// </summary>
    public const double EarthRadiusMetres = 6_371_008.8;

    /// <summary>
    /// Number of tessellation segments per full 360° circle.
    /// Partial arcs use a proportional count (minimum 2 segments).
    /// </summary>
    private const int SegmentsPerFullCircle = 72;

    /// <summary>
    /// Solves the geodesic direct problem on the WGS-84 ellipsoid: given an
    /// origin point, initial true bearing, and distance, computes the
    /// destination point.
    /// </summary>
    /// <param name="latDeg">Origin latitude in degrees.</param>
    /// <param name="lonDeg">Origin longitude in degrees.</param>
    /// <param name="bearingDeg">Initial true bearing in degrees (clockwise from north).</param>
    /// <param name="distanceMetres">Distance along the geodesic in metres.</param>
    /// <returns>Destination (latitude, longitude) in degrees.</returns>
    public static GeoPosition DirectProblem(
        double latDeg, double lonDeg, double bearingDeg, double distanceMetres) =>
        Wgs84Geodesy.GeodesicDestination(new GeoPosition(latDeg, lonDeg), bearingDeg, distanceMetres);

    /// <summary>
    /// Solves the direct problem along a rhumb line (loxodrome) on the
    /// WGS-84 ellipsoid: given an origin point, a constant true bearing and
    /// a distance, computes the destination point.
    /// </summary>
    /// <param name="latDeg">Origin latitude in degrees.</param>
    /// <param name="lonDeg">Origin longitude in degrees.</param>
    /// <param name="bearingDeg">Constant true bearing in degrees (clockwise from north).</param>
    /// <param name="distanceMetres">Distance along the rhumb line in metres.</param>
    /// <returns>
    /// Destination (latitude, longitude) in degrees. A rhumb line can't pass
    /// a pole, so a line that would reach one ends there.
    /// </returns>
    public static GeoPosition RhumbDirectProblem(
        double latDeg, double lonDeg, double bearingDeg, double distanceMetres) =>
        Wgs84Geodesy.RhumbDestination(new GeoPosition(latDeg, lonDeg), bearingDeg, distanceMetres);

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
    public static IReadOnlyList<GeoPosition> TessellateArc(
        double centreLat, double centreLon, double radiusMetres,
        double startBearingDeg, double sweepDeg)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radiusMetres);

        bool isFullCircle = Math.Abs(Math.Abs(sweepDeg) - 360.0) < 0.01;

        int segments = Math.Max(2, (int)Math.Ceiling(
            SegmentsPerFullCircle * Math.Abs(sweepDeg) / 360.0));

        double step = sweepDeg / segments;
        var points = new List<GeoPosition>(segments + 1);

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
    /// Tessellates a geodesic ray from an origin
    /// along a bearing for a given distance.
    /// </summary>
    /// <param name="originLat">Origin latitude in degrees.</param>
    /// <param name="originLon">Origin longitude in degrees.</param>
    /// <param name="bearingDeg">True bearing in degrees (clockwise from north).</param>
    /// <param name="distanceMetres">Total ray length in metres.</param>
    /// <returns>
    /// An ordered list of at least two coordinates from origin to endpoint.
    /// Short rays (&lt; 10 km) are returned as a simple two-point segment;
    /// longer rays are tessellated to follow the geodesic.
    /// </returns>
    public static IReadOnlyList<GeoPosition> TessellateRay(
        double originLat, double originLon, double bearingDeg, double distanceMetres)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(distanceMetres);

        // For short rays (< 10 km), a straight segment is visually identical
        // to the geodesic at any reasonable zoom level.
        const double shortRayThreshold = 10_000.0;
        if (distanceMetres <= shortRayThreshold)
        {
            var end = DirectProblem(originLat, originLon, bearingDeg, distanceMetres);
            return [new GeoPosition(originLat, originLon), end];
        }

        // Longer rays: tessellate at ~5 km intervals.
        const double segmentLength = 5_000.0;
        int segments = Math.Max(2, (int)Math.Ceiling(distanceMetres / segmentLength));
        double stepDist = distanceMetres / segments;

        var points = new List<GeoPosition>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            points.Add(DirectProblem(originLat, originLon, bearingDeg, i * stepDist));
        }

        return points;
    }
}
