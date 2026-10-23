using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Routes.Rtz;

/// <summary>Converts an <see cref="RtzRoute"/> to the <see cref="PassagePlan"/> the route checks take.</summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Leg <c>i</c> runs from waypoint <c>i</c> to waypoint <c>i + 1</c> and reads
/// the <c>&lt;leg&gt;</c> of waypoint <c>i + 1</c> (in RTZ a waypoint's leg is
/// the leg <em>to</em> it), with the default waypoint's values filled in
/// (<see cref="RtzRoute.GetEffectiveLeg"/>).
/// </description></item>
/// <item><description>
/// Cross-track limits are nautical miles in RTZ; the draft is the deeper of
/// the forward and aft draughts (<see cref="RtzLeg.MaxDraught"/>); an
/// orthodrome leg is a great circle, anything else a rhumb line.
/// </description></item>
/// <item><description>
/// Speeds and the departure come from the planning schedule
/// (<see cref="RtzRoute.GetPlanningSchedule"/>). A leg's speed is on the
/// element of the waypoint it leads to; without one it's implied by that
/// element's ETA and the previous element's departure, rounded to 0.01 kn.
/// The plan's departure is the first waypoint's (<see cref="RtzScheduleElement.Departure"/>:
/// its ETD, else its ETA plus its stay).
/// </description></item>
/// </list>
/// RTZ has no squat allowance, so every leg's squat is zero.
/// </remarks>
public static class RtzPassagePlan
{
    /// <summary>The route as a passage plan.</summary>
    /// <param name="route">The route.</param>
    /// <returns>The plan: one leg per pair of consecutive waypoints (none with fewer than two), named "Leg 1 → 2" and so on.</returns>
    public static PassagePlan ToPassagePlan(this RtzRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        var schedule = route.GetPlanningSchedule();
        var legs = new List<PassageLeg>(Math.Max(0, route.Waypoints.Count - 1));
        for (var i = 0; i + 1 < route.Waypoints.Count; i++)
        {
            var leg = route.GetEffectiveLeg(i + 1);
            var geometry = leg?.GeometryType == RtzGeometryType.Orthodrome ? LegGeometry.GreatCircle : LegGeometry.Rhumb;
            legs.Add(new PassageLeg
            {
                Points = [route.Waypoints[i].Position, route.Waypoints[i + 1].Position],
                Geometry = geometry,
                PortLimit = leg?.PortsideXtd is { } port ? Length.FromNauticalMiles(port) : null,
                StarboardLimit = leg?.StarboardXtd is { } starboard ? Length.FromNauticalMiles(starboard) : null,
                Speed = schedule is not null && LegSpeedKnots(route, schedule, i, geometry) is { } knots ? Speed.FromKnots(knots) : null,
                Draft = leg?.MaxDraught is { } draft ? Depth.FromMetres(draft) : null,
                RequiredUkc = leg?.StaticUkc is { } ukc ? Depth.FromMetres(ukc) : null,
                Name = $"Leg {i + 1} → {i + 2}",
            });
        }

        var departure = route.Waypoints.Count > 0 ? schedule?.ElementFor(route.Waypoints[0].Id)?.Departure : null;
        return new PassagePlan(legs, departure);
    }

    private static double? LegSpeedKnots(RtzRoute route, RtzSchedule schedule, int legIndex, LegGeometry geometry)
    {
        var to = schedule.ElementFor(route.Waypoints[legIndex + 1].Id);
        if (to?.Speed is { } speed)
            return speed;
        if (schedule.ElementFor(route.Waypoints[legIndex].Id)?.Departure is not { } leave || to?.Eta is not { } arrive || arrive <= leave)
            return null;
        var distanceNm = PassageGeodesy.DistanceMeters(route.Waypoints[legIndex].Position, route.Waypoints[legIndex + 1].Position, geometry)
            / PassageGeodesy.MetersPerNauticalMile;
        return Math.Round(distanceNm / (arrive - leave).TotalHours, 2);
    }
}
