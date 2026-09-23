using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Geometry;

/// <summary>
/// Static spatial predicates shared by every tool that accepts a
/// <see cref="GeoQuery"/>. All predicates operate in planar
/// lat/lon space and treat bounding-box edges as inclusive.
/// </summary>
public static class SpatialPredicates
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="box"/> intersects (or
    /// touches) <paramref name="query"/>'s coarse bounding box.
    /// </summary>
    /// <remarks>
    /// A <see cref="GeoQuery.Polyline"/> is tested segment by segment:
    /// <paramref name="box"/> must touch the bounding box of at least one
    /// segment, inflated by the corridor half-width. Testing against the
    /// envelope of every vertex instead would match anything inside the
    /// route's overall extent — kilometres off a bending track — whatever
    /// the corridor width.
    /// </remarks>
    public static bool Intersects(BoundingBox box, GeoQuery query)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(query);

        var envelope = query.GetBoundingBox();
        if (!Intersects(box, envelope.SouthLatitude, envelope.WestLongitude, envelope.NorthLatitude, envelope.EastLongitude))
        {
            return false;
        }

        if (query is not GeoQuery.Polyline { Value: var polyline } || polyline.Vertices.Count < 3)
        {
            // A single segment's corridor box is the whole envelope.
            return true;
        }

        var vertices = polyline.Vertices;
        for (var i = 0; i < vertices.Count - 1; i++)
        {
            var (a, b) = (vertices[i], vertices[i + 1]);
            var south = Math.Min(a.Latitude, b.Latitude);
            var north = Math.Max(a.Latitude, b.Latitude);
            var (latPad, lonPad) = GeoQuery.CorridorPadDegrees(south, north, polyline.CorridorWidthMeters);
            if (Intersects(
                    box,
                    south - latPad,
                    Math.Min(a.Longitude, b.Longitude) - lonPad,
                    north + latPad,
                    Math.Max(a.Longitude, b.Longitude) + lonPad))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Intersects(BoundingBox box, double south, double west, double north, double east)
        => box.WestLongitude <= east
            && box.EastLongitude >= west
            && box.SouthLatitude <= north
            && box.NorthLatitude >= south;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="box"/> contains every
    /// point of <paramref name="query"/>'s coarse bounding box. Used
    /// by point-style queries where the query bbox collapses to the
    /// point itself.
    /// </summary>
    public static bool Contains(BoundingBox box, GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(point);

        return point.Latitude >= box.SouthLatitude
            && point.Latitude <= box.NorthLatitude
            && point.Longitude >= box.WestLongitude
            && point.Longitude <= box.EastLongitude;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="point"/> lies inside
    /// the polygon <paramref name="ring"/>, using a planar ray-casting
    /// test. Points on the ring boundary are reported as inside.
    /// </summary>
    public static bool ContainsPoint(IReadOnlyList<GeoPoint> ring, GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        if (ring.Count == 0 || ring.Count < 4)
        {
            return false;
        }

        var x = point.Longitude;
        var y = point.Latitude;
        var inside = false;

        for (int i = 0, j = ring.Count - 2; i < ring.Count - 1; j = i++)
        {
            var xi = ring[i].Longitude;
            var yi = ring[i].Latitude;
            var xj = ring[j].Longitude;
            var yj = ring[j].Latitude;

            var intersect = ((yi > y) != (yj > y))
                && (x < (xj - xi) * (y - yi) / ((yj - yi) == 0 ? 1e-12 : (yj - yi)) + xi);
            if (intersect)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
