using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// Planar tests in longitude/latitude for the route check's short
/// cross-sections and track segments. Fractions along a segment are the same
/// in any affine frame, so they need no projection over the distances the
/// check works at.
/// </summary>
internal static class PlanarGeometry
{
    /// <summary>
    /// Where segment a–b crosses segment p–q, as a fraction along a–b, or
    /// <c>null</c>. Parallel and collinear segments don't cross. A crossing
    /// within rounding of either end of a–b counts, clamped to it, so a
    /// boundary through a station is found on both sides of it; callers
    /// test the stretches between crossings rather than count them.
    /// </summary>
    public static double? Intersect(GeoPosition a, GeoPosition b, GeoPosition p, GeoPosition q)
    {
        var rx = b.Longitude - a.Longitude;
        var ry = b.Latitude - a.Latitude;
        var sx = q.Longitude - p.Longitude;
        var sy = q.Latitude - p.Latitude;
        var denominator = rx * sy - ry * sx;
        if (denominator == 0)
        {
            return null;
        }

        var qx = p.Longitude - a.Longitude;
        var qy = p.Latitude - a.Latitude;
        var u = (qx * sy - qy * sx) / denominator;
        var t = (qx * ry - qy * rx) / denominator;
        const double epsilon = 1e-9;
        return u is >= -epsilon and <= 1 + epsilon && t is >= -epsilon and <= 1 + epsilon ? Math.Clamp(u, 0, 1) : null;
    }

    /// <summary>Whether a point lies in any of the surfaces: inside its exterior ring and outside its holes.</summary>
    public static bool Contains(IReadOnlyList<SurfacePart> surfaces, GeoPosition point)
    {
        foreach (var surface in surfaces)
        {
            if (!InRing(surface.ExteriorRing, point))
            {
                continue;
            }

            var inHole = false;
            foreach (var hole in surface.InteriorRings)
            {
                if (InRing(hole, point))
                {
                    inHole = true;
                    break;
                }
            }

            if (!inHole)
            {
                return true;
            }
        }

        return false;
    }

    // Even-odd ray casting.
    private static bool InRing(IReadOnlyList<GeoPosition> ring, GeoPosition point)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Latitude > point.Latitude) != (b.Latitude > point.Latitude)
                && point.Longitude < (b.Longitude - a.Longitude) * (point.Latitude - a.Latitude) / (b.Latitude - a.Latitude) + a.Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
