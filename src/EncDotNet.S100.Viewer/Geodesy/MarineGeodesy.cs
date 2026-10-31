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
/// All inputs are WGS-84 latitude/longitude in degrees on a sphere of
/// <see cref="GeodesicHelper.EarthRadiusMetres"/>. Bearings are true
/// (relative to true north), in the closed-open range [0°, 360°).
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

    /// <summary>Mean Earth radius in nautical miles.</summary>
    private const double EarthRadiusNm = GeodesicHelper.EarthRadiusMetres / PassageGeodesy.MetersPerNauticalMile;

    /// <summary>
    /// Maximum absolute latitude (degrees) used when evaluating rhumb-line
    /// formulas. Clamped to avoid the singularity at the poles where the
    /// Mercator projection (<c>ln tan(π/4 + φ/2)</c>) blows up.
    /// </summary>
    private const double LatitudeClampDegrees = 89.99;

    /// <summary>
    /// Computes the rhumb-line distance, in nautical miles, between two
    /// WGS-84 points along a constant true bearing (loxodrome).
    /// </summary>
    public static double RhumbDistanceNm(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
    {
        var phi1 = ClampLatRadians(lat1Deg);
        var phi2 = ClampLatRadians(lat2Deg);
        var dphi = phi2 - phi1;
        var dlam = NormalizeDeltaLonRadians(DegToRad(lon2Deg - lon1Deg));

        // Stretched (Mercator) latitude difference.
        var dpsi = Math.Log(Math.Tan(Math.PI / 4.0 + phi2 / 2.0) /
                            Math.Tan(Math.PI / 4.0 + phi1 / 2.0));

        // q = dphi/dpsi normally; degenerate to cos(phi1) when latitudes match.
        var q = Math.Abs(dpsi) > 1e-12 ? dphi / dpsi : Math.Cos(phi1);

        var distRadians = Math.Sqrt(dphi * dphi + q * q * dlam * dlam);
        return distRadians * EarthRadiusNm;
    }

    /// <summary>
    /// Computes the rhumb-line true bearing in degrees [0°, 360°) from the
    /// first point to the second.
    /// </summary>
    public static double RhumbBearingDegrees(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
    {
        var phi1 = ClampLatRadians(lat1Deg);
        var phi2 = ClampLatRadians(lat2Deg);
        var dlam = NormalizeDeltaLonRadians(DegToRad(lon2Deg - lon1Deg));

        var dpsi = Math.Log(Math.Tan(Math.PI / 4.0 + phi2 / 2.0) /
                            Math.Tan(Math.PI / 4.0 + phi1 / 2.0));

        var theta = Math.Atan2(dlam, dpsi);
        var deg = RadToDeg(theta);
        return ((deg % 360.0) + 360.0) % 360.0;
    }

    /// <summary>
    /// Computes the great-circle (orthodromic) distance, in nautical miles,
    /// between two WGS-84 points using the haversine formula on a sphere of
    /// mean Earth radius. This is the shorter geodesic distance and pairs
    /// with <see cref="GreatCircleInitialBearingDegrees"/> for legs whose
    /// geometry is geodesic rather than loxodromic.
    /// </summary>
    public static double GreatCircleDistanceNm(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
    {
        var phi1 = DegToRad(lat1Deg);
        var phi2 = DegToRad(lat2Deg);
        var dphi = DegToRad(lat2Deg - lat1Deg);
        var dlam = NormalizeDeltaLonRadians(DegToRad(lon2Deg - lon1Deg));

        var sinDphi = Math.Sin(dphi / 2.0);
        var sinDlam = Math.Sin(dlam / 2.0);
        var a = sinDphi * sinDphi + Math.Cos(phi1) * Math.Cos(phi2) * sinDlam * sinDlam;
        var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        return c * EarthRadiusNm;
    }

    /// <summary>
    /// Computes the great-circle initial (forward) true bearing in degrees
    /// [0°, 360°) from the first point to the second. Unlike a rhumb-line
    /// bearing this changes continuously along the path; it is the bearing
    /// to steer at the departure point.
    /// </summary>
    public static double GreatCircleInitialBearingDegrees(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
    {
        var phi1 = DegToRad(lat1Deg);
        var phi2 = DegToRad(lat2Deg);
        var dlam = NormalizeDeltaLonRadians(DegToRad(lon2Deg - lon1Deg));

        var y = Math.Sin(dlam) * Math.Cos(phi2);
        var x = Math.Cos(phi1) * Math.Sin(phi2) -
                Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dlam);
        var theta = Math.Atan2(y, x);
        var deg = RadToDeg(theta);
        return ((deg % 360.0) + 360.0) % 360.0;
    }

    /// <summary>
    /// Returns <paramref name="segments"/>+1 points along the great-circle
    /// arc from the first point to the second (inclusive of both ends),
    /// using spherical interpolation. Lets a renderer draw a geodesic leg
    /// as a curve in Mercator space rather than a straight (rhumb-looking)
    /// line. <paramref name="segments"/> is clamped to at least 1.
    /// </summary>
    public static IReadOnlyList<GeoPosition> GreatCircleIntermediatePoints(
        double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg, int segments)
    {
        if (segments < 1) segments = 1;

        var phi1 = DegToRad(lat1Deg);
        var lam1 = DegToRad(lon1Deg);
        var phi2 = DegToRad(lat2Deg);
        var lam2 = DegToRad(lon2Deg);

        // Angular distance between the two points (haversine).
        var dphi = phi2 - phi1;
        var dlam = NormalizeDeltaLonRadians(lam2 - lam1);
        var sinDphi = Math.Sin(dphi / 2.0);
        var sinDlam = Math.Sin(dlam / 2.0);
        var a = sinDphi * sinDphi + Math.Cos(phi1) * Math.Cos(phi2) * sinDlam * sinDlam;
        var delta = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));

        var result = new List<GeoPosition>(segments + 1);
        if (delta < 1e-12)
        {
            // Coincident points — nothing to interpolate.
            result.Add(new GeoPosition(lat1Deg, lon1Deg));
            result.Add(new GeoPosition(lat2Deg, lon2Deg));
            return result;
        }

        // Cartesian endpoints for slerp.
        var x1 = Math.Cos(phi1) * Math.Cos(lam1);
        var y1 = Math.Cos(phi1) * Math.Sin(lam1);
        var z1 = Math.Sin(phi1);
        var x2 = Math.Cos(phi2) * Math.Cos(lam2);
        var y2 = Math.Cos(phi2) * Math.Sin(lam2);
        var z2 = Math.Sin(phi2);
        var sinDelta = Math.Sin(delta);

        for (var i = 0; i <= segments; i++)
        {
            var f = (double)i / segments;
            var aCoef = Math.Sin((1.0 - f) * delta) / sinDelta;
            var bCoef = Math.Sin(f * delta) / sinDelta;
            var x = aCoef * x1 + bCoef * x2;
            var y = aCoef * y1 + bCoef * y2;
            var z = aCoef * z1 + bCoef * z2;
            var lat = Math.Atan2(z, Math.Sqrt(x * x + y * y));
            var lon = Math.Atan2(y, x);
            result.Add(new GeoPosition(RadToDeg(lat), RadToDeg(lon)));
        }

        return result;
    }

    /// <summary>Distance in metres from one position to another along the given geometry.</summary>
    public static double DistanceMetres(LegGeometry geometry, GeoPosition from, GeoPosition to) =>
        (geometry == LegGeometry.GreatCircle
            ? GreatCircleDistanceNm(from.Latitude, from.Longitude, to.Latitude, to.Longitude)
            : RhumbDistanceNm(from.Latitude, from.Longitude, to.Latitude, to.Longitude))
        * PassageGeodesy.MetersPerNauticalMile;

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
            ? GeodesicHelper.DirectProblem(origin.Latitude, origin.Longitude, bearingDegrees, distanceMetres)
            : GeodesicHelper.RhumbDirectProblem(origin.Latitude, origin.Longitude, bearingDegrees, distanceMetres);

    /// <summary>
    /// Points along the great circle from <paramref name="from"/> to
    /// <paramref name="to"/>, both ends included, no more than
    /// <see cref="MaxSegmentMetres"/> apart. The ends are the given
    /// positions exactly, so the line meets its waypoints.
    /// </summary>
    public static IReadOnlyList<GeoPosition> DensifyGreatCircle(GeoPosition from, GeoPosition to)
    {
        var metres = DistanceMetres(LegGeometry.GreatCircle, from, to);
        var segments = Math.Max(1, (int)Math.Ceiling(metres / MaxSegmentMetres));
        var points = GreatCircleIntermediatePoints(from.Latitude, from.Longitude, to.Latitude, to.Longitude, segments).ToArray();
        points[0] = from;
        points[^1] = to;
        return points;
    }

    /// <summary>
    /// A closed geodesic circle: every point is <paramref name="radiusMetres"/>
    /// from <paramref name="centre"/> along a great circle. Points run
    /// clockwise from north, at least <see cref="MinCircleSegments"/> pieces
    /// and none longer than <see cref="MaxSegmentMetres"/>; the last point
    /// repeats the first.
    /// </summary>
    public static IReadOnlyList<GeoPosition> GeodesicCircle(GeoPosition centre, double radiusMetres)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radiusMetres);

        // A small circle of angular radius r has circumference 2πR·sin r.
        var circumference = 2.0 * Math.PI * GeodesicHelper.EarthRadiusMetres
            * Math.Abs(Math.Sin(radiusMetres / GeodesicHelper.EarthRadiusMetres));
        var segments = Math.Max(MinCircleSegments, (int)Math.Ceiling(circumference / MaxSegmentMetres));

        var points = new GeoPosition[segments + 1];
        for (var i = 0; i < segments; i++)
        {
            points[i] = GeodesicHelper.DirectProblem(centre.Latitude, centre.Longitude, 360.0 * i / segments, radiusMetres);
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

    private static double DegToRad(double deg) => deg * (Math.PI / 180.0);
    private static double RadToDeg(double rad) => rad * (180.0 / Math.PI);

    private static double ClampLatRadians(double latDeg)
    {
        if (latDeg > LatitudeClampDegrees) latDeg = LatitudeClampDegrees;
        else if (latDeg < -LatitudeClampDegrees) latDeg = -LatitudeClampDegrees;
        return DegToRad(latDeg);
    }

    private static double NormalizeDeltaLonRadians(double dlamRadians)
    {
        // Wrap the longitude difference into (-π, π] so we always traverse
        // the short way around the globe.
        const double twoPi = 2.0 * Math.PI;
        if (dlamRadians > Math.PI) dlamRadians -= twoPi;
        else if (dlamRadians < -Math.PI) dlamRadians += twoPi;
        return dlamRadians;
    }
}
