using EncDotNet.S100.DataModel;
using GeographicLib;

namespace EncDotNet.S100.Geodesy;

/// <summary>
/// Geodesics and rhumb lines on the WGS-84 ellipsoid: distances, bearings,
/// the direct problem and points along a line.
/// </summary>
/// <remarks>
/// <para>
/// Built on GeographicLib.NET, a C# port of GeographicLib (MIT; copyright
/// Charles Karney and the GeographicLib.NET contributors; see
/// THIRD-PARTY-NOTICES.md). It implements Karney, "Algorithms for
/// geodesics" (2013) and "The area of rhumb polygons" (2024), so results
/// agree with other navigation software to well under a millimetre and
/// 0.001°.
/// </para>
/// <para>
/// Bearings are true, clockwise from north, in [0°, 360°). Returned
/// longitudes are in [-180°, 180°]. A rhumb line can't pass a pole, so the
/// direct problem stops at one.
/// </para>
/// </remarks>
public static class Wgs84Geodesy
{
    /// <summary>Length in metres of the geodesic (shortest path) between two positions.</summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <returns>The distance in metres.</returns>
    public static double GeodesicDistanceMetres(GeoPosition from, GeoPosition to)
    {
        Geodesic.WGS84.Inverse(from.Latitude, from.Longitude, to.Latitude, to.Longitude, out double metres);
        return metres;
    }

    /// <summary>
    /// The initial true bearing of the geodesic at <paramref name="from"/>
    /// towards <paramref name="to"/>. Coincident positions have bearing 0°.
    /// </summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <returns>The bearing in degrees, in [0°, 360°).</returns>
    public static double GeodesicInitialBearingDegrees(GeoPosition from, GeoPosition to)
    {
        Geodesic.WGS84.Inverse(
            from.Latitude, from.Longitude, to.Latitude, to.Longitude,
            out double metres, out double azimuth, out _);
        return metres > 0 ? NormalizeBearing(azimuth) : 0.0;
    }

    /// <summary>
    /// The position <paramref name="distanceMetres"/> from
    /// <paramref name="origin"/> along the geodesic that leaves it on
    /// <paramref name="bearingDegrees"/> (the geodesic direct problem).
    /// </summary>
    /// <param name="origin">The start position.</param>
    /// <param name="bearingDegrees">Initial true bearing in degrees.</param>
    /// <param name="distanceMetres">Distance along the geodesic in metres.</param>
    /// <returns>The end position.</returns>
    public static GeoPosition GeodesicDestination(GeoPosition origin, double bearingDegrees, double distanceMetres)
    {
        Geodesic.WGS84.Direct(
            origin.Latitude, origin.Longitude, bearingDegrees, distanceMetres,
            out double latitude, out double longitude);
        return new GeoPosition(latitude, longitude);
    }

    /// <summary>
    /// The position a fraction of the way along the geodesic from
    /// <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <param name="fraction">Fraction of the distance, from 0 (start) to 1 (end).</param>
    /// <returns>The interpolated position; the ends themselves at 0 and 1.</returns>
    public static GeoPosition GeodesicInterpolate(GeoPosition from, GeoPosition to, double fraction)
    {
        if (fraction <= 0)
        {
            return from;
        }

        if (fraction >= 1)
        {
            return to;
        }

        var line = Geodesic.WGS84.InverseLine(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
        line.Position(fraction * line.Distance, out double latitude, out double longitude);
        return new GeoPosition(latitude, longitude);
    }

    /// <summary>
    /// <paramref name="segments"/> + 1 evenly spaced points along the
    /// geodesic from <paramref name="from"/> to <paramref name="to"/>. The
    /// first and last are the given positions exactly.
    /// </summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <param name="segments">Number of pieces; at least 1.</param>
    /// <returns>The points, in order from start to end.</returns>
    public static GeoPosition[] GeodesicPoints(GeoPosition from, GeoPosition to, int segments)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segments);

        var points = new GeoPosition[segments + 1];
        points[0] = from;
        points[segments] = to;
        if (segments > 1)
        {
            var line = Geodesic.WGS84.InverseLine(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
            var step = line.Distance / segments;
            for (var i = 1; i < segments; i++)
            {
                line.Position(i * step, out double latitude, out double longitude);
                points[i] = new GeoPosition(latitude, longitude);
            }
        }

        return points;
    }

    /// <summary>Length in metres of the shorter rhumb line (loxodrome) between two positions.</summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <returns>The distance in metres.</returns>
    public static double RhumbDistanceMetres(GeoPosition from, GeoPosition to)
    {
        Rhumb.WGS84.Inverse(from.Latitude, from.Longitude, to.Latitude, to.Longitude, out double metres, out _);
        return metres;
    }

    /// <summary>
    /// The constant true bearing of the shorter rhumb line from
    /// <paramref name="from"/> to <paramref name="to"/>. Coincident
    /// positions have bearing 0°.
    /// </summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <returns>The bearing in degrees, in [0°, 360°).</returns>
    public static double RhumbBearingDegrees(GeoPosition from, GeoPosition to)
    {
        Rhumb.WGS84.Inverse(
            from.Latitude, from.Longitude, to.Latitude, to.Longitude,
            out double metres, out double azimuth);
        return metres > 0 ? NormalizeBearing(azimuth) : 0.0;
    }

    /// <summary>
    /// The position <paramref name="distanceMetres"/> from
    /// <paramref name="origin"/> along the rhumb line on
    /// <paramref name="bearingDegrees"/>. A line that would pass a pole
    /// ends at it.
    /// </summary>
    /// <param name="origin">The start position.</param>
    /// <param name="bearingDegrees">Constant true bearing in degrees.</param>
    /// <param name="distanceMetres">Distance along the rhumb line in metres.</param>
    /// <returns>The end position.</returns>
    public static GeoPosition RhumbDestination(GeoPosition origin, double bearingDegrees, double distanceMetres)
    {
        Rhumb.WGS84.Direct(
            origin.Latitude, origin.Longitude, bearingDegrees, distanceMetres,
            out double latitude, out double longitude);

        // GeographicLib carries a line over the pole and down the far side,
        // with no defined longitude; a navigator's rhumb line stops there.
        if (double.IsNaN(longitude))
        {
            var north = Math.Cos(bearingDegrees * (Math.PI / 180.0)) * distanceMetres >= 0;
            return new GeoPosition(north ? 90.0 : -90.0, origin.Longitude);
        }

        return new GeoPosition(latitude, longitude);
    }

    /// <summary>
    /// The position a fraction of the way along the shorter rhumb line from
    /// <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <param name="fraction">Fraction of the distance, from 0 (start) to 1 (end).</param>
    /// <returns>The interpolated position; the ends themselves at 0 and 1.</returns>
    public static GeoPosition RhumbInterpolate(GeoPosition from, GeoPosition to, double fraction)
    {
        if (fraction <= 0)
        {
            return from;
        }

        if (fraction >= 1)
        {
            return to;
        }

        Rhumb.WGS84.Inverse(
            from.Latitude, from.Longitude, to.Latitude, to.Longitude,
            out double metres, out double azimuth);
        return RhumbDestination(from, azimuth, fraction * metres);
    }

    /// <summary>
    /// Metres per degree of latitude and of longitude at a latitude: the
    /// ellipsoid's local scale, for a flat east/north frame a few kilometres
    /// across.
    /// </summary>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <returns>
    /// Metres per degree northward (from the meridional radius of curvature)
    /// and eastward (from the prime-vertical radius, times cos latitude).
    /// </returns>
    public static (double North, double East) MetresPerDegree(double latitude)
    {
        var phi = latitude * (Math.PI / 180.0);
        var sin = Math.Sin(phi);
        var w = 1.0 - EccentricitySquared * sin * sin;
        var primeVertical = Geodesic.WGS84.EquatorialRadius / Math.Sqrt(w);
        var meridional = primeVertical * (1.0 - EccentricitySquared) / w;
        return (meridional * (Math.PI / 180.0), primeVertical * Math.Cos(phi) * (Math.PI / 180.0));
    }

    private static readonly double EccentricitySquared =
        Geodesic.WGS84.Flattening * (2.0 - Geodesic.WGS84.Flattening);

    private static double NormalizeBearing(double degrees)
    {
        var bearing = degrees % 360.0;
        return bearing < 0 ? bearing + 360.0 : bearing;
    }
}
