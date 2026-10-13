namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// Route-level passage values: the defaults every <see cref="RouteLeg"/>
/// inherits unless it carries its own override. Every field is optional; a
/// route with only waypoints has an empty passage.
/// </summary>
/// <remarks>
/// <para>
/// A leg's effective value for an inheritable field
/// (<see cref="RoutePassageField"/>) is its override when set, otherwise the
/// passage value. Changing a passage value therefore changes only the legs
/// that inherit it. Edit passage values through
/// <see cref="Route.SetPassageValue"/> and <see cref="Route.SetDeparture"/>,
/// and leg overrides through <see cref="Route.SetLegOverride"/>, so that the
/// "store an override only when it differs" rule and the
/// <see cref="Route.Changed"/> event stay consistent. Each of those methods
/// sets one value and returns the previous one. Interactive and agent edits
/// run inside <see cref="Route.History"/>, whose snapshots include the
/// passage, so each one undoes as one step.
/// </para>
/// <para>
/// The vessel stays on <see cref="RouteInfo.Vessel"/>; the Passage card shows
/// it alongside these values.
/// </para>
/// <para>
/// Mapping to exchange formats, for the S-421 and RTZ import/export work
/// (#390, #391, #888). "Leg" means <c>RouteWaypointLeg</c> in S-421 and
/// <c>&lt;leg&gt;</c> in RTZ 1.x. Route-level values have no route-level
/// slot in either format, so on export they are written to every leg (or
/// schedule element) as its effective value, and on import a value shared by
/// every leg can be lifted back into the passage.
/// </para>
/// <code>
/// | Passage value            | Leg override (RouteLeg)                 | S-421                                            | RTZ 1.x                                   |
/// |--------------------------|-----------------------------------------|--------------------------------------------------|-------------------------------------------|
/// | DraftMeters              | DraftMeters                             | routeWaypointLegDraft                            | leg/@draughtForward, leg/@draughtAft      |
/// | SquatMeters              | (none; vessel allowance)                | (none)                                           | (none)                                    |
/// | SpeedKnots (SOG)         | PlannedSpeedKnots                       | RouteScheduleElement routeScheduleElementPlanSOG | scheduleElement/@speed                    |
/// | Departure                | (none; route-level)                     | first RouteScheduleElement routeScheduleElementETD | first scheduleElement/@etd              |
/// | RequiredUkcMeters        | StaticUnderKeelClearanceMeters          | routeWaypointLegStaticUKC                        | leg/@staticUKC                            |
/// | PortCrossTrackMeters     | PortCrossTrackDistanceLimitMeters       | routeWaypointLegPortXTDL                         | leg/@portsideXTD (NM in RTZ)              |
/// | StarboardCrossTrackMeters| StarboardCrossTrackDistanceLimitMeters  | routeWaypointLegStarboardXTDL                    | leg/@starboardXTD (NM in RTZ)             |
/// | (RouteInfo.Vessel)       | (none)                                  | routeInfoVessel*                                 | routeInfo/@vesselName, @vesselMMSI, …     |
/// </code>
/// <para>
/// Per-leg S-421 values that are not passage values (channel limits, safety
/// contour and depth, SOG/STW limits, dynamic UKC, safety margin, note) stay
/// plain leg attributes: they round-trip but don't inherit.
/// </para>
/// </remarks>
public sealed class RoutePassage
{
    /// <summary>Vessel draft, in metres. Enables Check passage.</summary>
    public double? DraftMeters { get; internal set; }

    /// <summary>Fixed squat allowance, in metres; added to the draft. Optional.</summary>
    public double? SquatMeters { get; internal set; }

    /// <summary>Planned speed over ground, in knots. Enables ETAs.</summary>
    public double? SpeedKnots { get; internal set; }

    /// <summary>
    /// Planned departure from the first waypoint. When <c>null</c>, ETAs and
    /// checks use the current view time.
    /// </summary>
    public DateTimeOffset? Departure { get; internal set; }

    /// <summary>Required static under-keel clearance, in metres.</summary>
    public double? RequiredUkcMeters { get; internal set; }

    /// <summary>Port cross-track distance limit, in metres.</summary>
    public double? PortCrossTrackMeters { get; internal set; }

    /// <summary>Starboard cross-track distance limit, in metres.</summary>
    public double? StarboardCrossTrackMeters { get; internal set; }

    /// <summary>Whether no passage value is set.</summary>
    public bool IsEmpty =>
        DraftMeters is null && SquatMeters is null && SpeedKnots is null && Departure is null
        && RequiredUkcMeters is null && PortCrossTrackMeters is null && StarboardCrossTrackMeters is null;

    /// <summary>Returns the passage value of <paramref name="field"/>.</summary>
    /// <param name="field">The passage field to read.</param>
    /// <returns>The value, or <c>null</c> when unset.</returns>
    public double? Get(RoutePassageField field) => field switch
    {
        RoutePassageField.Draft => DraftMeters,
        RoutePassageField.Squat => SquatMeters,
        RoutePassageField.Speed => SpeedKnots,
        RoutePassageField.RequiredUkc => RequiredUkcMeters,
        RoutePassageField.PortCrossTrack => PortCrossTrackMeters,
        RoutePassageField.StarboardCrossTrack => StarboardCrossTrackMeters,
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    /// <summary>Returns a copy of these passage values.</summary>
    internal RoutePassage Clone() => (RoutePassage)MemberwiseClone();

    /// <summary>Overwrites every value with <paramref name="other"/>'s.</summary>
    internal void CopyFrom(RoutePassage other)
    {
        DraftMeters = other.DraftMeters;
        SquatMeters = other.SquatMeters;
        SpeedKnots = other.SpeedKnots;
        Departure = other.Departure;
        RequiredUkcMeters = other.RequiredUkcMeters;
        PortCrossTrackMeters = other.PortCrossTrackMeters;
        StarboardCrossTrackMeters = other.StarboardCrossTrackMeters;
    }

    internal void Set(RoutePassageField field, double? value)
    {
        switch (field)
        {
            case RoutePassageField.Draft: DraftMeters = value; break;
            case RoutePassageField.Squat: SquatMeters = value; break;
            case RoutePassageField.Speed: SpeedKnots = value; break;
            case RoutePassageField.RequiredUkc: RequiredUkcMeters = value; break;
            case RoutePassageField.PortCrossTrack: PortCrossTrackMeters = value; break;
            case RoutePassageField.StarboardCrossTrack: StarboardCrossTrackMeters = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }
}

/// <summary>
/// A numeric passage value. Every field except <see cref="Squat"/> can be
/// overridden per leg (see <see cref="RoutePassageFields.IsLegOverridable"/>).
/// </summary>
public enum RoutePassageField
{
    /// <summary>Draft, in metres (<see cref="RouteLeg.DraftMeters"/>).</summary>
    Draft,

    /// <summary>Squat allowance, in metres. Route-level only.</summary>
    Squat,

    /// <summary>Speed over ground, in knots (<see cref="RouteLeg.PlannedSpeedKnots"/>).</summary>
    Speed,

    /// <summary>Required static UKC, in metres (<see cref="RouteLeg.StaticUnderKeelClearanceMeters"/>).</summary>
    RequiredUkc,

    /// <summary>Port cross-track limit, in metres (<see cref="RouteLeg.PortCrossTrackDistanceLimitMeters"/>).</summary>
    PortCrossTrack,

    /// <summary>Starboard cross-track limit, in metres (<see cref="RouteLeg.StarboardCrossTrackDistanceLimitMeters"/>).</summary>
    StarboardCrossTrack,
}

/// <summary>Helpers for <see cref="RoutePassageField"/>.</summary>
public static class RoutePassageFields
{
    /// <summary>The fields a leg can override, in display order.</summary>
    public static IReadOnlyList<RoutePassageField> LegOverridable { get; } =
    [
        RoutePassageField.Speed,
        RoutePassageField.Draft,
        RoutePassageField.PortCrossTrack,
        RoutePassageField.StarboardCrossTrack,
        RoutePassageField.RequiredUkc,
    ];

    /// <summary>Whether a leg can carry an override for <paramref name="field"/>.</summary>
    public static bool IsLegOverridable(RoutePassageField field) => field != RoutePassageField.Squat;
}
