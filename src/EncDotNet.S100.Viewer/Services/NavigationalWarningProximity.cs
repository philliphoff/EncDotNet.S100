using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// How far a warning's geometry is from own ship or a route, for the NEW
/// distance filter (#997, S-98 §12.12.1 item 4).
/// </summary>
/// <remarks>
/// Distances are measured on a local equirectangular plane around each
/// pair compared, in nautical miles (1′ of latitude). That is accurate to
/// well under a percent at the tens of miles the filter is set to, which is
/// all a show-or-hide threshold needs. A position inside an area is 0 NM.
/// </remarks>
internal static class NavigationalWarningProximity
{
    /// <summary>The least distance in NM from <paramref name="shape"/> to <paramref name="point"/>, or <c>null</c> when the shape has no coordinates.</summary>
    public static double? DistanceNm(S100FeatureGeometry shape, GeoPosition point)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape.ExteriorRing.Count >= 3 && Inside(shape.ExteriorRing, point))
            return 0;
        double? best = null;
        foreach (var segment in Segments(shape))
            best = Min(best, PointSegmentNm(point, segment.A, segment.B));
        return best;
    }

    /// <summary>The least distance in NM from <paramref name="shape"/> to the polyline <paramref name="route"/>, or <c>null</c> when either has no coordinates.</summary>
    public static double? DistanceNm(S100FeatureGeometry shape, IReadOnlyList<GeoPosition> route)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(route);
        if (route.Count == 0)
            return null;
        if (shape.ExteriorRing.Count >= 3 && route.Any(p => Inside(shape.ExteriorRing, p)))
            return 0;
        var legs = route.Count == 1
            ? [(route[0], route[0])]
            : Enumerable.Range(0, route.Count - 1).Select(i => (route[i], route[i + 1])).ToList();
        double? best = null;
        foreach (var segment in Segments(shape))
        {
            foreach (var (a, b) in legs)
                best = Min(best, SegmentSegmentNm(segment.A, segment.B, a, b));
        }

        return best;
    }

    private static double? Min(double? a, double b) => a is { } x ? Math.Min(x, b) : b;

    /// <summary>Every point as a zero-length segment, and every edge of every curve and ring.</summary>
    private static IEnumerable<(GeoPosition A, GeoPosition B)> Segments(S100FeatureGeometry shape)
    {
        foreach (var p in shape.Points)
            yield return (p, p);
        foreach (var line in shape.Curves.Append(shape.ExteriorRing))
        {
            if (line.Count == 1)
                yield return (line[0], line[0]);
            for (var i = 1; i < line.Count; i++)
                yield return (line[i - 1], line[i]);
        }
    }

    /// <summary>Plane coordinates in NM of <paramref name="p"/> around <paramref name="origin"/>; longitudes wrap at the antimeridian.</summary>
    private static (double X, double Y) Local(GeoPosition p, GeoPosition origin)
    {
        var dLon = p.Longitude - origin.Longitude;
        dLon -= 360 * Math.Round(dLon / 360);
        return (dLon * 60 * Math.Cos(origin.Latitude * Math.PI / 180), (p.Latitude - origin.Latitude) * 60);
    }

    private static double PointSegmentNm(GeoPosition p, GeoPosition a, GeoPosition b)
    {
        var (bx, by) = Local(b, a);
        var (px, py) = Local(p, a);
        return PlanePointSegment(px, py, 0, 0, bx, by);
    }

    private static double SegmentSegmentNm(GeoPosition a1, GeoPosition a2, GeoPosition b1, GeoPosition b2)
    {
        var (ax, ay) = Local(a2, a1);
        var (cx, cy) = Local(b1, a1);
        var (dx, dy) = Local(b2, a1);
        if (Crosses(0, 0, ax, ay, cx, cy, dx, dy))
            return 0;
        return Math.Min(
            Math.Min(PlanePointSegment(0, 0, cx, cy, dx, dy), PlanePointSegment(ax, ay, cx, cy, dx, dy)),
            Math.Min(PlanePointSegment(cx, cy, 0, 0, ax, ay), PlanePointSegment(dx, dy, 0, 0, ax, ay)));
    }

    private static double PlanePointSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
        var x = ax + t * dx - px;
        var y = ay + t * dy - py;
        return Math.Sqrt(x * x + y * y);
    }

    private static bool Crosses(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        static double Cross(double ox, double oy, double px, double py, double qx, double qy) =>
            (px - ox) * (qy - oy) - (py - oy) * (qx - ox);
        var d1 = Cross(cx, cy, dx, dy, ax, ay);
        var d2 = Cross(cx, cy, dx, dy, bx, by);
        var d3 = Cross(ax, ay, bx, by, cx, cy);
        var d4 = Cross(ax, ay, bx, by, dx, dy);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    /// <summary>Even-odd test of <paramref name="p"/> against <paramref name="ring"/>, on the plane around <paramref name="p"/>.</summary>
    private static bool Inside(IReadOnlyList<GeoPosition> ring, GeoPosition p)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (xi, yi) = Local(ring[i], p);
            var (xj, yj) = Local(ring[j], p);
            if ((yi > 0) != (yj > 0) && 0 < (xj - xi) * (0 - yi) / (yj - yi) + xi)
                inside = !inside;
        }

        return inside;
    }
}
