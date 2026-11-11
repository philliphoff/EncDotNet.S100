namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>Where a charted danger is from a route.</summary>
public enum HazardRouteRelation
{
    /// <summary>Inside the cross-track limits.</summary>
    InCorridor,

    /// <summary>Beyond the limits, within the nearby band.</summary>
    Near,

    /// <summary>Further away.</summary>
    Off,
}

/// <summary>Which depth made a danger a danger.</summary>
public enum HazardThreshold
{
    /// <summary>It isn't one: deep enough for every threshold.</summary>
    None,

    /// <summary>Its depth is unknown, or it reaches the surface.</summary>
    NoDepth,

    /// <summary>No deeper than the safety contour (and not the keel threshold).</summary>
    SafetyContour,

    /// <summary>Shallower than draft + squat + required UKC.</summary>
    Keel,
}

/// <summary>A charted danger against a route, as the world model has it: the expected answer for a route check (#955).</summary>
/// <param name="Hazard">The danger.</param>
/// <param name="Relation">Where it is from the route.</param>
/// <param name="Leg">The leg it's closest to.</param>
/// <param name="DistanceMeters">The distance from departure to its foot on the route, in local metres.</param>
/// <param name="OffsetMeters">How far it is from the track, positive to port.</param>
/// <param name="Threshold">Which depth made it a danger.</param>
/// <param name="InFinestCell">
/// Whether the finest cell covering it charts it. One charted only in a
/// coarser cell is hidden by the finer one (#947).
/// </param>
public sealed record RouteHazardTruth(
    PointHazard Hazard,
    HazardRouteRelation Relation,
    int Leg,
    double DistanceMeters,
    double OffsetMeters,
    HazardThreshold Threshold,
    bool InFinestCell)
{
    /// <summary>Whether a route check should report it: a danger, charted in the finest cell, and in or near the corridor.</summary>
    public bool Expected => Threshold != HazardThreshold.None && InFinestCell && Relation != HazardRouteRelation.Off;
}

/// <summary>A stretch of a route's track over water shallower than a depth.</summary>
/// <param name="Leg">The leg.</param>
/// <param name="StartMeters">Distance from departure to its start, in local metres.</param>
/// <param name="EndMeters">Distance from departure to its end.</param>
/// <param name="LeastDepthMeters">The least true depth in it.</param>
public sealed record ShoalStretch(int Leg, double StartMeters, double EndMeters, double LeastDepthMeters);

/// <summary>
/// A scenario's charted dangers and shoals against one of its routes,
/// computed from the definitions and the world in local metres: the truth a
/// route check is tested against.
/// </summary>
/// <remarks>
/// A danger's depth is its <see cref="PointHazard.ValueOfSounding"/>; one
/// without is a danger whatever the thresholds, and one in shallow water
/// counts as well as one in deep water. Its distance is to the nearest point of the route's legs; a danger past a
/// leg's end is measured from the end. Cells are ranked by their minimum
/// display scale, finest first.
/// </remarks>
public static class RouteHazards
{
    /// <summary>Every danger in the scenario, against <paramref name="route"/>.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="route">The route.</param>
    /// <param name="nearbyMeters">How far beyond the cross-track limits counts as near.</param>
    /// <param name="safetyContourMeters">The safety contour.</param>
    /// <param name="keelMeters">Draft + squat + required UKC, or <c>null</c> without a draft.</param>
    /// <returns>One entry per danger, in definition order.</returns>
    public static IReadOnlyList<RouteHazardTruth> For(
        ScenarioDefinition definition,
        S421RouteSpec route,
        double nearbyMeters,
        double safetyContourMeters,
        double? keelMeters)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(route);
        var path = route.Waypoints.Select(name => definition.Places[name]).ToList();
        var limit = S421RouteBuilder.CrossTrackLimitMeters;
        var results = new List<RouteHazardTruth>();
        foreach (var hazard in definition.Hazards)
        {
            var (leg, distance, offset) = Nearest(path, hazard.Position);
            var relation = Math.Abs(offset) <= limit ? HazardRouteRelation.InCorridor
                : Math.Abs(offset) <= limit + nearbyMeters ? HazardRouteRelation.Near
                : HazardRouteRelation.Off;
            var threshold = hazard.ValueOfSounding is not { } depth ? HazardThreshold.NoDepth
                : keelMeters is { } keel && depth < keel ? HazardThreshold.Keel
                : depth <= safetyContourMeters ? HazardThreshold.SafetyContour
                : HazardThreshold.None;
            var finest = definition.Cells
                .Where(c => c.Extent.Contains(hazard.Position.X, hazard.Position.Y))
                .MinBy(c => c.MinimumDisplayScale);
            var inFinest = finest is not null && hazard.ChartedIn(finest);
            results.Add(new RouteHazardTruth(hazard, relation, leg, distance, offset, threshold, inFinest));
        }

        return results;
    }

    /// <summary>
    /// The stretches of a route's track where the true seabed is shallower
    /// than a depth, found by stepping along each leg.
    /// </summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="route">The route.</param>
    /// <param name="depthMeters">The depth.</param>
    /// <param name="stepMeters">The step along each leg.</param>
    /// <returns>The stretches in route order. A stretch over a waypoint is split at it.</returns>
    public static IReadOnlyList<ShoalStretch> ShoalsAlong(ScenarioDefinition definition, S421RouteSpec route, double depthMeters, double stepMeters = 1)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(route);
        var world = definition.World;
        var path = route.Waypoints.Select(name => definition.Places[name]).ToList();
        var result = new List<ShoalStretch>();
        var along = 0.0;
        for (var leg = 0; leg + 1 < path.Count; leg++)
        {
            var (a, b) = (path[leg], path[leg + 1]);
            var length = Length(a, b);
            var steps = Math.Max(1, (int)Math.Ceiling(length / stepMeters));
            double? start = null;
            var least = double.MaxValue;
            for (var i = 0; i <= steps; i++)
            {
                var f = (double)i / steps;
                var depth = world.DepthAt(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
                if (depth < depthMeters)
                {
                    start ??= along + length * f;
                    least = Math.Min(least, depth);
                }
                else if (start is { } s)
                {
                    result.Add(new ShoalStretch(leg, s, along + length * f, least));
                    start = null;
                    least = double.MaxValue;
                }
            }

            if (start is { } open)
            {
                result.Add(new ShoalStretch(leg, open, along + length, least));
            }

            along += length;
        }

        return result;
    }

    // The leg a point is closest to, the distance from departure to its
    // foot, and its offset, positive to port (left of the leg's direction).
    private static (int Leg, double Distance, double Offset) Nearest(List<(double X, double Y)> path, (double X, double Y) point)
    {
        var best = (Leg: 0, Distance: 0.0, Offset: double.MaxValue);
        var along = 0.0;
        for (var leg = 0; leg + 1 < path.Count; leg++)
        {
            var (a, b) = (path[leg], path[leg + 1]);
            var length = Length(a, b);
            var (dx, dy) = ((b.X - a.X) / length, (b.Y - a.Y) / length);
            var u = Math.Clamp((point.X - a.X) * dx + (point.Y - a.Y) * dy, 0, length);
            var (fx, fy) = (a.X + dx * u, a.Y + dy * u);
            var offset = Length((fx, fy), point);
            if (offset < Math.Abs(best.Offset))
            {
                var side = dx * (point.Y - a.Y) - dy * (point.X - a.X);
                best = (leg, along + u, side >= 0 ? offset : -offset);
            }

            along += length;
        }

        return best;
    }

    private static double Length((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
