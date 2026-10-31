using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Geodesy;

/// <summary>
/// Marine-navigation geodesy helpers used by the measure tools and the route
/// overlay: rhumb-line and great-circle distances, bearings and direct
/// problems, and densified lines and circles for drawing.
/// </summary>
/// <remarks>
/// All inputs are WGS-84 latitude/longitude in degrees, measured on the
/// WGS-84 ellipsoid by <see cref="Wgs84Geodesy"/>; a great circle is the
/// ellipsoidal geodesic. Bearings are true (relative to true north), in the
/// closed-open range [0°, 360°).
/// </remarks>
internal static class MarineGeodesy
{
    /// <summary>
    /// Longest straight piece, in metres, of a densified great circle or
    /// geodesic circle. Shorter pieces keep the drawn curve on the true one
    /// at large scales.
    /// </summary>
    public const double MaxSegmentMetres = 50_000.0;

    /// <summary>Fewest pieces a geodesic circle is drawn with, however small.</summary>
    public const int MinCircleSegments = 64;

    /// <summary>
    /// Computes the rhumb-line distance, in nautical miles, between two
    /// WGS-84 points along a constant true bearing (loxodrome).
    /// </summary>
    public static double RhumbDistanceNm(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg) =>
        Wgs84Geodesy.RhumbDistanceMetres(new GeoPosition(lat1Deg, lon1Deg), new GeoPosition(lat2Deg, lon2Deg))
        / PassageGeodesy.MetersPerNauticalMile;

    /// <summary>
    /// Computes the rhumb-line true bearing in degrees [0°, 360°) from the
    /// first point to the second.
    /// </summary>
    public static double RhumbBearingDegrees(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg) =>
        Wgs84Geodesy.RhumbBearingDegrees(new GeoPosition(lat1Deg, lon1Deg), new GeoPosition(lat2Deg, lon2Deg));

    /// <summary>
    /// Computes the great-circle (geodesic) distance, in nautical miles,
    /// between two WGS-84 points. This is the shortest distance and pairs
    /// with <see cref="GreatCircleInitialBearingDegrees"/> for legs whose
    /// geometry is geodesic rather than loxodromic.
    /// </summary>
    public static double GreatCircleDistanceNm(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg) =>
        Wgs84Geodesy.GeodesicDistanceMetres(new GeoPosition(lat1Deg, lon1Deg), new GeoPosition(lat2Deg, lon2Deg))
        / PassageGeodesy.MetersPerNauticalMile;

    /// <summary>
    /// Computes the great-circle initial (forward) true bearing in degrees
    /// [0°, 360°) from the first point to the second. Unlike a rhumb-line
    /// bearing this changes continuously along the path; it is the bearing
    /// to steer at the departure point.
    /// </summary>
    public static double GreatCircleInitialBearingDegrees(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg) =>
        Wgs84Geodesy.GeodesicInitialBearingDegrees(new GeoPosition(lat1Deg, lon1Deg), new GeoPosition(lat2Deg, lon2Deg));

    /// <summary>
    /// Returns <paramref name="segments"/>+1 evenly spaced points along the
    /// great circle (geodesic) from the first point to the second, both ends
    /// included. Lets a renderer draw a geodesic leg as a curve in Mercator
    /// space rather than a straight (rhumb-looking) line.
    /// <paramref name="segments"/> is clamped to at least 1.
    /// </summary>
    public static IReadOnlyList<GeoPosition> GreatCircleIntermediatePoints(
        double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg, int segments) =>
        Wgs84Geodesy.GeodesicPoints(
            new GeoPosition(lat1Deg, lon1Deg), new GeoPosition(lat2Deg, lon2Deg), Math.Max(1, segments));

    /// <summary>Distance in metres from one position to another along the given geometry.</summary>
    public static double DistanceMetres(LegGeometry geometry, GeoPosition from, GeoPosition to) =>
        PassageGeodesy.DistanceMeters(from, to, geometry);

    /// <summary>
    /// True bearing in degrees [0°, 360°) at <paramref name="from"/> towards
    /// <paramref name="to"/>: the constant bearing of a rhumb line, or the
    /// initial bearing of a great circle.
    /// </summary>
    public static double InitialBearingDegrees(LegGeometry geometry, GeoPosition from, GeoPosition to) =>
        geometry == LegGeometry.GreatCircle
            ? GreatCircleInitialBearingDegrees(from.Latitude, from.Longitude, to.Latitude, to.Longitude)
            : RhumbBearingDegrees(from.Latitude, from.Longitude, to.Latitude, to.Longitude);

    /// <summary>
    /// True bearing in degrees [0°, 360°) at <paramref name="to"/> back
    /// towards <paramref name="from"/>. On a rhumb line it's the forward
    /// bearing ± 180°; on a great circle it generally isn't.
    /// </summary>
    public static double ReverseBearingDegrees(LegGeometry geometry, GeoPosition from, GeoPosition to) =>
        InitialBearingDegrees(geometry, to, from);

    /// <summary>
    /// The position <paramref name="distanceMetres"/> from
    /// <paramref name="origin"/> on an initial true bearing, along a great
    /// circle or a rhumb line.
    /// </summary>
    public static GeoPosition Destination(LegGeometry geometry, GeoPosition origin, double bearingDegrees, double distanceMetres) =>
        geometry == LegGeometry.GreatCircle
            ? Wgs84Geodesy.GeodesicDestination(origin, bearingDegrees, distanceMetres)
            : Wgs84Geodesy.RhumbDestination(origin, bearingDegrees, distanceMetres);

    /// <summary>
    /// Points along the great circle from <paramref name="from"/> to
    /// <paramref name="to"/>, both ends included, no more than
    /// <see cref="MaxSegmentMetres"/> apart. The ends are the given
    /// positions exactly, so the line meets its waypoints.
    /// </summary>
    public static IReadOnlyList<GeoPosition> DensifyGreatCircle(GeoPosition from, GeoPosition to)
    {
        var metres = Wgs84Geodesy.GeodesicDistanceMetres(from, to);
        var segments = Math.Max(1, (int)Math.Ceiling(metres / MaxSegmentMetres));
        return Wgs84Geodesy.GeodesicPoints(from, to, segments);
    }

    /// <summary>
    /// A closed geodesic circle: every point is <paramref name="radiusMetres"/>
    /// from <paramref name="centre"/> along a geodesic. Points run
    /// clockwise from north, at least <see cref="MinCircleSegments"/> pieces
    /// and none longer than <see cref="MaxSegmentMetres"/>; the last point
    /// repeats the first.
    /// </summary>
    public static IReadOnlyList<GeoPosition> GeodesicCircle(GeoPosition centre, double radiusMetres)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radiusMetres);

        // Only sets the piece count: a small circle of angular radius r on
        // the mean sphere has circumference 2πR·sin r.
        var circumference = 2.0 * Math.PI * GeodesicHelper.EarthRadiusMetres
            * Math.Abs(Math.Sin(radiusMetres / GeodesicHelper.EarthRadiusMetres));
        var segments = Math.Max(MinCircleSegments, (int)Math.Ceiling(circumference / MaxSegmentMetres));

        var points = new GeoPosition[segments + 1];
        for (var i = 0; i < segments; i++)
        {
            points[i] = Wgs84Geodesy.GeodesicDestination(centre, 360.0 * i / segments, radiusMetres);
        }

        points[segments] = points[0];
        return points;
    }

    /// <summary>
    /// Splits a sequence of waypoints into one or more sub-paths so that no
    /// segment crosses the antimeridian (±180° longitude). Each output
    /// sub-path uses unwrapped longitudes that are continuous across its
    /// own segments; this lets a Mercator renderer draw the path as
    /// straight Mercator lines without the "wrap around the world"
    /// artifact you get when consecutive points span e.g. 179°E and
    /// 179°W.
    /// </summary>
    /// <remarks>
    /// Within a sub-path the second-and-later longitudes may exceed the
    /// canonical [-180, 180] range — this is intentional so the renderer
    /// can project the segment as a single straight line in Mercator
    /// space. Each new sub-path resets to the canonical range at its
    /// first point.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<GeoPosition>> SplitAtAntimeridian(
        IEnumerable<GeoPosition> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var result = new List<List<GeoPosition>>();
        List<GeoPosition>? current = null;
        double prevLon = 0.0;

        foreach (var pt in points)
        {
            if (current is null)
            {
                current = new List<GeoPosition> { pt };
                prevLon = pt.Longitude;
                result.Add(current);
                continue;
            }

            // If the longitude jump exceeds 180° in either direction, the
            // shorter rhumb crossed the antimeridian — start a new sub-path.
            var rawDelta = pt.Longitude - prevLon;
            if (rawDelta > 180.0 || rawDelta < -180.0)
            {
                current = new List<GeoPosition> { pt };
                result.Add(current);
                prevLon = pt.Longitude;
            }
            else
            {
                var unwrapped = prevLon + rawDelta;
                current.Add(new GeoPosition(pt.Latitude, unwrapped));
                prevLon = unwrapped;
            }
        }

        return result;
    }
}
