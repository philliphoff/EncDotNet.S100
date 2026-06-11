using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools.Geometry;

/// <summary>
/// Great-circle distance helpers shared by proximity tools
/// (<see cref="EncDotNet.S100.Mcp.Tools.FindNearestTool"/>). All
/// distances are in metres on the WGS-84 sphere; the haversine formula
/// is used with the mean Earth radius, which is adequate for the
/// bounding-box precision the rest of the catalog tools work at.
/// </summary>
public static class GeoDistance
{
    /// <summary>Mean Earth radius in metres (IUGG mean radius R1).</summary>
    public const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>
    /// Great-circle distance in metres between two WGS-84 points.
    /// </summary>
    public static double HaversineMeters(
        double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = lat1 * Math.PI / 180.0;
        var phi2 = lat2 * Math.PI / 180.0;
        var dPhi = (lat2 - lat1) * Math.PI / 180.0;
        var dLambda = (lon2 - lon1) * Math.PI / 180.0;

        var a = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2)
            + Math.Cos(phi1) * Math.Cos(phi2)
            * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0.0, 1 - a)));
        return EarthRadiusMeters * c;
    }

    /// <summary>
    /// Returns the great-circle distance in metres from
    /// <paramref name="point"/> to the nearest edge of
    /// <paramref name="box"/>, or <c>0</c> when the point lies inside
    /// (or on) the box. The query latitude/longitude are clamped onto
    /// the box and the haversine distance to that clamped point is
    /// returned, matching the bounding-box precision used by the other
    /// spatial tools.
    /// </summary>
    public static double NearestDistanceMeters(BoundingBox box, GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(point);

        var clampedLat = Math.Clamp(point.Latitude, box.SouthLatitude, box.NorthLatitude);
        var clampedLon = Math.Clamp(point.Longitude, box.WestLongitude, box.EastLongitude);
        if (clampedLat == point.Latitude && clampedLon == point.Longitude)
        {
            return 0.0;
        }

        return HaversineMeters(point.Latitude, point.Longitude, clampedLat, clampedLon);
    }
}
