namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Great-circle geometry helpers used by the Vessels panel to compute
/// the range and bearing from the own ship to each AIS target. Uses a
/// spherical-earth model (the haversine formula), which is accurate to
/// well within a metre per nautical mile at the ranges AIS targets are
/// listed — far finer than the panel displays.
/// </summary>
internal static class VesselGeoMath
{
    /// <summary>Mean earth radius in metres (WGS-84 authalic sphere).</summary>
    internal const double EarthRadiusMetres = 6_371_008.8;

    /// <summary>Metres in one nautical mile.</summary>
    internal const double MetresPerNauticalMile = 1852.0;

    /// <summary>
    /// Great-circle distance in metres between two WGS-84 points.
    /// </summary>
    public static double DistanceMetres(
        double latitude1, double longitude1,
        double latitude2, double longitude2)
    {
        var phi1 = DegreesToRadians(latitude1);
        var phi2 = DegreesToRadians(latitude2);
        var dPhi = DegreesToRadians(latitude2 - latitude1);
        var dLambda = DegreesToRadians(longitude2 - longitude1);

        var sinDPhi = Math.Sin(dPhi / 2.0);
        var sinDLambda = Math.Sin(dLambda / 2.0);
        var a = (sinDPhi * sinDPhi)
            + (Math.Cos(phi1) * Math.Cos(phi2) * sinDLambda * sinDLambda);
        var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        return EarthRadiusMetres * c;
    }

    /// <summary>
    /// Initial great-circle bearing (forward azimuth) in degrees true,
    /// normalised to <c>[0, 360)</c>, from point 1 toward point 2.
    /// </summary>
    public static double InitialBearingDegrees(
        double latitude1, double longitude1,
        double latitude2, double longitude2)
    {
        var phi1 = DegreesToRadians(latitude1);
        var phi2 = DegreesToRadians(latitude2);
        var dLambda = DegreesToRadians(longitude2 - longitude1);

        var y = Math.Sin(dLambda) * Math.Cos(phi2);
        var x = (Math.Cos(phi1) * Math.Sin(phi2))
            - (Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLambda));
        var theta = Math.Atan2(y, x);
        var degrees = RadiansToDegrees(theta);
        return ((degrees % 360.0) + 360.0) % 360.0;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}
