using System.Collections.Immutable;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools.Geometry;

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
    public static bool Intersects(BoundingBox box, GeoQuery query)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(query);

        var q = query.GetBoundingBox();
        return box.WestLongitude <= q.EastLongitude
            && box.EastLongitude >= q.WestLongitude
            && box.SouthLatitude <= q.NorthLatitude
            && box.NorthLatitude >= q.SouthLatitude;
    }

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
    public static bool ContainsPoint(ImmutableArray<GeoPoint> ring, GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        if (ring.IsDefaultOrEmpty || ring.Length < 4)
        {
            return false;
        }

        var x = point.Longitude;
        var y = point.Latitude;
        var inside = false;

        for (int i = 0, j = ring.Length - 2; i < ring.Length - 1; j = i++)
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
