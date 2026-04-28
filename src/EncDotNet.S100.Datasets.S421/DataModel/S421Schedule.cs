using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S421.DataModel;

/// <summary>
/// The variant kind of an S-421 schedule (Manual, Calculated, or
/// Recommended). Each maps to a distinct information type in the GML.
/// </summary>
public enum S421ScheduleVariantKind
{
    /// <summary>A schedule manually authored by the navigator (<c>RouteScheduleManual</c>).</summary>
    Manual,
    /// <summary>A schedule calculated by the planning system (<c>RouteScheduleCalculated</c>).</summary>
    Calculated,
    /// <summary>A schedule recommended by an external authority (<c>RouteScheduleRecommended</c>).</summary>
    Recommended,
}

/// <summary>
/// Strongly-typed projection of an S-421 <c>RouteSchedule</c> information type.
/// Spec reference: S-421 Annex A "RouteSchedule" (FC).
/// </summary>
public sealed class S421Schedule
{
    /// <summary>The <c>gml:id</c> of the source <c>RouteSchedule</c> element.</summary>
    public required string Id { get; init; }

    /// <summary>FC code <c>routeScheduleID</c>.</summary>
    public int? ScheduleNumber { get; init; }

    /// <summary>FC code <c>routeScheduleName</c>.</summary>
    public string? Name { get; init; }

    /// <summary>The schedule variants present on this schedule.</summary>
    public required ImmutableArray<S421ScheduleVariant> Variants { get; init; }

    /// <summary>Unrecognised / extension attributes preserved verbatim.</summary>
    public required ImmutableDictionary<string, string> ExtraAttributes { get; init; }
}

/// <summary>
/// One variant of an S-421 schedule (Manual, Calculated, or Recommended)
/// containing the schedule elements per waypoint.
/// </summary>
public sealed class S421ScheduleVariant
{
    /// <summary>
    /// The <c>gml:id</c> of the source variant element
    /// (<c>RouteScheduleManual</c> / <c>RouteScheduleCalculated</c> /
    /// <c>RouteScheduleRecommended</c>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The variant kind.</summary>
    public required S421ScheduleVariantKind Kind { get; init; }

    /// <summary>The schedule elements, each addressing one waypoint.</summary>
    public required ImmutableArray<S421ScheduleElement> Elements { get; init; }
}

/// <summary>
/// Strongly-typed projection of an S-421 <c>RouteScheduleElement</c>
/// information type — schedule data for a single waypoint.
/// Spec reference: S-421 Annex A "RouteScheduleElement" (FC).
/// </summary>
public sealed class S421ScheduleElement
{
    /// <summary>The <c>gml:id</c> of the source <c>RouteScheduleElement</c>.</summary>
    public required string Id { get; init; }

    /// <summary>FC code <c>routeWaypointId</c> — the addressed waypoint ordinal.</summary>
    public int? WaypointNumber { get; init; }

    /// <summary>FC code <c>routeScheduleElementPlanSOG</c> (knots).</summary>
    public double? PlannedSpeedOverGround { get; init; }

    /// <summary>FC code <c>routeScheduleElementETD</c> — Estimated Time of Departure.</summary>
    public DateTimeOffset? Etd { get; init; }

    /// <summary>FC code <c>routeScheduleElementETA</c> — Estimated Time of Arrival.</summary>
    public DateTimeOffset? Eta { get; init; }

    /// <summary>FC code <c>routeScheduleElementETDWindowBefore</c> (minutes).</summary>
    public int? EtdWindowBeforeMinutes { get; init; }

    /// <summary>FC code <c>routeScheduleElementETDWindowAfter</c> (minutes).</summary>
    public int? EtdWindowAfterMinutes { get; init; }

    /// <summary>FC code <c>routeScheduleElementETAWindowBefore</c> (minutes).</summary>
    public int? EtaWindowBeforeMinutes { get; init; }

    /// <summary>FC code <c>routeScheduleElementETAWindowAfter</c> (minutes).</summary>
    public int? EtaWindowAfterMinutes { get; init; }

    /// <summary>FC code <c>routeScheduleElementNote</c>.</summary>
    public string? Note { get; init; }

    /// <summary>Unrecognised / extension attributes preserved verbatim.</summary>
    public required ImmutableDictionary<string, string> ExtraAttributes { get; init; }
}
