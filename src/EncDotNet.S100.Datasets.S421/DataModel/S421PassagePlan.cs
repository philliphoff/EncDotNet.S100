using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.S421.DataModel;

/// <summary>Converts an <see cref="S421Route"/> to the <see cref="PassagePlan"/> the route checks take.</summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Leg <c>i</c> runs from waypoint <c>i</c> to waypoint <c>i + 1</c> and reads
/// waypoint <c>i</c>'s outgoing <c>RouteWaypointLeg</c>
/// (<see cref="S421Waypoint.OutgoingLeg"/>): its draft, static UKC, port and
/// starboard cross-track distance limits (metres) and geometry type
/// (2, geodesic, is a great circle; anything else a rhumb line).
/// </description></item>
/// <item><description>
/// Speeds and the departure come from the planning schedule variant
/// (<see cref="S421Route.GetPlanningSchedule"/>). A leg's speed is its start
/// waypoint's planned SOG, when positive; the departure is the variant's
/// first element's ETD.
/// </description></item>
/// </list>
/// S-421 has no squat allowance, so every leg's squat is zero.
/// </remarks>
public static class S421PassagePlan
{
    // S-421 routeWaypointLegGeometryType: 1 = loxodrome, 2 = geodesic.
    private const int GeodesicGeometryType = 2;

    /// <summary>The route as a passage plan.</summary>
    /// <param name="route">The route.</param>
    /// <returns>The plan: one leg per pair of consecutive waypoints (none with fewer than two), named "Leg 1 → 2" and so on.</returns>
    public static PassagePlan ToPassagePlan(this S421Route route)
    {
        ArgumentNullException.ThrowIfNull(route);

        var elements = route.GetPlanningSchedule()?.Variant.Elements ?? [];
        var legs = new List<PassageLeg>(Math.Max(0, route.Waypoints.Count - 1));
        for (var i = 0; i + 1 < route.Waypoints.Count; i++)
        {
            var leg = route.Waypoints[i].OutgoingLeg;
            var number = route.Waypoints[i].WaypointNumber;
            var speed = elements.FirstOrDefault(e => e.WaypointNumber == number)?.PlannedSpeedOverGround;
            legs.Add(new PassageLeg
            {
                Points = [route.Waypoints[i].Position, route.Waypoints[i + 1].Position],
                Geometry = leg?.GeometryTypeCode == GeodesicGeometryType ? LegGeometry.GreatCircle : LegGeometry.Rhumb,
                PortLimit = leg?.PortCrossTrackDistanceLimit is { } port ? Length.FromMetres(port) : null,
                StarboardLimit = leg?.StarboardCrossTrackDistanceLimit is { } starboard ? Length.FromMetres(starboard) : null,
                Speed = speed is { } knots && knots > 0 ? Speed.FromKnots(knots) : null,
                Draft = leg?.Draft is { } draft ? Depth.FromMetres(draft) : null,
                RequiredUkc = leg?.StaticUnderKeelClearance is { } ukc ? Depth.FromMetres(ukc) : null,
                Name = $"Leg {i + 1} → {i + 2}",
            });
        }

        return new PassagePlan(legs, elements.Count > 0 ? elements[0].Etd : null);
    }
}
