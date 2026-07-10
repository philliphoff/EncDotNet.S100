using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Mcp.Tools.Geometry;

/// <summary>
/// Precise (not bounding-box) planar geometry helpers shared by the
/// distance- and containment-ranking tools (<c>nearest_features</c>, the
/// precise mode of <c>query_features</c>).
/// </summary>
/// <remarks>
/// <para>
/// All distances are computed with a fast local equirectangular
/// projection: latitude differences scale by a constant
/// <see cref="MetersPerDegreeLatitude"/> and longitude differences scale
/// by the same constant times the cosine of the mean latitude. This is
/// accurate to a fraction of a percent over the spans of a single S-100
/// dataset and avoids pulling in a full geodesy library, matching the
/// approximation already used by <see cref="EncDotNet.S100.Mcp.Tools.IdentifyFeaturesTool"/>.
/// </para>
/// <para>
/// Unlike the vertex-only approximation in that tool, the helpers here
/// measure distance to the nearest <em>point on a segment</em> (not just
/// to vertices) and treat an area feature's distance as zero when the
/// query point lies inside its exterior ring (interior-ring holes
/// honoured).
/// </para>
/// </remarks>
public static class GeometryDistance
{
    /// <summary>Metres per degree of latitude (WGS-84 mean).</summary>
    public const double MetersPerDegreeLatitude = 111_320.0;

    /// <summary>
    /// The result of measuring a query point against a feature's geometry.
    /// </summary>
    /// <param name="DistanceMeters">
    /// Distance from the query point to the nearest point of the feature's
    /// geometry, in metres. Zero when the point lies inside an area
    /// feature.
    /// </param>
    /// <param name="Inside">
    /// <c>true</c> when the point lies within an area feature's exterior
    /// ring (and outside any interior-ring hole).
    /// </param>
    /// <param name="Primitive">
    /// The geometry primitive the distance was measured against
    /// (<see cref="S100GeometryType.Point"/>, <see cref="S100GeometryType.Curve"/>,
    /// or <see cref="S100GeometryType.Surface"/>).
    /// </param>
    /// <param name="NearestLatitude">Latitude of the nearest point on the feature (the point itself when <see cref="Inside"/>).</param>
    /// <param name="NearestLongitude">Longitude of the nearest point on the feature (the point itself when <see cref="Inside"/>).</param>
    public readonly record struct FeatureDistance(
        double DistanceMeters,
        bool Inside,
        S100GeometryType Primitive,
        double NearestLatitude,
        double NearestLongitude);

    /// <summary>
    /// Measures <paramref name="point"/> against <paramref name="feature"/>'s
    /// geometry, returning <c>null</c> when the feature carries no geometry.
    /// </summary>
    /// <remarks>
    /// Surface geometry takes precedence over curve, which takes precedence
    /// over point — matching the preference order used elsewhere in the
    /// tools surface.
    /// </remarks>
    public static FeatureDistance? Measure(IS100Feature feature, GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(point);

        if (feature.ExteriorRing.Count > 0)
        {
            return MeasureSurface(feature, point);
        }

        if (feature.Curves.Count > 0)
        {
            var (dist, lat, lon) = NearestOnCurves(feature.Curves, point);
            if (!double.IsPositiveInfinity(dist))
            {
                return new FeatureDistance(dist, false, S100GeometryType.Curve, lat, lon);
            }
        }

        if (feature.Points.Count > 0)
        {
            var best = double.PositiveInfinity;
            double bLat = 0, bLon = 0;
            foreach (var (lat, lon) in feature.Points)
            {
                var d = Meters(point, lat, lon);
                if (d < best)
                {
                    best = d;
                    bLat = lat;
                    bLon = lon;
                }
            }

            if (!double.IsPositiveInfinity(best))
            {
                return new FeatureDistance(best, false, S100GeometryType.Point, bLat, bLon);
            }
        }

        return null;
    }

    private static FeatureDistance MeasureSurface(IS100Feature feature, GeoPoint point)
    {
        var inside = ContainsPoint(feature.ExteriorRing, point);
        if (inside && feature.InteriorRings.Count > 0)
        {
            foreach (var hole in feature.InteriorRings)
            {
                if (ContainsPoint(hole, point))
                {
                    inside = false;
                    break;
                }
            }
        }

        if (inside)
        {
            return new FeatureDistance(0.0, true, S100GeometryType.Surface, point.Latitude, point.Longitude);
        }

        // Outside (or inside a hole): distance to the nearest ring edge.
        var (dist, lat, lon) = NearestOnRing(feature.ExteriorRing, point);
        if (feature.InteriorRings.Count > 0)
        {
            foreach (var hole in feature.InteriorRings)
            {
                var (hd, hlat, hlon) = NearestOnRing(hole, point);
                if (hd < dist)
                {
                    dist = hd;
                    lat = hlat;
                    lon = hlon;
                }
            }
        }

        return new FeatureDistance(dist, false, S100GeometryType.Surface, lat, lon);
    }

    private static (double Distance, double Lat, double Lon) NearestOnCurves(
        IReadOnlyList<IReadOnlyList<GeoPosition>> curves,
        GeoPoint point)
    {
        var best = double.PositiveInfinity;
        double bLat = 0, bLon = 0;

        foreach (var curve in curves)
        {
            if (curve.Count == 0)
            {
                continue;
            }

            if (curve.Count == 1)
            {
                var d = Meters(point, curve[0].Latitude, curve[0].Longitude);
                if (d < best)
                {
                    best = d;
                    bLat = curve[0].Latitude;
                    bLon = curve[0].Longitude;
                }
                continue;
            }

            for (var i = 0; i < curve.Count - 1; i++)
            {
                var (d, lat, lon) = PointToSegment(point, curve[i], curve[i + 1]);
                if (d < best)
                {
                    best = d;
                    bLat = lat;
                    bLon = lon;
                }
            }
        }

        return (best, bLat, bLon);
    }

    private static (double Distance, double Lat, double Lon) NearestOnRing(
        IReadOnlyList<GeoPosition> ring,
        GeoPoint point)
    {
        var best = double.PositiveInfinity;
        double bLat = 0, bLon = 0;

        if (ring.Count == 0)
        {
            return (best, bLat, bLon);
        }

        for (var i = 0; i < ring.Count - 1; i++)
        {
            var (d, lat, lon) = PointToSegment(point, ring[i], ring[i + 1]);
            if (d < best)
            {
                best = d;
                bLat = lat;
                bLon = lon;
            }
        }

        return (best, bLat, bLon);
    }

    /// <summary>
    /// Returns the distance in metres from <paramref name="point"/> to the
    /// nearest point on the segment <paramref name="a"/>–<paramref name="b"/>,
    /// together with that nearest point. The projection is performed in a
    /// local metric frame centred on the query point so the result is a
    /// true perpendicular distance, not merely the nearer endpoint.
    /// </summary>
    public static (double Distance, double Lat, double Lon) PointToSegment(
        GeoPoint point,
        GeoPosition a,
        GeoPosition b)
    {
        var cosLat = Math.Cos(point.Latitude * Math.PI / 180.0);

        // Local metric coordinates relative to the query point.
        double ToX(double lon) => (lon - point.Longitude) * MetersPerDegreeLatitude * cosLat;
        double ToY(double lat) => (lat - point.Latitude) * MetersPerDegreeLatitude;

        var ax = ToX(a.Longitude);
        var ay = ToY(a.Latitude);
        var bx = ToX(b.Longitude);
        var by = ToY(b.Latitude);

        var dx = bx - ax;
        var dy = by - ay;
        var lenSq = dx * dx + dy * dy;

        double nx, ny;
        if (lenSq <= 1e-12)
        {
            nx = ax;
            ny = ay;
        }
        else
        {
            // Project the origin (the query point) onto the segment.
            var t = -(ax * dx + ay * dy) / lenSq;
            t = Math.Clamp(t, 0.0, 1.0);
            nx = ax + t * dx;
            ny = ay + t * dy;
        }

        var distance = Math.Sqrt(nx * nx + ny * ny);
        var nearestLon = point.Longitude + (cosLat > 1e-9 ? nx / (MetersPerDegreeLatitude * cosLat) : 0.0);
        var nearestLat = point.Latitude + ny / MetersPerDegreeLatitude;
        return (distance, nearestLat, nearestLon);
    }

    /// <summary>
    /// Returns the great-circle initial bearing (degrees true, <c>[0, 360)</c>)
    /// from <paramref name="from"/> toward the point (<paramref name="toLat"/>, <paramref name="toLon"/>).
    /// </summary>
    public static double Bearing(GeoPoint from, double toLat, double toLon)
    {
        var lat1 = from.Latitude * Math.PI / 180.0;
        var lat2 = toLat * Math.PI / 180.0;
        var dLon = (toLon - from.Longitude) * Math.PI / 180.0;

        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        var bearing = Math.Atan2(y, x) * 180.0 / Math.PI;
        return (bearing + 360.0) % 360.0;
    }

    /// <summary>
    /// Distance in metres between two points (equirectangular).
    /// </summary>
    public static double Meters(GeoPoint a, double lat, double lon)
    {
        var dLat = (a.Latitude - lat) * MetersPerDegreeLatitude;
        var dLon = (a.Longitude - lon) * MetersPerDegreeLatitude
            * Math.Cos((a.Latitude + lat) * 0.5 * Math.PI / 180.0);
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }

    /// <summary>
    /// Planar ray-cast point-in-ring test. A point on the boundary is
    /// reported as inside. Rings shorter than four points are treated as
    /// empty.
    /// </summary>
    public static bool ContainsPoint(IReadOnlyList<GeoPosition> ring, GeoPoint point)
    {
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
