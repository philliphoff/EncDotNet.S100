using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>How a leg runs between its points.</summary>
public enum LegGeometry
{
    /// <summary>A rhumb line (loxodrome): constant bearing.</summary>
    Rhumb,

    /// <summary>A great circle (orthodrome): shortest path on the sphere.</summary>
    GreatCircle,
}

/// <summary>
/// One leg of a passage with the vessel values that apply on it. Callers
/// resolve inheritance (route values, per-leg overrides) before building
/// a leg, so every value here is the effective one.
/// </summary>
/// <param name="Points">
/// The leg's positions in order: at least two (its waypoints), or more when
/// the caller has already densified the leg.
/// </param>
/// <param name="DraftMeters">The vessel's draft on this leg, in metres.</param>
/// <param name="SpeedKnots">
/// Speed over ground on this leg in knots, or <c>null</c> when unknown. The
/// entered speed is the SOG; S-111 currents are not applied (handoff
/// decision 4). Without a speed, the leg and every later leg have no ETAs.
/// </param>
/// <param name="SquatMeters">
/// A fixed squat allowance in metres (handoff decision 3: squat is not
/// modelled from speed). Zero when no allowance is set.
/// </param>
/// <param name="RequiredUkcMeters">
/// The required under-keel clearance in metres, or <c>null</c> when unset;
/// an unset value checks against 0 m and the result says no margin is set.
/// </param>
/// <param name="Geometry">Whether the leg is a rhumb line or a great circle.</param>
/// <param name="Name">An optional display name for the leg, such as "Leg 6 → 7".</param>
public sealed record PassageLeg(
    IReadOnlyList<GeoPosition> Points,
    double DraftMeters,
    double? SpeedKnots = null,
    double SquatMeters = 0,
    double? RequiredUkcMeters = null,
    LegGeometry Geometry = LegGeometry.Rhumb,
    string? Name = null);

/// <summary>A passage to check: its legs in order and the departure time.</summary>
/// <param name="Legs">The legs in route order. Each leg starts where the previous one ends.</param>
/// <param name="Departure">The departure time from the first point.</param>
public sealed record PassagePlan(IReadOnlyList<PassageLeg> Legs, DateTimeOffset Departure);
