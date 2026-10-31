using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// Distances and interpolation along rhumb lines and great circles on the
/// WGS-84 ellipsoid, used to measure legs and to place samples along them.
/// </summary>
/// <remarks>
/// A great-circle leg follows the ellipsoidal geodesic. Both geometries come
/// from <see cref="Wgs84Geodesy"/>, as do the viewer's route metrics, so a leg
/// measured here has the same length the route panel shows.
/// </remarks>
public static class PassageGeodesy
{
    /// <summary>Metres in one international nautical mile.</summary>
    public const double MetersPerNauticalMile = 1852.0;

    /// <summary>Mean earth radius in metres (<see cref="GeodesicHelper.EarthRadiusMetres"/>).</summary>
    public const double EarthRadiusMeters = GeodesicHelper.EarthRadiusMetres;

    /// <summary>Mean earth radius in nautical miles.</summary>
    public const double EarthRadiusNauticalMiles = EarthRadiusMeters / MetersPerNauticalMile;

    /// <summary>Metres per degree of latitude on the mean sphere.</summary>
    public const double MetersPerDegreeLatitude = EarthRadiusMeters * Math.PI / 180.0;

    /// <summary>Distance in metres between two positions along the given leg geometry.</summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <param name="geometry">Rhumb line or great circle.</param>
    /// <returns>The distance in metres.</returns>
    public static double DistanceMeters(GeoPosition from, GeoPosition to, LegGeometry geometry) =>
        geometry == LegGeometry.GreatCircle
            ? GreatCircleDistanceMeters(from, to)
            : RhumbDistanceMeters(from, to);

    /// <summary>
    /// The position a fraction of the way from <paramref name="from"/> to
    /// <paramref name="to"/> along the given leg geometry.
    /// </summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <param name="fraction">Fraction of the distance, from 0 (start) to 1 (end).</param>
    /// <param name="geometry">Rhumb line or great circle.</param>
    /// <returns>The interpolated position.</returns>
    public static GeoPosition Interpolate(GeoPosition from, GeoPosition to, double fraction, LegGeometry geometry) =>
        geometry == LegGeometry.GreatCircle
            ? GreatCircleInterpolate(from, to, fraction)
            : RhumbInterpolate(from, to, fraction);

    /// <summary>Rhumb-line (loxodrome) distance in metres.</summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <returns>The distance in metres.</returns>
    public static double RhumbDistanceMeters(GeoPosition from, GeoPosition to) =>
        Wgs84Geodesy.RhumbDistanceMetres(from, to);

    /// <summary>Great-circle (geodesic) distance in metres.</summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <returns>The distance in metres.</returns>
    public static double GreatCircleDistanceMeters(GeoPosition from, GeoPosition to) =>
        Wgs84Geodesy.GeodesicDistanceMetres(from, to);

    /// <summary>The position a fraction of the way along the rhumb line.</summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <param name="fraction">Fraction of the distance, from 0 to 1.</param>
    /// <returns>The interpolated position.</returns>
    public static GeoPosition RhumbInterpolate(GeoPosition from, GeoPosition to, double fraction) =>
        Wgs84Geodesy.RhumbInterpolate(from, to, fraction);

    /// <summary>The position a fraction of the way along the great circle (geodesic).</summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <param name="fraction">Fraction of the distance, from 0 to 1.</param>
    /// <returns>The interpolated position.</returns>
    public static GeoPosition GreatCircleInterpolate(GeoPosition from, GeoPosition to, double fraction) =>
        Wgs84Geodesy.GeodesicInterpolate(from, to, fraction);
}
