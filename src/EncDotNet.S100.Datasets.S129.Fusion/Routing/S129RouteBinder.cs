using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Datasets.S421.DataModel;

namespace EncDotNet.S100.Datasets.S129.Fusion.Routing;

/// <summary>
/// Correlates the control points of an
/// <see cref="S129UnderKeelClearancePlan"/> with the waypoints and legs
/// of an S-421 <see cref="S421Route"/> via spatial proximity.
/// </summary>
/// <remarks>
/// <para>
/// S-129 Edition 2.0.0 does not carry explicit waypoint cross-references
/// from control points back to the source S-421 route; the binder is
/// purely geometric.
/// </para>
/// <para>
/// For each control point, the binder first tests every waypoint and
/// keeps the closest within
/// <see cref="S129RouteBindingOptions.WaypointToleranceMeters"/>. If no
/// waypoint is close enough, it tests every leg's polyline (constructed
/// from the leg's <see cref="S421Leg.Coordinates"/> if present, falling
/// back to the leg's start/end waypoint positions) and keeps the
/// closest within
/// <see cref="S129RouteBindingOptions.LegToleranceMeters"/>. If neither
/// passes, the control point is recorded as <see cref="S129RouteMappingKind.Unmapped"/>.
/// </para>
/// <para>
/// Distances are computed using the haversine great-circle formula
/// (Earth radius 6371008.8 m, the WGS-84 mean radius). At S-129
/// chart-plan scales this is accurate to the small fractions of a
/// metre that matter for tolerance comparisons.
/// </para>
/// </remarks>
public static class S129RouteBinder
{
    private const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>
    /// Correlates the supplied plan's control points with the supplied
    /// route. Pure data accessor; never throws for missing or sparse
    /// inputs.
    /// </summary>
    /// <param name="plan">The S-129 UKC plan.</param>
    /// <param name="route">The S-421 route.</param>
    /// <param name="options">
    /// Tolerance parameters; defaults to
    /// <see cref="S129RouteBindingOptions.Default"/>.
    /// </param>
    public static S129RouteBinding Bind(
        S129UnderKeelClearancePlan plan,
        S421Route route,
        S129RouteBindingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(route);

        var opts = options ?? S129RouteBindingOptions.Default;
        var mappings = ImmutableArray.CreateBuilder<(S129ControlPoint, S129ControlPointRouteMapping)>(plan.ControlPoints.Length);

        foreach (var cp in plan.ControlPoints)
        {
            mappings.Add((cp, MapOne(cp, route, opts)));
        }

        return new S129RouteBinding(plan, route, mappings.ToImmutable());
    }

    private static S129ControlPointRouteMapping MapOne(
        S129ControlPoint cp,
        S421Route route,
        S129RouteBindingOptions opts)
    {
        if (cp.Position is not { } cpPos) return S129ControlPointRouteMapping.Unmapped;

        // Waypoint pass.
        S421Waypoint? bestWp = null;
        double bestWpDistance = double.PositiveInfinity;
        foreach (var wp in route.Waypoints)
        {
            double d = Haversine(cpPos, wp.Position);
            if (d < bestWpDistance)
            {
                bestWpDistance = d;
                bestWp = wp;
            }
        }

        if (bestWp is not null && bestWpDistance <= opts.WaypointToleranceMeters)
            return S129ControlPointRouteMapping.OnWaypoint(bestWp, bestWpDistance);

        // Leg pass.
        S421Leg? bestLeg = null;
        double bestLegDistance = double.PositiveInfinity;
        double bestLegFraction = 0;
        foreach (var leg in route.Legs)
        {
            var polyline = BuildLegPolyline(leg);
            if (polyline.Count < 2) continue;

            var (distance, fraction) = NearestPointOnPolyline(cpPos, polyline);
            if (distance < bestLegDistance)
            {
                bestLegDistance = distance;
                bestLegFraction = fraction;
                bestLeg = leg;
            }
        }

        if (bestLeg is not null && bestLegDistance <= opts.LegToleranceMeters)
            return S129ControlPointRouteMapping.OnLeg(bestLeg, bestLegFraction, bestLegDistance);

        return S129ControlPointRouteMapping.Unmapped;
    }

    private static IReadOnlyList<GeoPosition> BuildLegPolyline(S421Leg leg)
    {
        if (!leg.Coordinates.IsDefaultOrEmpty && leg.Coordinates.Length >= 2)
            return leg.Coordinates;

        if (leg.StartWaypoint is not null && leg.EndWaypoint is not null)
            return [leg.StartWaypoint.Position, leg.EndWaypoint.Position];

        return Array.Empty<GeoPosition>();
    }

    /// <summary>
    /// Returns the minimum distance (m) from <paramref name="point"/> to
    /// the polyline plus the fractional position (0..1) along the
    /// polyline's total arc length where the projection lands.
    /// Approximates each segment as planar on the equirectangular plane
    /// scaled to the segment's mean latitude — accurate to well under a
    /// metre across an S-129-sized chart.
    /// </summary>
    private static (double Distance, double Fraction) NearestPointOnPolyline(
        GeoPosition point,
        IReadOnlyList<GeoPosition> polyline)
    {
        double bestDistance = double.PositiveInfinity;
        double bestArcSoFar = 0;
        double accumulated = 0;
        var segmentLengths = new double[polyline.Count - 1];
        double total = 0;
        for (int i = 0; i < polyline.Count - 1; i++)
        {
            segmentLengths[i] = Haversine(polyline[i], polyline[i + 1]);
            total += segmentLengths[i];
        }
        if (total == 0)
        {
            // Degenerate polyline — fall back to the first vertex.
            return (Haversine(point, polyline[0]), 0);
        }

        for (int i = 0; i < polyline.Count - 1; i++)
        {
            var a = polyline[i];
            var b = polyline[i + 1];
            var (segDistance, segFraction) = NearestPointOnSegment(point, a, b);
            if (segDistance < bestDistance)
            {
                bestDistance = segDistance;
                bestArcSoFar = accumulated + segFraction * segmentLengths[i];
            }
            accumulated += segmentLengths[i];
        }

        return (bestDistance, bestArcSoFar / total);
    }

    private static (double Distance, double Fraction) NearestPointOnSegment(
        GeoPosition point, GeoPosition a, GeoPosition b)
    {
        // Equirectangular projection around the segment's mean latitude.
        double midLat = (a.Latitude + b.Latitude) * 0.5;
        double cosMidLat = Math.Cos(midLat * Math.PI / 180.0);
        const double metersPerDegreeLat = Math.PI * EarthRadiusMeters / 180.0;

        double ax = a.Longitude * cosMidLat * metersPerDegreeLat;
        double ay = a.Latitude * metersPerDegreeLat;
        double bx = b.Longitude * cosMidLat * metersPerDegreeLat;
        double by = b.Latitude * metersPerDegreeLat;
        double px = point.Longitude * cosMidLat * metersPerDegreeLat;
        double py = point.Latitude * metersPerDegreeLat;

        double dx = bx - ax;
        double dy = by - ay;
        double lenSq = dx * dx + dy * dy;
        double t = lenSq > 0 ? ((px - ax) * dx + (py - ay) * dy) / lenSq : 0;
        if (t < 0) t = 0;
        else if (t > 1) t = 1;

        double closestX = ax + t * dx;
        double closestY = ay + t * dy;
        double distance = Math.Sqrt((px - closestX) * (px - closestX) + (py - closestY) * (py - closestY));
        return (distance, t);
    }

    private static double Haversine(GeoPosition a, GeoPosition b)
    {
        double lat1 = a.Latitude * Math.PI / 180.0;
        double lat2 = b.Latitude * Math.PI / 180.0;
        double dLat = (b.Latitude - a.Latitude) * Math.PI / 180.0;
        double dLon = (b.Longitude - a.Longitude) * Math.PI / 180.0;

        double s = Math.Sin(dLat / 2);
        double c = Math.Sin(dLon / 2);
        double h = s * s + Math.Cos(lat1) * Math.Cos(lat2) * c * c;
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
