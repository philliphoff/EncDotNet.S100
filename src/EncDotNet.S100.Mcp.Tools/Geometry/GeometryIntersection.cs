using System.Collections.Immutable;
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

        if (!feature.ExteriorRing.IsDefaultOrEmpty)
        {
            return SurfaceIntersects(feature.ExteriorRing, feature.InteriorRings, query);
        }

        if (!feature.Curves.IsDefaultOrEmpty && feature.Curves.Length > 0)
        {
            return CurveIntersects(feature.Curves, query);
        }

        if (!feature.Points.IsDefaultOrEmpty)
        {
            return PointFeatureIntersects(feature.Points, query);
        }

        return false;
    }

    private static bool SurfaceIntersects(
        ImmutableArray<(double Latitude, double Longitude)> ring,
        ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> holes,
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
        ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> curves,
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
        ImmutableArray<(double Latitude, double Longitude)> points,
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
        ImmutableArray<(double Latitude, double Longitude)> ring,
        ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> holes,
        GeoPoint point)
    {
        if (!GeometryDistance.ContainsPoint(ring, point))
        {
            return false;
        }

        if (!holes.IsDefaultOrEmpty)
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
        ImmutableArray<(double Latitude, double Longitude)> ring,
        ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> holes)
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

        if (!holes.IsDefaultOrEmpty)
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
        ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> curves,
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
        ImmutableArray<(double Latitude, double Longitude)> vertices,
        ImmutableArray<((double Latitude, double Longitude) A, (double Latitude, double Longitude) B)> segments,
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
        ImmutableArray<(double Latitude, double Longitude)> ring)
    {
        var best = double.PositiveInfinity;
        for (var i = 0; i < ring.Length - 1; i++)
        {
            var d = GeometryDistance.PointToSegment(point, ring[i], ring[i + 1]).Distance;
            if (d < best)
            {
                best = d;
            }
        }

        return best;
    }

    private static ImmutableArray<(double Latitude, double Longitude)> QueryRing(GeoQuery query) => query switch
    {
        GeoQuery.Box b => ImmutableArray.Create<(double, double)>(
            (b.Value.SouthLatitude, b.Value.WestLongitude),
            (b.Value.SouthLatitude, b.Value.EastLongitude),
            (b.Value.NorthLatitude, b.Value.EastLongitude),
            (b.Value.NorthLatitude, b.Value.WestLongitude),
            (b.Value.SouthLatitude, b.Value.WestLongitude)),
        GeoQuery.Polygon p => p.Value.Ring
            .Select(v => (v.Latitude, v.Longitude))
            .ToImmutableArray(),
        _ => ImmutableArray<(double, double)>.Empty,
    };

    private static ImmutableArray<((double Latitude, double Longitude) A, (double Latitude, double Longitude) B)> Segments(
        ImmutableArray<GeoPoint> vertices)
    {
        if (vertices.IsDefaultOrEmpty || vertices.Length < 2)
        {
            return ImmutableArray<((double, double), (double, double))>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<((double, double), (double, double))>(vertices.Length - 1);
        for (var i = 0; i < vertices.Length - 1; i++)
        {
            builder.Add((
                (vertices[i].Latitude, vertices[i].Longitude),
                (vertices[i + 1].Latitude, vertices[i + 1].Longitude)));
        }

        return builder.MoveToImmutable();
    }

    private static ImmutableArray<((double Latitude, double Longitude) A, (double Latitude, double Longitude) B)> Segments(
        ImmutableArray<(double Latitude, double Longitude)> vertices)
    {
        if (vertices.IsDefaultOrEmpty || vertices.Length < 2)
        {
            return ImmutableArray<((double, double), (double, double))>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<((double, double), (double, double))>(vertices.Length - 1);
        for (var i = 0; i < vertices.Length - 1; i++)
        {
            builder.Add((vertices[i], vertices[i + 1]));
        }

        return builder.MoveToImmutable();
    }

    private static bool RingsCross(
        ImmutableArray<(double Latitude, double Longitude)> ringA,
        ImmutableArray<(double Latitude, double Longitude)> ringB)
        => SegmentsCross(Segments(ringA), Segments(ringB));

    private static bool HolesCross(
        ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> holes,
        ImmutableArray<(double Latitude, double Longitude)> ringB)
    {
        if (holes.IsDefaultOrEmpty)
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
        ImmutableArray<((double Latitude, double Longitude) A, (double Latitude, double Longitude) B)> segments,
        ImmutableArray<(double Latitude, double Longitude)> ring)
        => SegmentsCross(segments, Segments(ring));

    private static bool HolesCrossSegments(
        ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> holes,
        ImmutableArray<((double Latitude, double Longitude) A, (double Latitude, double Longitude) B)> segments)
    {
        if (holes.IsDefaultOrEmpty)
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
        ImmutableArray<((double Latitude, double Longitude) A, (double Latitude, double Longitude) B)> a,
        ImmutableArray<((double Latitude, double Longitude) A, (double Latitude, double Longitude) B)> b)
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
        (double Latitude, double Longitude) p1,
        (double Latitude, double Longitude) p2,
        (double Latitude, double Longitude) p3,
        (double Latitude, double Longitude) p4)
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
        (double Latitude, double Longitude) a,
        (double Latitude, double Longitude) b,
        (double Latitude, double Longitude) c)
        => (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude)
            - (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);

    private static bool OnSegment(
        (double Latitude, double Longitude) a,
        (double Latitude, double Longitude) b,
        (double Latitude, double Longitude) p)
        => Math.Min(a.Longitude, b.Longitude) <= p.Longitude
            && p.Longitude <= Math.Max(a.Longitude, b.Longitude)
            && Math.Min(a.Latitude, b.Latitude) <= p.Latitude
            && p.Latitude <= Math.Max(a.Latitude, b.Latitude);
}
