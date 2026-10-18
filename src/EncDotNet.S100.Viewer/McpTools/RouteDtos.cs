using System.ComponentModel;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>One waypoint of a route, as seen over the MCP wire.</summary>
[Description("A single route waypoint with its position and optional metadata.")]
internal sealed record WaypointDto(
    [property: Description("Zero-based index of the waypoint in route order.")] int Index,
    [property: Description("WGS-84 latitude in decimal degrees.")] double Lat,
    [property: Description("WGS-84 longitude in decimal degrees.")] double Lon,
    [property: Description("Author-assigned ordinal (S-421 routeWaypointID), or null.")] int? Number,
    [property: Description("Human-readable waypoint name, or null.")] string? Name,
    [property: Description("Whether the waypoint is pinned and must not be moved, or null when unset.")] bool? Fixed,
    [property: Description("Planned turn radius at the waypoint in nautical miles, or null.")] double? TurnRadiusNm,
    [property: Description("ETA at the waypoint (ISO-8601), or null when unknown: no speed on this or an earlier leg. See the route's 'times'.")] DateTimeOffset? Eta = null);

/// <summary>
/// The passage-inheritable values of one leg: either the effective values or
/// only the leg's overrides.
/// </summary>
[Description("A leg's passage values (speed, draft, cross-track limits, required UKC). In 'effective' each is the leg's override when set, else the route's passage value; in 'overrides' only the leg's own values are present.")]
internal sealed record LegPassageValuesDto(
    [property: Description("Planned speed over ground in knots, or null.")] double? SpeedKnots,
    [property: Description("Draft in metres, or null.")] double? DraftMeters,
    [property: Description("Port cross-track distance limit in metres, or null.")] double? PortCrossTrackMeters,
    [property: Description("Starboard cross-track distance limit in metres, or null.")] double? StarboardCrossTrackMeters,
    [property: Description("Required static under-keel clearance in metres, or null.")] double? RequiredUkcMeters);

/// <summary>One leg of a route, including its computed geometry.</summary>
[Description("A single route leg (the segment joining two consecutive waypoints) with its computed metrics, its passage values (effective and overridden) and the rest of its S-421 navigational envelope.")]
internal sealed record LegDto(
    [property: Description("Zero-based index of the leg; leg i joins waypoint i to waypoint i+1.")] int Index,
    [property: Description("Leg path geometry: \"loxodrome\" (rhumb line, constant bearing) or \"geodesic\" (great circle).")] string GeometryType,
    [property: Description("Computed leg length in nautical miles.")] double DistanceNm,
    [property: Description("Computed initial true bearing in degrees [0, 360).")] double InitialBearingDegrees,
    [property: Description("The passage values that apply to this leg: its override when set, else the route's passage value.")] LegPassageValuesDto Effective,
    [property: Description("Only this leg's overrides; a value absent here is inherited from the route's passage.")] LegPassageValuesDto Overrides,
    [property: Description("Starboard channel limit in metres, or null.")] double? StarboardChannelLimitMeters,
    [property: Description("Port channel limit in metres, or null.")] double? PortChannelLimitMeters,
    [property: Description("Safety contour for the leg in metres, or null.")] double? SafetyContourMeters,
    [property: Description("Safety depth for the leg in metres, or null.")] double? SafetyDepthMeters,
    [property: Description("Minimum speed over ground in knots, or null.")] double? SpeedOverGroundMinKnots,
    [property: Description("Maximum speed over ground in knots, or null.")] double? SpeedOverGroundMaxKnots,
    [property: Description("Minimum speed through water in knots, or null.")] double? SpeedThroughWaterMinKnots,
    [property: Description("Maximum speed through water in knots, or null.")] double? SpeedThroughWaterMaxKnots,
    [property: Description("Dynamic under-keel clearance in metres, or null.")] double? DynamicUnderKeelClearanceMeters,
    [property: Description("Safety margin in metres, or null.")] double? SafetyMarginMeters,
    [property: Description("Free-text note for the leg, or null.")] string? Note,
    [property: Description("Time to run the leg at its effective speed, in minutes, or null when it has no speed.")] double? DurationMinutes = null);

/// <summary>Route-level passage values.</summary>
[Description("A route's passage values, which every leg inherits unless it overrides them; all optional.")]
internal sealed record PassageDto(
    [property: Description("Draft in metres, or null. Enables Check passage.")] double? DraftMeters,
    [property: Description("Fixed squat allowance in metres, or null.")] double? SquatMeters,
    [property: Description("Planned speed over ground in knots, or null. Enables ETAs.")] double? SpeedKnots,
    [property: Description("Planned departure (ISO-8601), or null to use the view time.")] DateTimeOffset? Departure,
    [property: Description("Required static under-keel clearance in metres, or null.")] double? RequiredUkcMeters,
    [property: Description("Port cross-track distance limit in metres, or null.")] double? PortCrossTrackMeters,
    [property: Description("Starboard cross-track distance limit in metres, or null.")] double? StarboardCrossTrackMeters,
    [property: Description("The vessel (from the route info; set it with set_route_info), or null.")] VesselDto? Vessel);

/// <summary>What one passage value is (#960, decision 16).</summary>
[Description("What one passage value is: a check setting or a route value, and where it comes from.")]
internal sealed record PassageFieldDto(
    [property: Description("The PassageDto property: draftMeters, squatMeters, requiredUkcMeters, departure, speedKnots, portCrossTrackMeters or starboardCrossTrackMeters.")] string Field,
    [property: Description("\"checkSetting\" (your setting for checking the route, not route content; written on export only when asked) or \"route\" (route content).")] string Kind,
    [property: Description("True when the value shown comes from the received route's file (a check setting you haven't set, or any route value of a received route).")] bool FromFile,
    [property: Description("True when set_passage can't change it: a route value of a received route (copy the route to edit it).")] bool ReadOnly);

/// <summary>Where a received route came from.</summary>
[Description("Where a received (read-only) route came from: its exchange file and the identity the file gives it.")]
internal sealed record RouteSourceDto(
    [property: Description("Exchange format: \"RTZ\" or \"S-421\".")] string Format,
    [property: Description("The format version the file declares, e.g. \"1.0\", or null.")] string? FormatVersion,
    [property: Description("Full path of the file the route was read from.")] string File,
    [property: Description("The route identifier in the file (S-421 routeID), or null (always null for RTZ, which has none; the route is known by its file).")] string? RouteId,
    [property: Description("The route edition in the file (S-421 routeEditionNo), or null (always null for RTZ).")] int? Edition,
    [property: Description("The route status code in the file (S-421 routeInfoStatus: 1 Initial, 2 Planned, 3 Recommended, 4 Acknowledged, 5 Used for monitoring, 6 Terminated, 7 Errors, …), or null.")] int? Status);

/// <summary>A route's timing: departure, arrival and where ETAs stop.</summary>
[Description("A route's timing at each leg's effective speed over ground (S-111 currents aren't applied).")]
internal sealed record RouteTimesDto(
    [property: Description("The time the ETAs run from (ISO-8601): the passage departure, or the view time when none is set.")] DateTimeOffset Departure,
    [property: Description("True when the route has no departure, so the ETAs follow the viewer's view time.")] bool FromViewTime,
    [property: Description("Arrival at the last waypoint (ISO-8601), or null when a leg has no speed.")] DateTimeOffset? Arrival,
    [property: Description("Time under way in minutes, or null when a leg has no speed.")] double? DurationMinutes,
    [property: Description("Index of the first leg with no speed, where the ETAs stop, or null when every leg has a speed.")] int? EtasStopAtLeg);

/// <summary>Result of get_passage.</summary>
[Description("Result of get_passage: a route's passage values.")]
internal sealed record GetPassageResult(
    [property: Description("Stable route identifier.")] string RouteId,
    [property: Description("The route's passage values.")] PassageDto Passage,
    [property: Description("What each passage value is: a check setting (draft, squat, required UKC, departure: yours, kept locally, editable on any route) or a route value (speed, cross-track: route content, read-only on a received route), and whether it comes from the route's file.")] IReadOnlyList<PassageFieldDto>? Fields = null);

/// <summary>Vessel metadata carried by a route's info block.</summary>
[Description("Vessel metadata the route is planned for; all fields optional.")]
internal sealed record VesselDto(
    [property: Description("Vessel name, or null.")] string? Name,
    [property: Description("Maritime Mobile Service Identity, or null.")] string? Mmsi,
    [property: Description("IMO number, or null.")] string? Imo,
    [property: Description("Call sign, or null.")] string? Callsign,
    [property: Description("Overall length in metres, or null.")] double? LengthMeters,
    [property: Description("Beam in metres, or null.")] double? BeamMeters);

/// <summary>Route-level metadata.</summary>
[Description("Route-level metadata (S-421 RouteInfo); all fields optional.")]
internal sealed record RouteInfoDto(
    [property: Description("Route name, or null.")] string? Name,
    [property: Description("Route author / originator, or null.")] string? Author,
    [property: Description("Free-text description, or null.")] string? Description,
    [property: Description("Departure port identifier, or null.")] string? DeparturePortId,
    [property: Description("Arrival port identifier, or null.")] string? ArrivalPortId,
    [property: Description("Planned start of validity (UTC ISO-8601), or null.")] DateTimeOffset? ValidityStart,
    [property: Description("Planned end of validity (UTC ISO-8601), or null.")] DateTimeOffset? ValidityEnd,
    [property: Description("Vessel metadata, or null when no vessel has been set.")] VesselDto? Vessel);

/// <summary>A compact route summary returned by list_routes.</summary>
[Description("A compact route summary: identity plus aggregate metrics.")]
internal sealed record RouteSummary(
    [property: Description("Stable route identifier.")] string RouteId,
    [property: Description("Route name, or null.")] string? Name,
    [property: Description("Whether this is the collection's active (default-target) route.")] bool IsActive,
    [property: Description("Number of waypoints in the route.")] int WaypointCount,
    [property: Description("Number of legs (max(0, waypointCount - 1)).")] int LegCount,
    [property: Description("Total route length in nautical miles.")] double TotalDistanceNm,
    [property: Description("True for a received route (from an opened S-421 dataset): read-only, not saved; edits fail with route_read_only. Copy it to edit.")] bool ReadOnly = false,
    [property: Description("Where a received route came from, or null for an owned route.")] RouteSourceDto? Source = null,
    [property: Description("How the map draws the route: \"planning\" (the route renderer) or \"native\" (its S-421 dataset's own portrayal). Set it with set_route_display_mode.")] string DisplayMode = "planning");

/// <summary>The full projection of a single route.</summary>
[Description("The full state of one route: identity, metadata, passage values, ordered waypoints and legs, and total distance.")]
internal sealed record RouteDetail(
    [property: Description("Stable route identifier.")] string RouteId,
    [property: Description("Route name, or null.")] string? Name,
    [property: Description("Whether this is the collection's active (default-target) route.")] bool IsActive,
    [property: Description("Route-level metadata.")] RouteInfoDto Info,
    [property: Description("Route-level passage values that legs inherit.")] PassageDto Passage,
    [property: Description("Waypoints in route order.")] IReadOnlyList<WaypointDto> Waypoints,
    [property: Description("Legs in route order; leg i joins waypoint i to waypoint i+1.")] IReadOnlyList<LegDto> Legs,
    [property: Description("Total route length in nautical miles.")] double TotalDistanceNm,
    [property: Description("Whether undo_route_edit has an edit to undo on this route.")] bool CanUndo,
    [property: Description("Whether redo_route_edit has an edit to redo on this route.")] bool CanRedo,
    [property: Description("What undo_route_edit would revert (e.g. \"move waypoint\"), or null.")] string? UndoLabel,
    [property: Description("What redo_route_edit would replay, or null.")] string? RedoLabel,
    [property: Description("The selected waypoint's index on the active route (set with select_route_item), or null; always null for other routes.")] int? SelectedWaypointIndex,
    [property: Description("The selected leg's index on the active route (haloed on the map, set with select_route_item), or null; always null for other routes.")] int? SelectedLegIndex,
    [property: Description("Departure, arrival and duration when at least one leg has a speed (set speedKnots with set_passage or set_leg_attributes), else null.")] RouteTimesDto? Times = null,
    [property: Description("True for a received route (from an opened S-421 dataset): read-only, not saved; edits fail with route_read_only. Copy it to edit.")] bool ReadOnly = false,
    [property: Description("Where a received route came from, or null for an owned route.")] RouteSourceDto? Source = null,
    [property: Description("How the map draws the route: \"planning\" (the route renderer) or \"native\" (its S-421 dataset's own portrayal). Set it with set_route_display_mode.")] string DisplayMode = "planning",
    [property: Description("What each passage value is: a check setting (draft, squat, required UKC, departure: yours, kept locally, editable on any route) or a route value (speed, cross-track: route content, read-only on a received route), and whether it comes from the route's file.")] IReadOnlyList<PassageFieldDto>? PassageFields = null);

/// <summary>Result of list_routes.</summary>
[Description("Result of list_routes: every route in the collection plus the active route id.")]
internal sealed record ListRoutesResult(
    [property: Description("All routes in insertion order.")] IReadOnlyList<RouteSummary> Routes,
    [property: Description("Id of the active route, or null when the collection is empty.")] string? ActiveRouteId,
    [property: Description("Whether the interactive route editor is on (set it with set_route_edit_mode).")] bool EditMode,
    [property: Description("Whether the Routes panel's Passage card is expanded (set it with set_passage_card).")] bool PassageCardExpanded);

/// <summary>Result of set_passage_card.</summary>
[Description("Result of set_passage_card: whether the Passage card is expanded.")]
internal sealed record PassageCardResult(
    [property: Description("Whether the Passage card is expanded.")] bool Expanded);

/// <summary>Result of set_route_edit_mode.</summary>
[Description("Result of set_route_edit_mode: the route edit mode now in force.")]
internal sealed record RouteEditModeResult(
    [property: Description("Whether the interactive route editor is on.")] bool Enabled,
    [property: Description("Id of the active route edits apply to, or null when there is none.")] string? ActiveRouteId);

/// <summary>Result of delete_route.</summary>
[Description("Result of delete_route: which route was removed and the new active route id.")]
internal sealed record DeleteRouteResult(
    [property: Description("Id of the route that was requested for deletion.")] string RouteId,
    [property: Description("Whether the route was present and removed.")] bool Deleted,
    [property: Description("Id of the active route after deletion, or null when the collection is now empty.")] string? ActiveRouteId);

/// <summary>
/// Builds wire DTOs from the live route model. All members assume they are
/// called on the UI thread (inside an <see cref="IRouteEditInvoker"/>
/// callback), so they read the mutable model without locking.
/// </summary>
internal static class RouteProjection
{
    /// <summary>Maps a geometry-type enum to its wire token.</summary>
    public static string GeometryToken(RouteLegGeometryType type)
        => type == RouteLegGeometryType.Geodesic ? "geodesic" : "loxodrome";

    /// <summary>Projects a single route to a <see cref="RouteDetail"/>.</summary>
    public static RouteDetail Detail(Route route, RoutesService routes)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(routes);

        var etas = RouteEtas.Compute(route, routes.ViewTime.Current);
        var waypoints = new List<WaypointDto>(route.Waypoints.Count);
        for (var i = 0; i < route.Waypoints.Count; i++)
        {
            var wp = route.Waypoints[i];
            waypoints.Add(new WaypointDto(
                i, wp.Position.Latitude, wp.Position.Longitude,
                wp.Number, wp.Name, wp.Fixed, wp.TurnRadiusNm, etas.WaypointEta(i)));
        }

        var legs = new List<LegDto>(route.Legs.Count);
        for (var i = 0; i < route.Legs.Count; i++)
        {
            var leg = route.Legs[i];
            var metrics = route.ComputeLegMetrics(i);
            legs.Add(new LegDto(
                i,
                GeometryToken(leg.GeometryType),
                metrics.DistanceNm,
                metrics.InitialBearingDegrees,
                LegValues(field => route.GetEffectiveLegValue(i, field)),
                LegValues(leg.GetOverride),
                leg.StarboardChannelLimitMeters,
                leg.PortChannelLimitMeters,
                leg.SafetyContourMeters,
                leg.SafetyDepthMeters,
                leg.SpeedOverGroundMinKnots,
                leg.SpeedOverGroundMaxKnots,
                leg.SpeedThroughWaterMinKnots,
                leg.SpeedThroughWaterMaxKnots,
                leg.DynamicUnderKeelClearanceMeters,
                leg.SafetyMarginMeters,
                leg.Note,
                etas.LegDuration(i)?.TotalMinutes));
        }

        var isActive = ReferenceEquals(route, routes.Routes.ActiveRoute);
        return new RouteDetail(
            route.Id,
            route.Name,
            isActive,
            Info(route.Info),
            Passage(route),
            waypoints,
            legs,
            route.TotalDistanceNm(),
            route.History.CanUndo,
            route.History.CanRedo,
            route.History.UndoLabel,
            route.History.RedoLabel,
            isActive ? routes.SelectedWaypointIndex : null,
            isActive ? routes.SelectedLegIndex : null,
            Times(etas),
            route.IsReadOnly,
            SourceOf(route),
            DisplayModeToken(route.DisplayMode),
            PassageFields(route));
    }

    /// <summary>The wire token of a display mode.</summary>
    public static string DisplayModeToken(RouteDisplayMode mode)
        => mode == RouteDisplayMode.Native ? "native" : "planning";

    private static RouteSourceDto? SourceOf(Route route)
        => route.Source is { } s
            ? new RouteSourceDto(s.Format, s.FormatVersion, s.FilePath, s.RouteId, s.Edition, s.Status)
            : null;

    private static RouteTimesDto? Times(RouteEtas etas)
        => etas.Schedule.Legs.Any(l => l.Duration is not null)
            ? new RouteTimesDto(
                etas.Departure,
                etas.IsFromViewTime,
                etas.Arrival,
                etas.Duration?.TotalMinutes,
                etas.FirstLegWithoutSpeed)
            : null;

    /// <summary>Projects a single route to a compact <see cref="RouteSummary"/>.</summary>
    public static RouteSummary Summary(Route route, RoutesService routes)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(routes);
        return new RouteSummary(
            route.Id,
            route.Name,
            ReferenceEquals(route, routes.Routes.ActiveRoute),
            route.Waypoints.Count,
            route.Legs.Count,
            route.TotalDistanceNm(),
            route.IsReadOnly,
            SourceOf(route),
            DisplayModeToken(route.DisplayMode));
    }

    /// <summary>Projects a route's passage values to a <see cref="PassageDto"/>.</summary>
    public static PassageDto Passage(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var p = route.Passage;
        return new PassageDto(
            p.DraftMeters,
            p.SquatMeters,
            p.SpeedKnots,
            p.Departure,
            p.RequiredUkcMeters,
            p.PortCrossTrackMeters,
            p.StarboardCrossTrackMeters,
            Vessel(route.Info.Vessel));
    }

    /// <summary>What each of a route's passage values is (#960).</summary>
    public static IReadOnlyList<PassageFieldDto> PassageFields(Route route)
    {
        var received = route.IsReadOnly;
        return
        [
            new("draftMeters", "checkSetting", route.IsCheckSettingFromFile(RoutePassageField.Draft), false),
            new("squatMeters", "checkSetting", route.IsCheckSettingFromFile(RoutePassageField.Squat), false),
            new("requiredUkcMeters", "checkSetting", route.IsCheckSettingFromFile(RoutePassageField.RequiredUkc), false),
            new("departure", "checkSetting", route.IsDepartureFromFile, false),
            new("speedKnots", "route", received, received),
            new("portCrossTrackMeters", "route", received, received),
            new("starboardCrossTrackMeters", "route", received, received),
        ];
    }

    private static LegPassageValuesDto LegValues(Func<RoutePassageField, double?> value)
        => new(
            value(RoutePassageField.Speed),
            value(RoutePassageField.Draft),
            value(RoutePassageField.PortCrossTrack),
            value(RoutePassageField.StarboardCrossTrack),
            value(RoutePassageField.RequiredUkc));

    private static VesselDto? Vessel(RouteVesselInfo? vessel)
        => vessel is { } v
            ? new VesselDto(v.Name, v.Mmsi, v.Imo, v.Callsign, v.LengthMeters, v.BeamMeters)
            : null;

    private static RouteInfoDto Info(RouteInfo info)
        => new(
            info.Name,
            info.Author,
            info.Description,
            info.DeparturePortId,
            info.ArrivalPortId,
            info.ValidityStart,
            info.ValidityEnd,
            Vessel(info.Vessel));
}
