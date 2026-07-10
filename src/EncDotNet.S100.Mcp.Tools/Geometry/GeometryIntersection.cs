using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Mcp.Tools.Geometry;

/// <summary>
/// True (full-geometry) intersection between a <see cref="GeoQuery"/> and
/// an <see cref="IS100Feature"/>'s geometry — the precise complement to the
/// bounding-box test in
/// <see cref="EncDotNet.S100.Mcp.Tools.Spec.FeatureGeometryQuery.Intersects"/>.
/// </summary>
/// <remarks>
/// <para>
/// Where the bounding-box test answers "could these overlap?", this answers
/// "do they actually overlap?" — point-in-polygon containment for area
/// features (interior-ring holes honoured) and genuine segment crossing for
/// the route-leg question "which features does this leg cross?". All maths
/// is planar in lat/lon space, matching the precision used elsewhere in the
/// tools surface; it is intended for ranking and membership tests over the
/// span of a single dataset, not survey-grade geodesy.
/// </para>
/// <para>
/// The predicate is conservative: for the genuinely degenerate combination
/// it cannot decide (a point query against a curve or point feature) it
/// returns <c>true</c> so a precise pass never drops a bounding-box match it
/// cannot prove disjoint. Surface geometry takes precedence over curve, which
/// takes precedence over point, matching the rest of the tools surface.
/// </para>
/// </remarks>
public static class GeometryIntersection
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="feature"/>'s geometry truly
    /// intersects <paramref name="query"/>. Features without geometry never
    /// match.
    /// </summary>
    public static bool Intersects(IS100Feature feature, GeoQuery query)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(query);

        if (feature.ExteriorRing.Count > 0)
        {
            return SurfaceIntersects(feature.ExteriorRing, feature.InteriorRings, query);
        }

        if (feature.Curves.Count > 0)
        {
            return CurveIntersects(feature.Curves, query);
        }

        if (feature.Points.Count > 0)
        {
            return PointFeatureIntersects(feature.Points, query);
        }

        return false;
    }

    private static bool SurfaceIntersects(
        IReadOnlyList<GeoPosition> ring,
        IReadOnlyList<IReadOnlyList<GeoPosition>> holes,
        GeoQuery query)
    {
        switch (query)
        {
            case GeoQuery.Point p:
                return ContainsArea(ring, holes, p.Value);

            case GeoQuery.Box or GeoQuery.Polygon:
            {
                var qring = QueryRing(query);
                // A query-ring vertex inside the feature area.
                foreach (var v in qring)
                {
                    if (ContainsArea(ring, holes, new GeoPoint(v.Latitude, v.Longitude)))
                    {
                        return true;
                    }
                }

                // A feature-ring vertex inside the (hole-less) query ring.
                foreach (var v in ring)
                {
                    if (GeometryDistance.ContainsPoint(qring, new GeoPoint(v.Latitude, v.Longitude)))
                    {
                        return true;
                    }
                }

                // Boundary crossing (exterior ring or any hole edge).
                return RingsCross(ring, qring) || HolesCross(holes, qring);
            }

            case GeoQuery.Polyline pl:
            {
                var segments = Segments(pl.Value.Vertices);

                // A leg endpoint inside the feature area.
                foreach (var v in pl.Value.Vertices)
                {
                    if (ContainsArea(ring, holes, new GeoPoint(v.Latitude, v.Longitude)))
                    {
                        return true;
                    }
                }

                // A leg crossing the exterior ring or any hole edge.
                if (SegmentsCrossRing(segments, ring) || HolesCrossSegments(holes, segments))
                {
                    return true;
                }

                return WithinCorridor(pl.Value, ring, holes);
            }

            default:
                return true;
        }
    }

    private static bool CurveIntersects(
        IReadOnlyList<IReadOnlyList<GeoPosition>> curves,
        GeoQuery query)
    {
        switch (query)
        {
            case GeoQuery.Point:
                // Point-on-curve is degenerate; keep the bounding-box decision.
                return true;

            case GeoQuery.Box or GeoQuery.Polygon:
            {
                var qring = QueryRing(query);
                foreach (var curve in curves)
                {
                    foreach (var v in curve)
                    {
                        if (GeometryDistance.ContainsPoint(qring, new GeoPoint(v.Latitude, v.Longitude)))
                        {
                            return true;
                        }
                    }

                    if (SegmentsCrossRing(Segments(curve), qring))
                    {
                        return true;
                    }
                }

                return false;
            }

            case GeoQuery.Polyline pl:
            {
                var legs = Segments(pl.Value.Vertices);
                foreach (var curve in curves)
                {
                    if (SegmentsCross(Segments(curve), legs))
                    {
                        return true;
                    }
                }

                return CurvesWithinCorridor(curves, pl.Value);
            }

            default:
                return true;
        }
    }

    private static bool PointFeatureIntersects(
        IReadOnlyList<GeoPosition> points,
        GeoQuery query)
    {
        switch (query)
        {
            case GeoQuery.Box or GeoQuery.Polygon:
            {
                var qring = QueryRing(query);
                foreach (var p in points)
                {
                    if (GeometryDistance.ContainsPoint(qring, new GeoPoint(p.Latitude, p.Longitude)))
                    {
                        return true;
                    }
                }

                return false;
            }

            case GeoQuery.Polyline pl:
            {
                // A point feature lies on a route leg only when its distance
                // to the leg is within the corridor half-width (a tiny
                // epsilon for a zero-width leg, i.e. genuinely on the line).
                var half = pl.Value.CorridorWidthMeters is { } w && w > 0 ? w : 1e-6;
                var legs = Segments(pl.Value.Vertices);
                foreach (var p in points)
                {
                    var gp = new GeoPoint(p.Latitude, p.Longitude);
                    foreach (var (a, b) in legs)
                    {
                        if (GeometryDistance.PointToSegment(gp, a, b).Distance <= half)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            default:
                // Point query against a point feature is degenerate, so keep
                // the bounding-box decision.
                return true;
        }
    }

    private static bool ContainsArea(
        IReadOnlyList<GeoPosition> ring,
        IReadOnlyList<IReadOnlyList<GeoPosition>> holes,
        GeoPoint point)
    {
        if (!GeometryDistance.ContainsPoint(ring, point))
        {
            return false;
        }

        if (holes.Count > 0)
        {
            foreach (var hole in holes)
            {
                if (GeometryDistance.ContainsPoint(hole, point))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool WithinCorridor(
        GeoPolyline polyline,
        IReadOnlyList<GeoPosition> ring,
        IReadOnlyList<IReadOnlyList<GeoPosition>> holes)
    {
        if (polyline.CorridorWidthMeters is not { } half || half <= 0)
        {
            return false;
        }

        var legs = Segments(polyline.Vertices);

        // Any ring vertex within half-width of the route, or any route
        // vertex within half-width of the ring.
        if (AnyVertexWithin(ring, legs, half))
        {
            return true;
        }

        foreach (var v in polyline.Vertices)
        {
            if (MinDistanceToRing(new GeoPoint(v.Latitude, v.Longitude), ring) <= half)
            {
                return true;
            }
        }

        if (holes.Count > 0)
        {
            foreach (var hole in holes)
            {
                if (AnyVertexWithin(hole, legs, half))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CurvesWithinCorridor(
        IReadOnlyList<IReadOnlyList<GeoPosition>> curves,
        GeoPolyline polyline)
    {
        if (polyline.CorridorWidthMeters is not { } half || half <= 0)
        {
            return false;
        }

        var legs = Segments(polyline.Vertices);
        foreach (var curve in curves)
        {
            if (AnyVertexWithin(curve, legs, half))
            {
                return true;
            }

            var curveSegments = Segments(curve);
            foreach (var v in polyline.Vertices)
            {
                var gp = new GeoPoint(v.Latitude, v.Longitude);
                foreach (var (a, b) in curveSegments)
                {
                    if (GeometryDistance.PointToSegment(gp, a, b).Distance <= half)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool AnyVertexWithin(
        IReadOnlyList<GeoPosition> vertices,
        IReadOnlyList<(GeoPosition A, GeoPosition B)> segments,
        double half)
    {
        foreach (var v in vertices)
        {
            var gp = new GeoPoint(v.Latitude, v.Longitude);
            foreach (var (a, b) in segments)
            {
                if (GeometryDistance.PointToSegment(gp, a, b).Distance <= half)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static double MinDistanceToRing(
        GeoPoint point,
        IReadOnlyList<GeoPosition> ring)
    {
        var best = double.PositiveInfinity;
        for (var i = 0; i < ring.Count - 1; i++)
        {
            var d = GeometryDistance.PointToSegment(point, ring[i], ring[i + 1]).Distance;
            if (d < best)
            {
                best = d;
            }
        }

        return best;
    }

    private static IReadOnlyList<GeoPosition> QueryRing(GeoQuery query) => query switch
    {
        GeoQuery.Box b =>
        [
            new GeoPosition(b.Value.SouthLatitude, b.Value.WestLongitude),
            new GeoPosition(b.Value.SouthLatitude, b.Value.EastLongitude),
            new GeoPosition(b.Value.NorthLatitude, b.Value.EastLongitude),
            new GeoPosition(b.Value.NorthLatitude, b.Value.WestLongitude),
            new GeoPosition(b.Value.SouthLatitude, b.Value.WestLongitude),
        ],
        GeoQuery.Polygon p => p.Value.Ring
            .Select(v => new GeoPosition(v.Latitude, v.Longitude))
            .ToArray(),
        _ => [],
    };

    private static IReadOnlyList<(GeoPosition A, GeoPosition B)> Segments(
        IReadOnlyList<GeoPoint> vertices)
    {
        if (vertices.Count == 0 || vertices.Count < 2)
        {
            return [];
        }

        var builder = new List<(GeoPosition, GeoPosition)>(vertices.Count - 1);
        for (var i = 0; i < vertices.Count - 1; i++)
        {
            builder.Add((
                new GeoPosition(vertices[i].Latitude, vertices[i].Longitude),
                new GeoPosition(vertices[i + 1].Latitude, vertices[i + 1].Longitude)));
        }

        return builder;
    }

    private static IReadOnlyList<(GeoPosition A, GeoPosition B)> Segments(
        IReadOnlyList<GeoPosition> vertices)
    {
        if (vertices.Count == 0 || vertices.Count < 2)
        {
            return [];
        }

        var builder = new List<(GeoPosition, GeoPosition)>(vertices.Count - 1);
        for (var i = 0; i < vertices.Count - 1; i++)
        {
            builder.Add((vertices[i], vertices[i + 1]));
        }

        return builder;
    }

    private static bool RingsCross(
        IReadOnlyList<GeoPosition> ringA,
        IReadOnlyList<GeoPosition> ringB)
        => SegmentsCross(Segments(ringA), Segments(ringB));

    private static bool HolesCross(
        IReadOnlyList<IReadOnlyList<GeoPosition>> holes,
        IReadOnlyList<GeoPosition> ringB)
    {
        if (holes.Count == 0)
        {
            return false;
        }

        foreach (var hole in holes)
        {
            if (RingsCross(hole, ringB))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentsCrossRing(
        IReadOnlyList<(GeoPosition A, GeoPosition B)> segments,
        IReadOnlyList<GeoPosition> ring)
        => SegmentsCross(segments, Segments(ring));

    private static bool HolesCrossSegments(
        IReadOnlyList<IReadOnlyList<GeoPosition>> holes,
        IReadOnlyList<(GeoPosition A, GeoPosition B)> segments)
    {
        if (holes.Count == 0)
        {
            return false;
        }

        foreach (var hole in holes)
        {
            if (SegmentsCrossRing(segments, hole))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentsCross(
        IReadOnlyList<(GeoPosition A, GeoPosition B)> a,
        IReadOnlyList<(GeoPosition A, GeoPosition B)> b)
    {
        foreach (var (a1, a2) in a)
        {
            foreach (var (b1, b2) in b)
            {
                if (SegmentsIntersect(a1, a2, b1, b2))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(
        GeoPosition p1,
        GeoPosition p2,
        GeoPosition p3,
        GeoPosition p4)
    {
        var d1 = Orient(p3, p4, p1);
        var d2 = Orient(p3, p4, p2);
        var d3 = Orient(p1, p2, p3);
        var d4 = Orient(p1, p2, p4);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
        {
            return true;
        }

        if (d1 == 0 && OnSegment(p3, p4, p1)) return true;
        if (d2 == 0 && OnSegment(p3, p4, p2)) return true;
        if (d3 == 0 && OnSegment(p1, p2, p3)) return true;
        if (d4 == 0 && OnSegment(p1, p2, p4)) return true;

        return false;
    }

    // Cross product of (b - a) and (c - a), using longitude as x and
    // latitude as y.
    private static double Orient(
        GeoPosition a,
        GeoPosition b,
        GeoPosition c)
        => (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude)
            - (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);

    private static bool OnSegment(
        GeoPosition a,
        GeoPosition b,
        GeoPosition p)
        => Math.Min(a.Longitude, b.Longitude) <= p.Longitude
            && p.Longitude <= Math.Max(a.Longitude, b.Longitude)
            && Math.Min(a.Latitude, b.Latitude) <= p.Latitude
            && p.Latitude <= Math.Max(a.Latitude, b.Latitude);
}
