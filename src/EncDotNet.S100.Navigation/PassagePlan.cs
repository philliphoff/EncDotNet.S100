using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

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
/// One leg of a passage: its track, its cross-track limits, and the vessel
/// values that apply on it. Every check reads its legs from here. Callers
/// resolve inheritance (route values, per-leg overrides) before building a
/// leg, so every value here is the effective one.
/// </summary>
public sealed class PassageLeg
{
    /// <summary>
    /// The leg's positions in order: at least two (its waypoints), or more
    /// when the caller has already densified the leg.
    /// </summary>
    public required IReadOnlyList<GeoPosition> Points { get; init; }

    /// <summary>Whether the leg is a rhumb line or a great circle.</summary>
    public LegGeometry Geometry { get; init; } = LegGeometry.Rhumb;

    /// <summary>
    /// How far the corridor reaches to port of the track (S-421
    /// <c>routeWaypointLegPortXTDL</c>; RTZ <c>portsideXTD</c>), or
    /// <c>null</c> for the check's default half-width. This is one side only,
    /// not the corridor's full width.
    /// </summary>
    public Length? PortLimit { get; init; }

    /// <summary>
    /// How far the corridor reaches to starboard of the track, or
    /// <c>null</c> for the check's default half-width.
    /// </summary>
    public Length? StarboardLimit { get; init; }

    /// <summary>
    /// Speed over ground on this leg, or <c>null</c> when unknown. S-111
    /// currents aren't applied. Without a speed, the leg and every later leg
    /// have no ETAs.
    /// </summary>
    public Speed? Speed { get; init; }

    /// <summary>
    /// The vessel's draft on this leg, or <c>null</c> when it has none. The
    /// clearance check needs a draft on every leg; the chart check judges a
    /// leg's dangers against its keel (draft + squat + required UKC) when it
    /// has one.
    /// </summary>
    public Depth? Draft { get; init; }

    /// <summary>A fixed squat allowance. It isn't modelled from speed. Zero when no allowance is set.</summary>
    public Depth Squat { get; init; }

    /// <summary>
    /// The required under-keel clearance, or <c>null</c> when unset; an
    /// unset value checks against 0 m, and the clearance result says no
    /// margin is set.
    /// </summary>
    public Depth? RequiredUkc { get; init; }

    /// <summary>An optional display name for the leg, such as "Leg 6 → 7".</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Draft + squat + required UKC in metres: the depth the keel needs on
    /// this leg, or <c>null</c> without a draft.
    /// </summary>
    internal double? KeelMetres => Draft is { } draft
        ? draft.TotalMetres + Squat.TotalMetres + (RequiredUkc?.TotalMetres ?? 0)
        : null;

    /// <summary>The speed in metres per second, or <c>null</c> when there's no positive speed.</summary>
    internal double? MetresPerSecond => Speed is { } speed && speed.TotalMetresPerSecond > 0 ? speed.TotalMetresPerSecond : null;
}

/// <summary>A passage to check: its legs in order and, when known, the departure time.</summary>
public sealed class PassagePlan
{
    /// <summary>Creates the plan.</summary>
    /// <param name="legs">The legs in route order. Each leg starts where the previous one ends.</param>
    /// <param name="departure">
    /// The departure time from the first point, or <c>null</c> when the route
    /// has none; the checks then use <see cref="RouteCheckSettings.At"/>.
    /// </param>
    public PassagePlan(IReadOnlyList<PassageLeg> legs, DateTimeOffset? departure = null)
    {
        ArgumentNullException.ThrowIfNull(legs);
        Legs = legs;
        Departure = departure;
    }

    /// <summary>The legs in route order.</summary>
    public IReadOnlyList<PassageLeg> Legs { get; }

    /// <summary>The departure time from the first point, or <c>null</c> when the route has none.</summary>
    public DateTimeOffset? Departure { get; }

    /// <summary>
    /// The departure the checks use: <see cref="Departure"/>, or
    /// <paramref name="settings"/>' <see cref="RouteCheckSettings.At"/> when
    /// the plan has none.
    /// </summary>
    /// <param name="settings">The check settings.</param>
    /// <returns>The effective departure.</returns>
    public DateTimeOffset EffectiveDeparture(RouteCheckSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Departure ?? settings.At;
    }

    /// <summary>A copy of the plan with a different departure.</summary>
    /// <param name="departure">The departure, or <c>null</c>.</param>
    /// <returns>The plan.</returns>
    public PassagePlan WithDeparture(DateTimeOffset? departure) => new(Legs, departure);
}
