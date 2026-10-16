namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>How a warning's geometry relates to a route.</summary>
public enum WarningRouteRelation
{
    /// <summary>The route passes through the warning's line or area.</summary>
    Crosses,

    /// <summary>The route passes within <see cref="RouteWarnings.NearDistanceMeters"/> of it.</summary>
    Near,

    /// <summary>Neither.</summary>
    Off,
}

/// <summary>A warning against a route, as the world model has it: the expected answer for a route check (#888).</summary>
/// <param name="WarningNumber">The warning's number.</param>
/// <param name="Relation">How it relates to the route.</param>
/// <param name="StartDistanceMeters">Where the route first meets it (or passes closest), metres from departure; <c>NaN</c> when off the route.</param>
/// <param name="EndDistanceMeters">Where the route leaves it; equal to the start for a line or point.</param>
/// <param name="StartTime">When the vessel reaches <paramref name="StartDistanceMeters"/> (UTC); <c>null</c> when off the route.</param>
/// <param name="EndTime">When it reaches <paramref name="EndDistanceMeters"/> (UTC).</param>
/// <param name="InForce">Whether the warning is in force at any time while the vessel is there.</param>
public sealed record RouteWarningTruth(
    int WarningNumber,
    WarningRouteRelation Relation,
    double StartDistanceMeters,
    double EndDistanceMeters,
    DateTime? StartTime,
    DateTime? EndTime,
    bool InForce);

/// <summary>
/// Which of a scenario's S-124 warnings cross or come near one of its
/// routes, where, and whether they're in force while the vessel is there:
/// computed from the definitions in the world's local frame, as the truth
/// a route check is tested against.
/// </summary>
/// <remarks>
/// <para>
/// A warning is in force over the vessel's time there when that time
/// overlaps its validity (<c>fixedDateRange</c>), the vessel arrives
/// before its cancellation date (Edition 2.0.0 only; S-124 Ed 2.0.0
/// §9.2.1), and no warning that cancels it was published by then. Edition
/// 1.0.0 has neither cancellation dates nor publication times, so there a
/// cancelled warning is never in force and a cancellation date is ignored.
/// </para>
/// <para>
/// Areas must be convex. Distances along the route are in the world's
/// local metres.
/// </para>
/// </remarks>
public static class RouteWarnings
{
    /// <summary>How close counts as near: the routes' 100 m cross-track limits.</summary>
    public const double NearDistanceMeters = 100;

    /// <summary>Every warning in the scenario, against <paramref name="route"/>.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="route">The route.</param>
    /// <returns>One entry per warning, in definition order.</returns>
    public static IReadOnlyList<RouteWarningTruth> For(ScenarioDefinition definition, S421RouteSpec route)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(route);
        var path = route.Waypoints.Select(name => definition.Places[name]).ToList();
        var results = new List<RouteWarningTruth>();
        foreach (var warning in definition.Warnings)
        {
            var (relation, start, end) = Relate(path, warning.Shape.Kind, warning.Shape.Points);
            if (relation == WarningRouteRelation.Off)
            {
                results.Add(new RouteWarningTruth(warning.WarningNumber, relation, double.NaN, double.NaN, null, null, InForce: false));
                continue;
            }

            var startTime = S421RouteBuilder.TimeAt(route, start);
            var endTime = S421RouteBuilder.TimeAt(route, end);
            results.Add(new RouteWarningTruth(warning.WarningNumber, relation, start, end, startTime, endTime, InForce(definition, warning, startTime, endTime)));
        }

        return results;
    }

    /// <summary>
    /// How a shape relates to a route, and the stretch of route involved:
    /// the span inside an area, the crossing point of a line, or the closest
    /// point to a near shape.
    /// </summary>
    /// <param name="route">The route's points, in local metres.</param>
    /// <param name="kind">The shape's kind.</param>
    /// <param name="shape">The shape's points (an area's corners in order, not closed).</param>
    /// <returns>The relation and the start and end distances along the route.</returns>
    public static (WarningRouteRelation Relation, double Start, double End) Relate(
        IReadOnlyList<(double X, double Y)> route,
        WarningGeometryKind kind,
        IReadOnlyList<(double X, double Y)> shape)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(shape);
        double along = 0;
        double? first = null, last = null;
        var closest = (Distance: double.MaxValue, Along: 0.0);
        for (var i = 1; i < route.Count; i++)
        {
            var a = route[i - 1];
            var b = route[i];
            var length = Distance(a, b);
            switch (kind)
            {
                case WarningGeometryKind.Area:
                    if (ClipToConvex(a, b, shape) is { } span)
                    {
                        first ??= along + span.T0 * length;
                        last = along + span.T1 * length;
                    }

                    break;
                case WarningGeometryKind.Line:
                    for (var j = 1; j < shape.Count; j++)
                    {
                        if (Intersect(a, b, shape[j - 1], shape[j]) is { } t)
                        {
                            first ??= along + t * length;
                            last = along + t * length;
                        }
                    }

                    break;
            }

            foreach (var p in shape)
            {
                var (d, t) = PointToSegment(p, a, b);
                if (d < closest.Distance)
                {
                    closest = (d, along + t * length);
                }
            }

            along += length;
        }

        if (first is { } s && last is { } e)
        {
            return (WarningRouteRelation.Crosses, s, e);
        }

        return closest.Distance <= NearDistanceMeters
            ? (WarningRouteRelation.Near, closest.Along, closest.Along)
            : (WarningRouteRelation.Off, double.NaN, double.NaN);
    }

    private static bool InForce(ScenarioDefinition definition, S124WarningSpec warning, DateTime start, DateTime end)
    {
        if (warning.ValidFrom is { } from && from > end)
        {
            return false;
        }

        if (warning.ValidTo is { } to && to < start)
        {
            return false;
        }

        var edition2 = definition.S124Edition == 2;
        if (edition2 && warning.CancellationDate is { } cancelled && cancelled <= start)
        {
            return false;
        }

        return !definition.Warnings.Any(w => w.Cancels.Contains(warning.WarningNumber) && (!edition2 || w.Published <= start));
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    private static (double Distance, double T) PointToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return (Distance(p, (a.X + t * dx, a.Y + t * dy)), t);
    }

    // The parameter along a→b where it crosses c→d, or null.
    private static double? Intersect((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        var r = (X: b.X - a.X, Y: b.Y - a.Y);
        var s = (X: d.X - c.X, Y: d.Y - c.Y);
        var denominator = r.X * s.Y - r.Y * s.X;
        if (Math.Abs(denominator) < 1e-12)
        {
            return null;
        }

        var t = ((c.X - a.X) * s.Y - (c.Y - a.Y) * s.X) / denominator;
        var u = ((c.X - a.X) * r.Y - (c.Y - a.Y) * r.X) / denominator;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1 ? t : null;
    }

    // Cyrus–Beck: the parameter span of a→b inside a convex polygon.
    private static (double T0, double T1)? ClipToConvex((double X, double Y) a, (double X, double Y) b, IReadOnlyList<(double X, double Y)> polygon)
    {
        double t0 = 0, t1 = 1;
        var d = (X: b.X - a.X, Y: b.Y - a.Y);
        var orientation = Math.Sign(SignedArea(polygon));
        for (var i = 0; i < polygon.Count; i++)
        {
            var p = polygon[i];
            var q = polygon[(i + 1) % polygon.Count];

            // Inward normal for the polygon's winding.
            var normal = (X: -(q.Y - p.Y) * orientation, Y: (q.X - p.X) * orientation);
            var numerator = normal.X * (a.X - p.X) + normal.Y * (a.Y - p.Y);
            var denominator = normal.X * d.X + normal.Y * d.Y;
            if (Math.Abs(denominator) < 1e-12)
            {
                if (numerator < 0)
                {
                    return null;
                }

                continue;
            }

            var t = -numerator / denominator;
            if (denominator > 0)
            {
                t0 = Math.Max(t0, t);
            }
            else
            {
                t1 = Math.Min(t1, t);
            }

            if (t0 > t1)
            {
                return null;
            }
        }

        return (t0, t1);
    }

    private static double SignedArea(IReadOnlyList<(double X, double Y)> polygon)
    {
        double area = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var p = polygon[i];
            var q = polygon[(i + 1) % polygon.Count];
            area += p.X * q.Y - q.X * p.Y;
        }

        return area / 2;
    }
}
