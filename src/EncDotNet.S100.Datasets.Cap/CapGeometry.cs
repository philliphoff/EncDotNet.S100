using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.Cap;

/// <summary>Geometry helpers for CAP areas.</summary>
public static class CapGeometry
{
    /// <summary>The number of vertices a CAP <c>circle</c> is drawn with.</summary>
    public const int CircleVertices = 64;

    // The IUGG mean Earth radius, in kilometres.
    private const double EarthRadiusKilometres = 6371.0088;

    // A circle of radius 0 is a point; it is drawn as a 50 m circle so it
    // shows and can be picked.
    private const double MinimumRadiusKilometres = 0.05;

    /// <summary>
    /// The polygon a CAP <c>circle</c> is drawn as: <see cref="CircleVertices"/>
    /// vertices at the circle's radius from its centre on a spherical Earth,
    /// clockwise from north, closed (the first vertex repeated at the end).
    /// </summary>
    /// <param name="circle">The circle.</param>
    /// <param name="vertices">How many distinct vertices to use; at least 3.</param>
    /// <returns>The closed ring, WGS-84.</returns>
    public static IReadOnlyList<GeoPosition> CircleToPolygon(CapCircle circle, int vertices = CircleVertices)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(vertices, 3);
        var radius = Math.Max(circle.RadiusKilometres, MinimumRadiusKilometres) / EarthRadiusKilometres;
        var lat1 = DegreesToRadians(circle.Centre.Latitude);
        var lon1 = DegreesToRadians(circle.Centre.Longitude);

        var ring = new GeoPosition[vertices + 1];
        for (var i = 0; i < vertices; i++)
        {
            var bearing = 2 * Math.PI * i / vertices;
            var lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(radius) + Math.Cos(lat1) * Math.Sin(radius) * Math.Cos(bearing));
            var lon2 = lon1 + Math.Atan2(
                Math.Sin(bearing) * Math.Sin(radius) * Math.Cos(lat1),
                Math.Cos(radius) - Math.Sin(lat1) * Math.Sin(lat2));
            var longitude = RadiansToDegrees(lon2);
            longitude = ((longitude + 540) % 360) - 180;
            ring[i] = new GeoPosition(RadiansToDegrees(lat2), longitude);
        }

        ring[vertices] = ring[0];
        return ring;
    }

    /// <summary>The great-circle distance between two positions on a spherical Earth, in kilometres.</summary>
    /// <param name="a">One position.</param>
    /// <param name="b">The other.</param>
    public static double DistanceKilometres(GeoPosition a, GeoPosition b)
    {
        var lat1 = DegreesToRadians(a.Latitude);
        var lat2 = DegreesToRadians(b.Latitude);
        var dLat = lat2 - lat1;
        var dLon = DegreesToRadians(b.Longitude - a.Longitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKilometres * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private static double RadiansToDegrees(double radians) => radians * 180 / Math.PI;
}
