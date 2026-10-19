using System.Xml.Linq;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Routes.Rtz;

/// <summary>
/// An RTZ route plan (IEC 61174 Annex S): the <c>route</c> root element with
/// its route info, waypoints, schedules and extensions.
/// </summary>
/// <remarks>
/// <para>
/// The model follows the RTZ schema attribute for attribute. Every element
/// also keeps what this model doesn't name: attributes it doesn't know (or
/// can't parse) in <c>ExtraAttributes</c>, and the children of its
/// <c>extensions</c> element in <c>Extensions</c>. <see cref="RtzWriter"/>
/// writes both back, so a file read and written again keeps them (Annex S:
/// data a reader doesn't recognise is written back unchanged).
/// </para>
/// <para>
/// Units are RTZ's: cross-track distances and turn radii in nautical miles,
/// depths, draughts and clearances in metres, speeds in knots, times in UTC.
/// </para>
/// </remarks>
public sealed class RtzRoute
{
    /// <summary>The RTZ version the file declares, e.g. <c>"1.0"</c> (see <see cref="RtzVersions"/>).</summary>
    public string Version { get; init; } = RtzVersions.Latest;

    /// <summary>The <c>routeInfo</c> element.</summary>
    public RtzRouteInfo Info { get; init; } = new();

    /// <summary>
    /// The <c>defaultWaypoint</c> element: values a waypoint or leg takes when
    /// it doesn't set its own. Use <see cref="GetEffectiveRadius"/> and
    /// <see cref="GetEffectiveLeg"/> to read values with the defaults applied.
    /// </summary>
    public RtzDefaultWaypoint? DefaultWaypoint { get; init; }

    /// <summary>Waypoints in route order; a valid route has at least two.</summary>
    public IReadOnlyList<RtzWaypoint> Waypoints { get; init; } = [];

    /// <summary>The children of <c>waypoints/extensions</c>.</summary>
    public IReadOnlyList<XElement> WaypointsExtensions { get; init; } = [];

    /// <summary>The schedules, in document order.</summary>
    public IReadOnlyList<RtzSchedule> Schedules { get; init; } = [];

    /// <summary>The children of <c>schedules/extensions</c>.</summary>
    public IReadOnlyList<XElement> SchedulesExtensions { get; init; } = [];

    /// <summary>The children of the route's own <c>extensions</c> element.</summary>
    public IReadOnlyList<XElement> Extensions { get; init; } = [];

    /// <summary>Attributes of <c>route</c> other than <c>version</c> and namespace declarations.</summary>
    public IReadOnlyDictionary<XName, string> ExtraAttributes { get; init; } = RtzAttributes.None;

    /// <summary>
    /// Namespace prefixes the file declares on <c>route</c> besides RTZ's own
    /// (for example <c>stm</c>), so extensions are written with the same prefixes.
    /// </summary>
    public IReadOnlyDictionary<string, string> NamespaceDeclarations { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The turn radius of waypoint <paramref name="index"/>, in nautical miles:
    /// its own, else the default waypoint's.
    /// </summary>
    /// <param name="index">The waypoint's index in <see cref="Waypoints"/>.</param>
    /// <returns>The radius, or <c>null</c> when neither sets one.</returns>
    public double? GetEffectiveRadius(int index) => Waypoints[index].Radius ?? DefaultWaypoint?.Radius;

    /// <summary>
    /// The leg into waypoint <paramref name="index"/> (from the waypoint
    /// before it), with every attribute it doesn't set taken from the default
    /// waypoint's leg. In RTZ a waypoint's <c>leg</c> describes the leg
    /// directed to it, so the first waypoint's leg describes no leg of the route.
    /// </summary>
    /// <param name="index">The waypoint's index in <see cref="Waypoints"/>.</param>
    /// <returns>The leg, or <c>null</c> when neither the waypoint nor the default sets one.</returns>
    public RtzLeg? GetEffectiveLeg(int index)
    {
        var own = Waypoints[index].Leg;
        var fallback = DefaultWaypoint?.Leg;
        if (own is null || fallback is null)
            return own ?? fallback;
        return own.WithDefaults(fallback);
    }

    /// <summary>
    /// The schedule a planner reads speeds and times from: the first schedule
    /// with a calculated table, else the first with a manual one.
    /// </summary>
    /// <returns>The schedule, or <c>null</c> when there is none with elements.</returns>
    public RtzSchedule? GetPlanningSchedule()
        => Schedules.FirstOrDefault(s => s.Calculated is { Elements.Count: > 0 })
            ?? Schedules.FirstOrDefault(s => s.Manual is { Elements.Count: > 0 });
}

/// <summary>The <c>routeInfo</c> element: information about the whole route.</summary>
public sealed class RtzRouteInfo
{
    /// <summary><c>routeName</c>: the route's name. Mandatory in RTZ; the writer writes "Route" when it's empty.</summary>
    public string? RouteName { get; init; }

    /// <summary><c>routeAuthor</c>.</summary>
    public string? RouteAuthor { get; init; }

    /// <summary><c>routeStatus</c>: free text in the schema; STM uses 1 (Original) to 8 (Inactive).</summary>
    public string? RouteStatus { get; init; }

    /// <summary><c>validityPeriodStart</c>, UTC.</summary>
    public DateTimeOffset? ValidityPeriodStart { get; init; }

    /// <summary><c>validityPeriodStop</c>, UTC.</summary>
    public DateTimeOffset? ValidityPeriodStop { get; init; }

    /// <summary><c>vesselName</c>.</summary>
    public string? VesselName { get; init; }

    /// <summary><c>vesselMMSI</c>, as written (digits).</summary>
    public string? VesselMmsi { get; init; }

    /// <summary><c>vesselIMO</c>, as written (digits).</summary>
    public string? VesselImo { get; init; }

    /// <summary><c>vesselVoyage</c>: the voyage number (STM: the voyage's UVID).</summary>
    public string? VesselVoyage { get; init; }

    /// <summary><c>vesselDisplacement</c>, in tonnes.</summary>
    public long? VesselDisplacement { get; init; }

    /// <summary><c>vesselCargo</c>, in tonnes.</summary>
    public long? VesselCargo { get; init; }

    /// <summary><c>vesselGM</c>: metacentric height, in metres.</summary>
    public double? VesselGm { get; init; }

    /// <summary><c>optimizationMethod</c>.</summary>
    public string? OptimizationMethod { get; init; }

    /// <summary><c>vesselMaxRoll</c>: the largest roll allowed, in degrees.</summary>
    public long? VesselMaxRoll { get; init; }

    /// <summary><c>vesselMaxWave</c>: the significant wave height limit, in metres.</summary>
    public double? VesselMaxWave { get; init; }

    /// <summary><c>vesselMaxWind</c>: the wind speed limit.</summary>
    public double? VesselMaxWind { get; init; }

    /// <summary><c>vesselSpeedMax</c>, in knots.</summary>
    public double? VesselSpeedMax { get; init; }

    /// <summary><c>vesselServiceMin</c>: the preferred service speed's minimum, in knots.</summary>
    public double? VesselServiceMin { get; init; }

    /// <summary><c>vesselServiceMax</c>: the preferred service speed's maximum, in knots.</summary>
    public double? VesselServiceMax { get; init; }

    /// <summary><c>routeChangesHistory</c>.</summary>
    public string? RouteChangesHistory { get; init; }

    /// <summary>The children of <c>routeInfo/extensions</c> (for example STM's <c>RouteInfoEx</c>).</summary>
    public IReadOnlyList<XElement> Extensions { get; init; } = [];

    /// <summary>Attributes this model doesn't name, or couldn't parse, kept verbatim.</summary>
    public IReadOnlyDictionary<XName, string> ExtraAttributes { get; init; } = RtzAttributes.None;
}

/// <summary>The <c>defaultWaypoint</c> element: defaults for waypoints and legs.</summary>
public sealed class RtzDefaultWaypoint
{
    /// <summary><c>radius</c>: the default turn radius, in nautical miles.</summary>
    public double? Radius { get; init; }

    /// <summary>The default leg attributes.</summary>
    public RtzLeg? Leg { get; init; }

    /// <summary>The children of <c>defaultWaypoint/extensions</c>.</summary>
    public IReadOnlyList<XElement> Extensions { get; init; } = [];

    /// <summary>Attributes this model doesn't name, or couldn't parse, kept verbatim.</summary>
    public IReadOnlyDictionary<XName, string> ExtraAttributes { get; init; } = RtzAttributes.None;
}

/// <summary>A <c>waypoint</c> element: a position, and the leg directed to it.</summary>
public sealed class RtzWaypoint
{
    /// <summary><c>id</c>: the waypoint's unique identifier, which schedule elements refer to.</summary>
    public int Id { get; init; }

    /// <summary><c>revision</c>: increased on every change. Mandatory in RTZ 1.2.</summary>
    public int? Revision { get; init; }

    /// <summary><c>name</c>.</summary>
    public string? Name { get; init; }

    /// <summary><c>radius</c>: the turn radius, in nautical miles.</summary>
    public double? Radius { get; init; }

    /// <summary>The <c>position</c> (WGS 84).</summary>
    public GeoPosition Position { get; init; }

    /// <summary>
    /// The <c>leg</c>: the leg directed to this waypoint from the one before.
    /// See <see cref="RtzRoute.GetEffectiveLeg"/> for its value with defaults.
    /// </summary>
    public RtzLeg? Leg { get; init; }

    /// <summary>The children of <c>waypoint/extensions</c>.</summary>
    public IReadOnlyList<XElement> Extensions { get; init; } = [];

    /// <summary>Attributes this model doesn't name, or couldn't parse, kept verbatim.</summary>
    public IReadOnlyDictionary<XName, string> ExtraAttributes { get; init; } = RtzAttributes.None;
}

/// <summary>How a leg's path is defined (RTZ <c>geometryType</c>).</summary>
public enum RtzGeometryType
{
    /// <summary><c>Loxodrome</c>: a rhumb line. RTZ's default when the attribute is absent.</summary>
    Loxodrome,

    /// <summary><c>Orthodrome</c>: a great circle.</summary>
    Orthodrome,
}

/// <summary>A <c>leg</c> element: the attributes of the leg directed to a waypoint.</summary>
public sealed class RtzLeg
{
    /// <summary><c>starboardXTD</c>: the starboard cross-track distance limit, in nautical miles.</summary>
    public double? StarboardXtd { get; init; }

    /// <summary><c>portsideXTD</c>: the port cross-track distance limit, in nautical miles.</summary>
    public double? PortsideXtd { get; init; }

    /// <summary><c>safetyContour</c>: the planned safety contour, in metres.</summary>
    public double? SafetyContour { get; init; }

    /// <summary><c>safetyDepth</c>: the planned safety depth, in metres.</summary>
    public double? SafetyDepth { get; init; }

    /// <summary><c>geometryType</c>; absent means <see cref="RtzGeometryType.Loxodrome"/>.</summary>
    public RtzGeometryType? GeometryType { get; init; }

    /// <summary><c>speedMin</c>: the lowest cruising speed over ground, in knots.</summary>
    public double? SpeedMin { get; init; }

    /// <summary><c>speedMax</c>: the highest allowed speed over ground, in knots.</summary>
    public double? SpeedMax { get; init; }

    /// <summary><c>draughtForward</c>: the static draught forward, in metres.</summary>
    public double? DraughtForward { get; init; }

    /// <summary><c>draughtAft</c>: the static draught aft, in metres.</summary>
    public double? DraughtAft { get; init; }

    /// <summary><c>staticUKC</c>: the minimum under-keel clearance on the leg, in metres.</summary>
    public double? StaticUkc { get; init; }

    /// <summary><c>dynamicUKC</c>: the minimum dynamic under-keel clearance on the leg, in metres.</summary>
    public double? DynamicUkc { get; init; }

    /// <summary><c>masthead</c>: the height of the masthead above the keel, in metres.</summary>
    public double? Masthead { get; init; }

    /// <summary><c>legReport</c>: reporting information.</summary>
    public string? LegReport { get; init; }

    /// <summary><c>legInfo</c>: information worth knowing on the leg.</summary>
    public string? LegInfo { get; init; }

    /// <summary><c>legNote1</c>: notes on the ETD and ETA.</summary>
    public string? LegNote1 { get; init; }

    /// <summary><c>legNote2</c>: local remarks.</summary>
    public string? LegNote2 { get; init; }

    /// <summary>The children of <c>leg/extensions</c> (RTZ 1.2 only).</summary>
    public IReadOnlyList<XElement> Extensions { get; init; } = [];

    /// <summary>Attributes this model doesn't name, or couldn't parse, kept verbatim.</summary>
    public IReadOnlyDictionary<XName, string> ExtraAttributes { get; init; } = RtzAttributes.None;

    /// <summary>The deeper of the two draughts, or the one that is set.</summary>
    public double? MaxDraught => DraughtForward is { } f && DraughtAft is { } a ? Math.Max(f, a) : DraughtForward ?? DraughtAft;

    /// <summary>This leg with every attribute it doesn't set taken from <paramref name="defaults"/>.</summary>
    /// <param name="defaults">The default leg.</param>
    /// <returns>The merged leg.</returns>
    public RtzLeg WithDefaults(RtzLeg defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        var extras = new Dictionary<XName, string>(defaults.ExtraAttributes);
        foreach (var (name, value) in ExtraAttributes)
            extras[name] = value;
        return new RtzLeg
        {
            StarboardXtd = StarboardXtd ?? defaults.StarboardXtd,
            PortsideXtd = PortsideXtd ?? defaults.PortsideXtd,
            SafetyContour = SafetyContour ?? defaults.SafetyContour,
            SafetyDepth = SafetyDepth ?? defaults.SafetyDepth,
            GeometryType = GeometryType ?? defaults.GeometryType,
            SpeedMin = SpeedMin ?? defaults.SpeedMin,
            SpeedMax = SpeedMax ?? defaults.SpeedMax,
            DraughtForward = DraughtForward ?? defaults.DraughtForward,
            DraughtAft = DraughtAft ?? defaults.DraughtAft,
            StaticUkc = StaticUkc ?? defaults.StaticUkc,
            DynamicUkc = DynamicUkc ?? defaults.DynamicUkc,
            Masthead = Masthead ?? defaults.Masthead,
            LegReport = LegReport ?? defaults.LegReport,
            LegInfo = LegInfo ?? defaults.LegInfo,
            LegNote1 = LegNote1 ?? defaults.LegNote1,
            LegNote2 = LegNote2 ?? defaults.LegNote2,
            Extensions = Extensions.Count > 0 ? Extensions : defaults.Extensions,
            ExtraAttributes = extras,
        };
    }
}

/// <summary>A <c>schedule</c> element: a manual table, a calculated table, or both.</summary>
public sealed class RtzSchedule
{
    /// <summary><c>id</c>: the schedule's identifier.</summary>
    public int Id { get; init; }

    /// <summary><c>name</c>.</summary>
    public string? Name { get; init; }

    /// <summary>The <c>manual</c> table: the planner's own times and speeds, or <c>null</c>.</summary>
    public RtzScheduleTable? Manual { get; init; }

    /// <summary>
    /// The <c>calculated</c> table: times and speeds calculated from the
    /// manual one, or <c>null</c>. Where the two disagree, Annex S says the
    /// calculated value is the valid one.
    /// </summary>
    public RtzScheduleTable? Calculated { get; init; }

    /// <summary>The children of <c>schedule/extensions</c>.</summary>
    public IReadOnlyList<XElement> Extensions { get; init; } = [];

    /// <summary>Attributes this model doesn't name, or couldn't parse, kept verbatim.</summary>
    public IReadOnlyDictionary<XName, string> ExtraAttributes { get; init; } = RtzAttributes.None;

    /// <summary>
    /// The element for waypoint <paramref name="waypointId"/>: the calculated
    /// table's, else the manual table's.
    /// </summary>
    /// <param name="waypointId">An <see cref="RtzWaypoint.Id"/>.</param>
    /// <returns>The element, or <c>null</c>.</returns>
    public RtzScheduleElement? ElementFor(int waypointId)
        => Calculated?.ElementFor(waypointId) ?? Manual?.ElementFor(waypointId);
}

/// <summary>A schedule's <c>manual</c> or <c>calculated</c> table.</summary>
public sealed class RtzScheduleTable
{
    /// <summary>The schedule elements, one per waypoint at most.</summary>
    public IReadOnlyList<RtzScheduleElement> Elements { get; init; } = [];

    /// <summary>The children of the table's <c>extensions</c>.</summary>
    public IReadOnlyList<XElement> Extensions { get; init; } = [];

    /// <summary>The element for waypoint <paramref name="waypointId"/>, or <c>null</c>.</summary>
    /// <param name="waypointId">An <see cref="RtzWaypoint.Id"/>.</param>
    /// <returns>The element.</returns>
    public RtzScheduleElement? ElementFor(int waypointId) => Elements.FirstOrDefault(e => e.WaypointId == waypointId);
}

/// <summary>
/// A schedule element (<c>scheduleElement</c>; <c>sheduleElement</c> in RTZ
/// 1.0): time-related values for a waypoint and the leg directed to it.
/// </summary>
public sealed class RtzScheduleElement
{
    /// <summary><c>waypointId</c>: the <see cref="RtzWaypoint.Id"/> this element is for.</summary>
    public int WaypointId { get; init; }

    /// <summary><c>etd</c>: the estimated departure, UTC.</summary>
    public DateTimeOffset? Etd { get; init; }

    /// <summary><c>etdWindowBefore</c>.</summary>
    public TimeSpan? EtdWindowBefore { get; init; }

    /// <summary><c>etdWindowAfter</c>.</summary>
    public TimeSpan? EtdWindowAfter { get; init; }

    /// <summary><c>eta</c>: the estimated arrival, UTC.</summary>
    public DateTimeOffset? Eta { get; init; }

    /// <summary><c>etaWindowBefore</c>.</summary>
    public TimeSpan? EtaWindowBefore { get; init; }

    /// <summary><c>etaWindowAfter</c>.</summary>
    public TimeSpan? EtaWindowAfter { get; init; }

    /// <summary><c>stay</c>: the time spent at the waypoint.</summary>
    public TimeSpan? Stay { get; init; }

    /// <summary><c>speed</c>: the planned speed on the leg directed to the waypoint, in knots.</summary>
    public double? Speed { get; init; }

    /// <summary><c>speedWindow</c>, in knots.</summary>
    public double? SpeedWindow { get; init; }

    /// <summary><c>windSpeed</c>.</summary>
    public double? WindSpeed { get; init; }

    /// <summary><c>windDirection</c>, in degrees.</summary>
    public double? WindDirection { get; init; }

    /// <summary><c>currentSpeed</c>, in knots.</summary>
    public double? CurrentSpeed { get; init; }

    /// <summary><c>currentDirection</c>, in degrees.</summary>
    public double? CurrentDirection { get; init; }

    /// <summary><c>windLoss</c>, in knots.</summary>
    public double? WindLoss { get; init; }

    /// <summary><c>waveLoss</c>, in knots.</summary>
    public double? WaveLoss { get; init; }

    /// <summary><c>totalLoss</c>, in knots.</summary>
    public double? TotalLoss { get; init; }

    /// <summary><c>rpm</c>: the advised engine RPM.</summary>
    public long? Rpm { get; init; }

    /// <summary><c>pitch</c>: the advised engine pitch.</summary>
    public long? Pitch { get; init; }

    /// <summary><c>fuel</c>: the predicted fuel consumption on the leg.</summary>
    public double? Fuel { get; init; }

    /// <summary><c>relFuelSave</c>, in percent.</summary>
    public double? RelFuelSave { get; init; }

    /// <summary><c>absFuelSave</c> (<c>absFuelSace</c> in the RTZ 1.0 schema).</summary>
    public double? AbsFuelSave { get; init; }

    /// <summary><c>Note</c>.</summary>
    public string? Note { get; init; }

    /// <summary>The children of the element's <c>extensions</c>.</summary>
    public IReadOnlyList<XElement> Extensions { get; init; } = [];

    /// <summary>Attributes this model doesn't name, or couldn't parse, kept verbatim.</summary>
    public IReadOnlyDictionary<XName, string> ExtraAttributes { get; init; } = RtzAttributes.None;

    /// <summary>
    /// When the vessel leaves the waypoint: <see cref="Etd"/>, else
    /// <see cref="Eta"/> plus <see cref="Stay"/>.
    /// </summary>
    public DateTimeOffset? Departure => Etd ?? (Eta is { } eta ? eta + (Stay ?? TimeSpan.Zero) : null);
}

/// <summary>Shared empty attribute set.</summary>
internal static class RtzAttributes
{
    public static readonly IReadOnlyDictionary<XName, string> None = new Dictionary<XName, string>();
}
