using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// Leg durations, waypoint ETAs and the arrival for a <see cref="Route"/>,
/// computed by the Navigation library's <see cref="PassageSchedule"/> so the
/// UKC check (#900) uses the same times.
/// </summary>
/// <remarks>
/// <para>
/// ETAs run from the route's departure, or from the view time when no
/// departure is set (handoff decision 2), at each leg's effective speed over
/// ground. S-111 currents aren't applied (decision 4).
/// </para>
/// <para>
/// A leg's duration needs only its own speed. ETAs need every earlier leg's
/// speed too, so they stop at the first leg with no speed:
/// <see cref="FirstLegWithoutSpeed"/> says where. A route with no speed at all
/// has no durations or ETAs.
/// </para>
/// </remarks>
internal sealed class RouteEtas
{
    private RouteEtas(PassageSchedule schedule, bool isFromViewTime)
    {
        Schedule = schedule;
        IsFromViewTime = isFromViewTime;

        var legs = schedule.Legs;
        HasEtas = legs.Count > 0 && legs[0].End is not null;
        if (legs.Any(l => l.Duration is not null))
        {
            var first = legs.FirstOrDefault(l => l.Duration is null);
            FirstLegWithoutSpeed = first?.LegIndex;
        }
    }

    /// <summary>The schedule the times come from.</summary>
    public PassageSchedule Schedule { get; }

    /// <summary>The time the ETAs run from: the departure, or the view time.</summary>
    public DateTimeOffset Departure => Schedule.Departure;

    /// <summary>True when the route has no departure, so the ETAs move with the view time.</summary>
    public bool IsFromViewTime { get; }

    /// <summary>True when at least the first leg has an ETA (the first leg has a speed).</summary>
    public bool HasEtas { get; }

    /// <summary>
    /// The first leg with no speed, where the ETAs stop, when some other leg
    /// has one; <c>null</c> when every leg has a speed or none does.
    /// </summary>
    public int? FirstLegWithoutSpeed { get; }

    /// <summary>The arrival at the last waypoint, or <c>null</c> when a leg has no speed.</summary>
    public DateTimeOffset? Arrival => HasEtas ? Schedule.Arrival : null;

    /// <summary>Time under way, or <c>null</c> when a leg has no speed.</summary>
    public TimeSpan? Duration => HasEtas ? Schedule.Duration : null;

    /// <summary>Time to run leg <paramref name="legIndex"/>, or <c>null</c> when it has no speed.</summary>
    public TimeSpan? LegDuration(int legIndex) => Schedule.Legs[legIndex].Duration;

    /// <summary>
    /// The ETA at waypoint <paramref name="waypointIndex"/>: the departure for
    /// the first one, else the end of the leg into it; <c>null</c> when unknown.
    /// </summary>
    public DateTimeOffset? WaypointEta(int waypointIndex)
    {
        if (!HasEtas)
            return null;
        return waypointIndex == 0 ? Departure : Schedule.Legs[waypointIndex - 1].End;
    }

    /// <summary>
    /// Computes the times for <paramref name="route"/>, leaving at its
    /// departure or, when it has none, at <paramref name="viewTime"/>.
    /// </summary>
    public static RouteEtas Compute(Route route, DateTimeOffset viewTime)
    {
        ArgumentNullException.ThrowIfNull(route);
        var departure = route.Passage.Departure;
        return new RouteEtas(
            PassageSchedule.Compute(ToPassageLegs(route), departure ?? viewTime),
            isFromViewTime: departure is null);
    }

    /// <summary>
    /// The route's legs as Navigation <see cref="PassageLeg"/>s, with every
    /// passage value resolved to the leg's effective one: draft (none when
    /// unset), speed, the route's squat, required UKC, and the cross-track
    /// limits. Legs are named "1 → 2".
    /// </summary>
    public static IReadOnlyList<PassageLeg> ToPassageLegs(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var legs = new PassageLeg[route.Legs.Count];
        for (var i = 0; i < legs.Length; i++)
        {
            var speed = route.GetEffectiveLegValue(i, RoutePassageField.Speed);
            legs[i] = new PassageLeg
            {
                Points = [route.Waypoints[i].Position, route.Waypoints[i + 1].Position],
                Geometry = route.Legs[i].GeometryType == RouteLegGeometryType.Geodesic
                    ? LegGeometry.GreatCircle
                    : LegGeometry.Rhumb,
                PortLimit = Metres(route.GetEffectiveLegValue(i, RoutePassageField.PortCrossTrack)),
                StarboardLimit = Metres(route.GetEffectiveLegValue(i, RoutePassageField.StarboardCrossTrack)),
                Speed = speed > 0 ? Speed.FromKnots(speed.Value) : null,
                Draft = DepthMetres(route.GetEffectiveLegValue(i, RoutePassageField.Draft)),
                Squat = Depth.FromMetres(route.Passage.SquatMeters ?? 0),
                RequiredUkc = DepthMetres(route.GetEffectiveLegValue(i, RoutePassageField.RequiredUkc)),
                Name = $"{i + 1} → {i + 2}",
            };
        }
        return legs;

        static Length? Metres(double? metres) => metres is { } m ? Length.FromMetres(m) : null;
        static Depth? DepthMetres(double? metres) => metres is { } m ? Depth.FromMetres(m) : null;
    }
}
