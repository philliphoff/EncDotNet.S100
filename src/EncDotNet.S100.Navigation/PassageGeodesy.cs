using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// Spherical-earth distances and interpolation along rhumb lines and great
/// circles, used to measure legs and to place samples along them.
/// </summary>
/// <remarks>
/// The earth radius and formulas match the viewer's route metrics, so a leg
/// measured here has the same length the route panel shows. A sphere is
/// accurate to a few tenths of a percent, which is well inside the precision
/// of an entered speed.
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

    // Mercator stretching diverges at the poles; clamp just short of them.
    private const double LatitudeClampDegrees = 89.99;

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
    public static double RhumbDistanceMeters(GeoPosition from, GeoPosition to)
    {
        var phi1 = ClampLatRadians(from.Latitude);
        var phi2 = ClampLatRadians(to.Latitude);
        var dphi = phi2 - phi1;
        var dlam = NormalizeDeltaLon(DegToRad(to.Longitude - from.Longitude));
        var dpsi = StretchedLatitude(phi2) - StretchedLatitude(phi1);
        var q = Math.Abs(dpsi) > 1e-12 ? dphi / dpsi : Math.Cos(phi1);
        return Math.Sqrt(dphi * dphi + q * q * dlam * dlam) * EarthRadiusMeters;
    }

    /// <summary>Great-circle (haversine) distance in metres.</summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <returns>The distance in metres.</returns>
    public static double GreatCircleDistanceMeters(GeoPosition from, GeoPosition to) =>
        CentralAngle(from, to) * EarthRadiusMeters;

    /// <summary>
    /// The position a fraction of the way along the rhumb line. Distance
    /// along a rhumb line is proportional to the change in latitude, and
    /// longitude changes in proportion to the Mercator-stretched latitude.
    /// </summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <param name="fraction">Fraction of the distance, from 0 to 1.</param>
    /// <returns>The interpolated position.</returns>
    public static GeoPosition RhumbInterpolate(GeoPosition from, GeoPosition to, double fraction)
    {
        var phi1 = ClampLatRadians(from.Latitude);
        var phi2 = ClampLatRadians(to.Latitude);
        var dlam = NormalizeDeltaLon(DegToRad(to.Longitude - from.Longitude));
        var psi1 = StretchedLatitude(phi1);
        var dpsi = StretchedLatitude(phi2) - psi1;

        var phi = phi1 + fraction * (phi2 - phi1);
        var lam = Math.Abs(dpsi) > 1e-12
            ? DegToRad(from.Longitude) + (StretchedLatitude(phi) - psi1) / dpsi * dlam
            : DegToRad(from.Longitude) + fraction * dlam;
        return new GeoPosition(RadToDeg(phi), WrapLongitude(RadToDeg(lam)));
    }

    /// <summary>The position a fraction of the way along the great circle (spherical linear interpolation).</summary>
    /// <param name="from">The start position.</param>
    /// <param name="to">The end position.</param>
    /// <param name="fraction">Fraction of the distance, from 0 to 1.</param>
    /// <returns>The interpolated position.</returns>
    public static GeoPosition GreatCircleInterpolate(GeoPosition from, GeoPosition to, double fraction)
    {
        var delta = CentralAngle(from, to);
        if (delta < 1e-12 || fraction <= 0)
        {
            return from;
        }

        if (fraction >= 1)
        {
            return to;
        }

        var phi1 = DegToRad(from.Latitude);
        var lam1 = DegToRad(from.Longitude);
        var phi2 = DegToRad(to.Latitude);
        var lam2 = DegToRad(to.Longitude);
        var sinDelta = Math.Sin(delta);
        var a = Math.Sin((1.0 - fraction) * delta) / sinDelta;
        var b = Math.Sin(fraction * delta) / sinDelta;
        var x = a * Math.Cos(phi1) * Math.Cos(lam1) + b * Math.Cos(phi2) * Math.Cos(lam2);
        var y = a * Math.Cos(phi1) * Math.Sin(lam1) + b * Math.Cos(phi2) * Math.Sin(lam2);
        var z = a * Math.Sin(phi1) + b * Math.Sin(phi2);
        return new GeoPosition(
            RadToDeg(Math.Atan2(z, Math.Sqrt(x * x + y * y))),
            RadToDeg(Math.Atan2(y, x)));
    }

    private static double CentralAngle(GeoPosition from, GeoPosition to)
    {
        var phi1 = DegToRad(from.Latitude);
        var phi2 = DegToRad(to.Latitude);
        var dphi = DegToRad(to.Latitude - from.Latitude);
        var dlam = NormalizeDeltaLon(DegToRad(to.Longitude - from.Longitude));
        var sinDphi = Math.Sin(dphi / 2.0);
        var sinDlam = Math.Sin(dlam / 2.0);
        var h = sinDphi * sinDphi + Math.Cos(phi1) * Math.Cos(phi2) * sinDlam * sinDlam;
        return 2.0 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1.0 - h));
    }

    private static double StretchedLatitude(double phi) => Math.Log(Math.Tan(Math.PI / 4.0 + phi / 2.0));

    private static double DegToRad(double deg) => deg * (Math.PI / 180.0);

    private static double RadToDeg(double rad) => rad * (180.0 / Math.PI);

    private static double ClampLatRadians(double latDeg) =>
        DegToRad(Math.Clamp(latDeg, -LatitudeClampDegrees, LatitudeClampDegrees));

    // Wraps a longitude difference into (-π, π] so a leg always takes the short way round.
    private static double NormalizeDeltaLon(double dlam)
    {
        if (dlam > Math.PI) return dlam - 2.0 * Math.PI;
        if (dlam < -Math.PI) return dlam + 2.0 * Math.PI;
        return dlam;
    }

    private static double WrapLongitude(double lon)
    {
        if (lon > 180.0) return lon - 360.0;
        if (lon < -180.0) return lon + 360.0;
        return lon;
    }
}
