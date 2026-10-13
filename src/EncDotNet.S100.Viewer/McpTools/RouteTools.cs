using System.ComponentModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Shared base for the agent-facing route tools. Holds the
/// <see cref="RoutesService"/> mutation surface and the
/// <see cref="IRouteEditInvoker"/> that marshals work onto the UI thread,
/// and provides route resolution plus coordinate validation common to the
/// family.
/// </summary>
/// <remarks>
/// Every tool resolves its target route by id, defaulting to the active
/// route when the id is omitted, and runs all model access inside
/// <see cref="IRouteEditInvoker.InvokeAsync{T}"/> so that the resulting
/// <c>Changed</c> fan-out (overlay redraw, panel rebuild) executes with UI
/// affinity. Every edit runs through the route's <see cref="Route.History"/>
/// as one step, so <c>undo_route_edit</c> (and Ctrl+Z in the viewer) can
/// revert it.
/// </remarks>
internal abstract class RouteToolBase
{
    internal const double MinLat = -90.0;
    internal const double MaxLat = 90.0;
    internal const double MinLon = -180.0;
    internal const double MaxLon = 180.0;

    /// <summary>The shared route mutation surface.</summary>
    protected RoutesService Routes { get; }

    /// <summary>Marshals model access onto the UI thread.</summary>
    protected IRouteEditInvoker Invoker { get; }

    /// <summary>Creates a new <see cref="RouteToolBase"/>.</summary>
    protected RouteToolBase(RoutesService routes, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(invoker);
        Routes = routes;
        Invoker = invoker;
    }

    /// <summary>
    /// Resolves the target route. When <paramref name="routeId"/> is null or
    /// whitespace the active route is used. Sets <paramref name="error"/> and
    /// returns <c>null</c> when the route cannot be resolved.
    /// </summary>
    protected Route? Resolve(string? routeId, out ToolError? error)
    {
        if (string.IsNullOrWhiteSpace(routeId))
        {
            var active = Routes.Routes.ActiveRoute;
            if (active is null)
            {
                error = new RouteNotFound("(active)");
                return null;
            }
            error = null;
            return active;
        }

        var route = Routes.Routes.FindById(routeId);
        if (route is null)
        {
            error = new RouteNotFound(routeId);
            return null;
        }
        error = null;
        return route;
    }

    /// <summary>Validates a latitude/longitude pair against the WGS-84 range.</summary>
    protected static ToolError? ValidateLatLon(double lat, double lon)
    {
        if (NotFinite(lat))
            return new InvalidArgument("lat", $"value {lat} is not a finite number");
        if (lat < MinLat || lat > MaxLat)
            return new InvalidArgument("lat", $"value {lat} is outside the WGS-84 range [{MinLat}, {MaxLat}]");
        if (NotFinite(lon))
            return new InvalidArgument("lon", $"value {lon} is not a finite number");
        if (lon < MinLon || lon > MaxLon)
            return new InvalidArgument("lon", $"value {lon} is outside the WGS-84 range [{MinLon}, {MaxLon}]");
        return null;
    }

    /// <summary>Applies any supplied waypoint metadata in place.</summary>
    protected static void ApplyWaypointMetadata(
        RouteWaypoint waypoint, int? number, string? name, bool? @fixed, double? turnRadiusNm)
    {
        if (number.HasValue) waypoint.Number = number;
        if (name is not null) waypoint.Name = name;
        if (@fixed.HasValue) waypoint.Fixed = @fixed;
        if (turnRadiusNm.HasValue) waypoint.TurnRadiusNm = turnRadiusNm;
    }

    /// <summary>Whether any waypoint-metadata field was supplied.</summary>
    protected static bool HasWaypointMetadata(int? number, string? name, bool? @fixed, double? turnRadiusNm)
        => number.HasValue || name is not null || @fixed.HasValue || turnRadiusNm.HasValue;

    private protected static bool NotFinite(double v) => double.IsNaN(v) || double.IsInfinity(v);

    private protected static ToolResult<RouteDetail> Ok(Route route, RoutesService routes)
        => ToolResult<RouteDetail>.Ok(RouteProjection.Detail(route, routes));
}

// ---------------------------------------------------------------------------
// create_route
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="CreateRouteTool"/>.</summary>
[Description("Request for create_route: create a new, empty route and make it active.")]
internal sealed record CreateRouteRequest(
    [property: Description("Optional route name.")] string? Name = null,
    [property: Description("Optional stable route id; a GUID is generated when omitted. Must be unique within the collection.")] string? Id = null);

/// <summary>
/// Creates a new editable route, adds it to the viewer's route collection,
/// and makes it the active route. The created route is empty; add waypoints
/// with <c>append_waypoint</c>.
/// </summary>
internal sealed class CreateRouteTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "create_route";

    /// <summary>Creates a new <see cref="CreateRouteTool"/>.</summary>
    public CreateRouteTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        CreateRouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Invoker.InvokeAsync(() =>
        {
            Route route;
            try
            {
                route = Routes.Routes.CreateRoute(request.Name, request.Id);
            }
            catch (ArgumentException ex)
            {
                return ToolResult<RouteDetail>.Err(new InvalidArgument("id", ex.Message));
            }
            return Ok(route, Routes);
        });
    }
}

// ---------------------------------------------------------------------------
// list_routes
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="ListRoutesTool"/> (no parameters).</summary>
[Description("Request for list_routes: no parameters.")]
internal sealed record ListRoutesRequest();

/// <summary>Lists every route in the viewer's collection with aggregate metrics.</summary>
internal sealed class ListRoutesTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "list_routes";

    /// <summary>Creates a new <see cref="ListRoutesTool"/>.</summary>
    public ListRoutesTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<ListRoutesResult>> InvokeAsync(
        ListRoutesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Invoker.InvokeAsync(() =>
        {
            var summaries = new System.Collections.Generic.List<RouteSummary>(Routes.Routes.Routes.Count);
            foreach (var route in Routes.Routes.Routes)
                summaries.Add(RouteProjection.Summary(route, Routes));
            return ToolResult<ListRoutesResult>.Ok(
                new ListRoutesResult(summaries, Routes.Routes.ActiveRoute?.Id, Routes.IsEditModeActive, Routes.IsPassageCardExpanded));
        });
    }
}

// ---------------------------------------------------------------------------
// get_route
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="GetRouteTool"/>.</summary>
[Description("Request for get_route: return the full state of one route.")]
internal sealed record GetRouteRequest(
    [property: Description("Id of the route to return; omit to use the active route.")] string? RouteId = null);

/// <summary>Returns the full state of a single route.</summary>
internal sealed class GetRouteTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "get_route";

    /// <summary>Creates a new <see cref="GetRouteTool"/>.</summary>
    public GetRouteTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        GetRouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            return route is null
                ? ToolResult<RouteDetail>.Err(error!)
                : Ok(route, Routes);
        });
    }
}

// ---------------------------------------------------------------------------
// delete_route
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="DeleteRouteTool"/>.</summary>
[Description("Request for delete_route: remove a route from the collection.")]
internal sealed record DeleteRouteRequest(
    [property: Description("Id of the route to delete; omit to delete the active route.")] string? RouteId = null);

/// <summary>Removes a route from the viewer's collection.</summary>
internal sealed class DeleteRouteTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "delete_route";

    /// <summary>Creates a new <see cref="DeleteRouteTool"/>.</summary>
    public DeleteRouteTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<DeleteRouteResult>> InvokeAsync(
        DeleteRouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<DeleteRouteResult>.Err(error!);

            var removed = Routes.Routes.Remove(route);
            return ToolResult<DeleteRouteResult>.Ok(
                new DeleteRouteResult(route.Id, removed, Routes.Routes.ActiveRoute?.Id));
        });
    }
}

// ---------------------------------------------------------------------------
// append_waypoint
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="AppendWaypointTool"/>.</summary>
[Description("Request for append_waypoint: add a waypoint to the end of a route.")]
internal sealed record AppendWaypointRequest(
    [property: Description("WGS-84 latitude in decimal degrees [-90, 90].")] double Lat,
    [property: Description("WGS-84 longitude in decimal degrees [-180, 180].")] double Lon,
    [property: Description("Id of the route to append to; omit to use the active route.")] string? RouteId = null,
    [property: Description("Optional author-assigned ordinal (S-421 routeWaypointID).")] int? Number = null,
    [property: Description("Optional waypoint name.")] string? Name = null,
    [property: Description("Optional flag pinning the waypoint so it must not be moved.")] bool? Fixed = null,
    [property: Description("Optional planned turn radius at the waypoint, in nautical miles.")] double? TurnRadiusNm = null);

/// <summary>Appends a waypoint to the end of a route.</summary>
internal sealed class AppendWaypointTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "append_waypoint";

    /// <summary>Creates a new <see cref="AppendWaypointTool"/>.</summary>
    public AppendWaypointTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        AppendWaypointRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateLatLon(request.Lat, request.Lon) is { } latLonError)
            return Task.FromResult(ToolResult<RouteDetail>.Err(latLonError));

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);

            route.History.Execute(RouteEditLabels.AppendWaypoint, () =>
            {
                var waypoint = route.AppendWaypoint(new GeoPosition(request.Lat, request.Lon));
                if (HasWaypointMetadata(request.Number, request.Name, request.Fixed, request.TurnRadiusNm))
                {
                    ApplyWaypointMetadata(waypoint, request.Number, request.Name, request.Fixed, request.TurnRadiusNm);
                    route.NotifyChanged();
                }
            });
            return Ok(route, Routes);
        });
    }
}

// ---------------------------------------------------------------------------
// insert_waypoint
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="InsertWaypointTool"/>.</summary>
[Description("Request for insert_waypoint: insert a waypoint at a position in a route, splitting the affected leg.")]
internal sealed record InsertWaypointRequest(
    [property: Description("Insertion index in [0, waypointCount]; 0 prepends, waypointCount appends.")] int Index,
    [property: Description("WGS-84 latitude in decimal degrees [-90, 90].")] double Lat,
    [property: Description("WGS-84 longitude in decimal degrees [-180, 180].")] double Lon,
    [property: Description("Id of the route to insert into; omit to use the active route.")] string? RouteId = null,
    [property: Description("Optional author-assigned ordinal (S-421 routeWaypointID).")] int? Number = null,
    [property: Description("Optional waypoint name.")] string? Name = null,
    [property: Description("Optional flag pinning the waypoint so it must not be moved.")] bool? Fixed = null,
    [property: Description("Optional planned turn radius at the waypoint, in nautical miles.")] double? TurnRadiusNm = null);

/// <summary>Inserts a waypoint at a given index of a route.</summary>
internal sealed class InsertWaypointTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "insert_waypoint";

    /// <summary>Creates a new <see cref="InsertWaypointTool"/>.</summary>
    public InsertWaypointTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        InsertWaypointRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateLatLon(request.Lat, request.Lon) is { } latLonError)
            return Task.FromResult(ToolResult<RouteDetail>.Err(latLonError));

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);

            if (request.Index < 0 || request.Index > route.Waypoints.Count)
                return ToolResult<RouteDetail>.Err(new InvalidArgument(
                    "index",
                    $"value {request.Index} is outside the insertable range [0, {route.Waypoints.Count}]"));

            route.History.Execute(RouteEditLabels.InsertWaypoint, () =>
            {
                var waypoint = route.InsertWaypoint(request.Index, new GeoPosition(request.Lat, request.Lon));
                if (HasWaypointMetadata(request.Number, request.Name, request.Fixed, request.TurnRadiusNm))
                {
                    ApplyWaypointMetadata(waypoint, request.Number, request.Name, request.Fixed, request.TurnRadiusNm);
                    route.NotifyChanged();
                }
            });
            return Ok(route, Routes);
        });
    }
}

// ---------------------------------------------------------------------------
// move_waypoint
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="MoveWaypointTool"/>.</summary>
[Description("Request for move_waypoint: move an existing waypoint to a new position.")]
internal sealed record MoveWaypointRequest(
    [property: Description("Index of the waypoint to move in [0, waypointCount).")] int Index,
    [property: Description("New WGS-84 latitude in decimal degrees [-90, 90].")] double Lat,
    [property: Description("New WGS-84 longitude in decimal degrees [-180, 180].")] double Lon,
    [property: Description("Id of the route to edit; omit to use the active route.")] string? RouteId = null);

/// <summary>Moves an existing waypoint to a new position.</summary>
internal sealed class MoveWaypointTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "move_waypoint";

    /// <summary>Creates a new <see cref="MoveWaypointTool"/>.</summary>
    public MoveWaypointTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        MoveWaypointRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (ValidateLatLon(request.Lat, request.Lon) is { } latLonError)
            return Task.FromResult(ToolResult<RouteDetail>.Err(latLonError));

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);

            if (request.Index < 0 || request.Index >= route.Waypoints.Count)
                return ToolResult<RouteDetail>.Err(new InvalidArgument(
                    "index",
                    $"value {request.Index} is outside the waypoint range [0, {route.Waypoints.Count - 1}]"));

            route.History.Execute(
                RouteEditLabels.MoveWaypoint,
                () => route.MoveWaypoint(request.Index, new GeoPosition(request.Lat, request.Lon)));
            return Ok(route, Routes);
        });
    }
}

// ---------------------------------------------------------------------------
// delete_waypoint
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="DeleteWaypointTool"/>.</summary>
[Description("Request for delete_waypoint: remove a waypoint, merging the adjacent legs.")]
internal sealed record DeleteWaypointRequest(
    [property: Description("Index of the waypoint to remove in [0, waypointCount).")] int Index,
    [property: Description("Id of the route to edit; omit to use the active route.")] string? RouteId = null);

/// <summary>Removes a waypoint from a route.</summary>
internal sealed class DeleteWaypointTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "delete_waypoint";

    /// <summary>Creates a new <see cref="DeleteWaypointTool"/>.</summary>
    public DeleteWaypointTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        DeleteWaypointRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);

            if (request.Index < 0 || request.Index >= route.Waypoints.Count)
                return ToolResult<RouteDetail>.Err(new InvalidArgument(
                    "index",
                    $"value {request.Index} is outside the waypoint range [0, {route.Waypoints.Count - 1}]"));

            route.History.Execute(RouteEditLabels.DeleteWaypoint, () => route.RemoveWaypoint(request.Index));
            return Ok(route, Routes);
        });
    }
}

// ---------------------------------------------------------------------------
// set_leg_attributes
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SetLegAttributesTool"/>.</summary>
/// <remarks>
/// Every attribute is optional. A supplied value overwrites; an omitted
/// (null) value is left unchanged; a name listed in <see cref="Clear"/> is
/// unset. The passage fields (<see cref="SpeedKnots"/>,
/// <see cref="DraftMeters"/>, the cross-track limits and
/// <see cref="StaticUnderKeelClearanceMeters"/>) are leg overrides of the
/// route's passage: a value equal to the passage value stores no override,
/// and clearing one resets the leg to inherit.
/// </remarks>
[Description("Request for set_leg_attributes: update one leg's geometry type, passage overrides and navigational envelope. All attributes optional; supplied values overwrite, omitted values are left unchanged, names in 'clear' are unset.")]
internal sealed record SetLegAttributesRequest(
    [property: Description("Index of the leg to edit in [0, legCount); leg i joins waypoint i to waypoint i+1.")] int LegIndex,
    [property: Description("Id of the route to edit; omit to use the active route.")] string? RouteId = null,
    [property: Description("Leg path geometry: \"loxodrome\" (rhumb line) or \"geodesic\" (great circle).")] string? GeometryType = null,
    [property: Description("Override: starboard cross-track distance limit in metres.")] double? StarboardCrossTrackDistanceLimitMeters = null,
    [property: Description("Override: port cross-track distance limit in metres.")] double? PortCrossTrackDistanceLimitMeters = null,
    [property: Description("Starboard channel limit in metres.")] double? StarboardChannelLimitMeters = null,
    [property: Description("Port channel limit in metres.")] double? PortChannelLimitMeters = null,
    [property: Description("Safety contour for the leg in metres.")] double? SafetyContourMeters = null,
    [property: Description("Safety depth for the leg in metres.")] double? SafetyDepthMeters = null,
    [property: Description("Minimum speed over ground in knots.")] double? SpeedOverGroundMinKnots = null,
    [property: Description("Maximum speed over ground in knots.")] double? SpeedOverGroundMaxKnots = null,
    [property: Description("Minimum speed through water in knots.")] double? SpeedThroughWaterMinKnots = null,
    [property: Description("Maximum speed through water in knots.")] double? SpeedThroughWaterMaxKnots = null,
    [property: Description("Override: draft for the leg in metres.")] double? DraftMeters = null,
    [property: Description("Override: required static under-keel clearance in metres.")] double? StaticUnderKeelClearanceMeters = null,
    [property: Description("Dynamic under-keel clearance in metres.")] double? DynamicUnderKeelClearanceMeters = null,
    [property: Description("Safety margin in metres.")] double? SafetyMarginMeters = null,
    [property: Description("Free-text note for the leg.")] string? Note = null,
    [property: Description("Override: planned speed over ground for the leg in knots (> 0).")] double? SpeedKnots = null,
    [property: Description("Names of attributes to unset, using this tool's parameter names (for example \"speedKnots\", \"draftMeters\", \"note\"). Unsetting a passage override resets the leg to inherit the route's passage value.")] IReadOnlyList<string>? Clear = null);

/// <summary>
/// Updates a single route leg's geometry type, passage overrides and
/// navigational envelope.
/// </summary>
internal sealed class SetLegAttributesTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "set_leg_attributes";

    // Parameter name → passage field, for the leg's passage overrides.
    private static readonly (string Name, RoutePassageField Field)[] OverrideParameters =
    [
        ("speedKnots", RoutePassageField.Speed),
        ("draftMeters", RoutePassageField.Draft),
        ("portCrossTrackDistanceLimitMeters", RoutePassageField.PortCrossTrack),
        ("starboardCrossTrackDistanceLimitMeters", RoutePassageField.StarboardCrossTrack),
        ("staticUnderKeelClearanceMeters", RoutePassageField.RequiredUkc),
    ];

    // Parameter name → setter, for the plain (non-inheriting) leg attributes.
    private static readonly (string Name, Action<RouteLeg, double?> Set)[] EnvelopeParameters =
    [
        ("starboardChannelLimitMeters", (l, v) => l.StarboardChannelLimitMeters = v),
        ("portChannelLimitMeters", (l, v) => l.PortChannelLimitMeters = v),
        ("safetyContourMeters", (l, v) => l.SafetyContourMeters = v),
        ("safetyDepthMeters", (l, v) => l.SafetyDepthMeters = v),
        ("speedOverGroundMinKnots", (l, v) => l.SpeedOverGroundMinKnots = v),
        ("speedOverGroundMaxKnots", (l, v) => l.SpeedOverGroundMaxKnots = v),
        ("speedThroughWaterMinKnots", (l, v) => l.SpeedThroughWaterMinKnots = v),
        ("speedThroughWaterMaxKnots", (l, v) => l.SpeedThroughWaterMaxKnots = v),
        ("dynamicUnderKeelClearanceMeters", (l, v) => l.DynamicUnderKeelClearanceMeters = v),
        ("safetyMarginMeters", (l, v) => l.SafetyMarginMeters = v),
    ];

    /// <summary>Creates a new <see cref="SetLegAttributesTool"/>.</summary>
    public SetLegAttributesTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>The names <see cref="SetLegAttributesRequest.Clear"/> accepts.</summary>
    internal static IEnumerable<string> ClearableNames
        => OverrideParameters.Select(p => p.Name).Concat(EnvelopeParameters.Select(p => p.Name)).Append("note");

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        SetLegAttributesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        RouteLegGeometryType? geometry = null;
        if (request.GeometryType is not null)
        {
            if (!TryParseGeometry(request.GeometryType, out var parsed))
                return Task.FromResult(ToolResult<RouteDetail>.Err(new InvalidArgument(
                    "geometryType",
                    $"value '{request.GeometryType}' is not one of: loxodrome, geodesic")));
            geometry = parsed;
        }

        // In OverrideParameters order.
        var overrides = new[]
        {
            request.SpeedKnots, request.DraftMeters,
            request.PortCrossTrackDistanceLimitMeters, request.StarboardCrossTrackDistanceLimitMeters,
            request.StaticUnderKeelClearanceMeters,
        };
        // In EnvelopeParameters order.
        var envelope = new double?[]
        {
            request.StarboardChannelLimitMeters, request.PortChannelLimitMeters,
            request.SafetyContourMeters, request.SafetyDepthMeters,
            request.SpeedOverGroundMinKnots, request.SpeedOverGroundMaxKnots,
            request.SpeedThroughWaterMinKnots, request.SpeedThroughWaterMaxKnots,
            request.DynamicUnderKeelClearanceMeters, request.SafetyMarginMeters,
        };

        for (var i = 0; i < overrides.Length; i++)
        {
            var (name, field) = OverrideParameters[i];
            if (PassageValues.Validate(name, field, overrides[i]) is { } invalid)
                return Task.FromResult(ToolResult<RouteDetail>.Err(invalid));
        }
        for (var i = 0; i < envelope.Length; i++)
        {
            if (envelope[i] is { } v && NotFinite(v))
                return Task.FromResult(ToolResult<RouteDetail>.Err(new InvalidArgument(
                    EnvelopeParameters[i].Name, $"value {v} is not a finite number")));
        }

        var supplied = OverrideParameters.Where((_, i) => overrides[i].HasValue).Select(p => p.Name)
            .Concat(EnvelopeParameters.Where((_, i) => envelope[i].HasValue).Select(p => p.Name));
        if (request.Note is not null)
            supplied = supplied.Append("note");
        if (PassageValues.ValidateClear(request.Clear, ClearableNames, supplied) is { } clearError)
            return Task.FromResult(ToolResult<RouteDetail>.Err(clearError));
        var clear = PassageValues.ClearSet(request.Clear);

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);

            if (request.LegIndex < 0 || request.LegIndex >= route.Legs.Count)
                return ToolResult<RouteDetail>.Err(new InvalidArgument(
                    "legIndex",
                    $"value {request.LegIndex} is outside the leg range [0, {route.Legs.Count - 1}]"));

            var leg = route.Legs[request.LegIndex];
            route.History.Execute(RouteEditLabels.EditLeg, () =>
            {
                // Passage overrides go through the route so the "only when it
                // differs" rule holds.
                for (var i = 0; i < overrides.Length; i++)
                {
                    var (name, field) = OverrideParameters[i];
                    if (clear.Contains(name))
                        route.ResetLegOverride(request.LegIndex, field);
                    else if (overrides[i].HasValue)
                        route.SetLegOverride(request.LegIndex, field, overrides[i]);
                }

                // The plain attributes are set in place; notify only when one
                // changed, so a no-op call records no undo step.
                var before = leg.Clone();
                if (geometry.HasValue) leg.GeometryType = geometry.Value;
                for (var i = 0; i < envelope.Length; i++)
                {
                    var (name, set) = EnvelopeParameters[i];
                    if (clear.Contains(name))
                        set(leg, null);
                    else if (envelope[i].HasValue)
                        set(leg, envelope[i]);
                }
                if (clear.Contains("note")) leg.Note = null;
                else if (request.Note is not null) leg.Note = request.Note;

                if (!before.HasSameAttributes(leg))
                    route.NotifyChanged();
            });
            return Ok(route, Routes);
        });
    }

    private static bool TryParseGeometry(string value, out RouteLegGeometryType type)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "loxodrome":
            case "rhumb":
            case "rhumbline":
                type = RouteLegGeometryType.Loxodrome;
                return true;
            case "geodesic":
            case "greatcircle":
            case "great_circle":
                type = RouteLegGeometryType.Geodesic;
                return true;
            default:
                type = RouteLegGeometryType.Loxodrome;
                return false;
        }
    }
}

// ---------------------------------------------------------------------------
// set_route_info
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SetRouteInfoTool"/>.</summary>
/// <remarks>
/// Every field is optional and overwrites the corresponding route-info value
/// when supplied; omitted fields are left unchanged. Supplying any vessel
/// field lazily creates the vessel block.
/// </remarks>
[Description("Request for set_route_info: update a route's metadata (name, author, ports, validity) and vessel particulars. All fields optional; supplied values overwrite, omitted values are left unchanged.")]
internal sealed record SetRouteInfoRequest(
    [property: Description("Id of the route to edit; omit to use the active route.")] string? RouteId = null,
    [property: Description("Route name.")] string? Name = null,
    [property: Description("Route author / originator.")] string? Author = null,
    [property: Description("Free-text route description.")] string? Description = null,
    [property: Description("Departure port identifier.")] string? DeparturePortId = null,
    [property: Description("Arrival port identifier.")] string? ArrivalPortId = null,
    [property: Description("Planned start of validity (UTC ISO-8601).")] DateTimeOffset? ValidityStart = null,
    [property: Description("Planned end of validity (UTC ISO-8601).")] DateTimeOffset? ValidityEnd = null,
    [property: Description("Vessel name (creates the vessel block when supplied).")] string? VesselName = null,
    [property: Description("Vessel MMSI (creates the vessel block when supplied).")] string? VesselMmsi = null,
    [property: Description("Vessel IMO number (creates the vessel block when supplied).")] string? VesselImo = null,
    [property: Description("Vessel call sign (creates the vessel block when supplied).")] string? VesselCallsign = null,
    [property: Description("Vessel overall length in metres (creates the vessel block when supplied).")] double? VesselLengthMeters = null,
    [property: Description("Vessel beam in metres (creates the vessel block when supplied).")] double? VesselBeamMeters = null);

/// <summary>Updates a route's metadata and vessel particulars.</summary>
internal sealed class SetRouteInfoTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "set_route_info";

    /// <summary>Creates a new <see cref="SetRouteInfoTool"/>.</summary>
    public SetRouteInfoTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        SetRouteInfoRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);

            route.History.Execute(RouteEditLabels.EditRouteInfo, () => Apply(route, request));
            return Ok(route, Routes);
        });
    }

    private static void Apply(Route route, SetRouteInfoRequest request)
    {
        var info = route.Info;
        if (request.Name is not null) info.Name = request.Name;
        if (request.Author is not null) info.Author = request.Author;
        if (request.Description is not null) info.Description = request.Description;
        if (request.DeparturePortId is not null) info.DeparturePortId = request.DeparturePortId;
        if (request.ArrivalPortId is not null) info.ArrivalPortId = request.ArrivalPortId;
        if (request.ValidityStart.HasValue) info.ValidityStart = request.ValidityStart;
        if (request.ValidityEnd.HasValue) info.ValidityEnd = request.ValidityEnd;

        var hasVesselField = request.VesselName is not null || request.VesselMmsi is not null
            || request.VesselImo is not null || request.VesselCallsign is not null
            || request.VesselLengthMeters.HasValue || request.VesselBeamMeters.HasValue;
        if (hasVesselField)
        {
            var vessel = info.Vessel ??= new RouteVesselInfo();
            if (request.VesselName is not null) vessel.Name = request.VesselName;
            if (request.VesselMmsi is not null) vessel.Mmsi = request.VesselMmsi;
            if (request.VesselImo is not null) vessel.Imo = request.VesselImo;
            if (request.VesselCallsign is not null) vessel.Callsign = request.VesselCallsign;
            if (request.VesselLengthMeters.HasValue) vessel.LengthMeters = request.VesselLengthMeters;
            if (request.VesselBeamMeters.HasValue) vessel.BeamMeters = request.VesselBeamMeters;
        }

        route.NotifyChanged();
    }
}

// ---------------------------------------------------------------------------
// undo_route_edit / redo_route_edit
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="UndoRouteEditTool"/> and <see cref="RedoRouteEditTool"/>.</summary>
[Description("Request for undo_route_edit / redo_route_edit: the route whose edit history to step.")]
internal sealed record RouteHistoryRequest(
    [property: Description("Id of the route; omit to use the active route.")] string? RouteId = null);

/// <summary>
/// Undoes the most recent edit to a route: the same per-route history as
/// Ctrl+Z (⌘Z) in the viewer, which records edits made in the viewer and
/// through the route tools alike.
/// </summary>
internal sealed class UndoRouteEditTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "undo_route_edit";

    /// <summary>Creates a new <see cref="UndoRouteEditTool"/>.</summary>
    public UndoRouteEditTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        RouteHistoryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);
            return route.History.Undo()
                ? Ok(route, Routes)
                : ToolResult<RouteDetail>.Err(new RouteHistoryEmpty(route.Id, "undo"));
        });
    }
}

/// <summary>Redoes the most recently undone edit to a route (Ctrl+Shift+Z / ⇧⌘Z in the viewer).</summary>
internal sealed class RedoRouteEditTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "redo_route_edit";

    /// <summary>Creates a new <see cref="RedoRouteEditTool"/>.</summary>
    public RedoRouteEditTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        RouteHistoryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);
            return route.History.Redo()
                ? Ok(route, Routes)
                : ToolResult<RouteDetail>.Err(new RouteHistoryEmpty(route.Id, "redo"));
        });
    }
}

// ---------------------------------------------------------------------------
// set_route_edit_mode
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SetRouteEditModeTool"/>.</summary>
[Description("Request for set_route_edit_mode: turn the interactive route editor on or off.")]
internal sealed record SetRouteEditModeRequest(
    [property: Description("True to turn route edit mode on (map clicks add waypoints); false to turn it off.")] bool Enabled);

/// <summary>
/// Turns the interactive route editor on or off, as the Routes panel's Edit
/// toggle and Ctrl+E (⌘E) do. Read the current mode from <c>list_routes</c>'s
/// <c>editMode</c>.
/// </summary>
internal sealed class SetRouteEditModeTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "set_route_edit_mode";

    /// <summary>Creates a new <see cref="SetRouteEditModeTool"/>.</summary>
    public SetRouteEditModeTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteEditModeResult>> InvokeAsync(
        SetRouteEditModeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Invoker.InvokeAsync(() =>
        {
            Routes.IsEditModeActive = request.Enabled;
            return ToolResult<RouteEditModeResult>.Ok(
                new RouteEditModeResult(Routes.IsEditModeActive, Routes.Routes.ActiveRoute?.Id));
        });
    }
}

// ---------------------------------------------------------------------------
// Passage value validation shared by set_passage and set_leg_attributes
// ---------------------------------------------------------------------------

/// <summary>Validation shared by the passage-editing tools.</summary>
internal static class PassageValues
{
    /// <summary>
    /// Validates one passage value: finite, non-negative, and positive for a
    /// speed. Returns <c>null</c> when <paramref name="value"/> is valid or absent.
    /// </summary>
    public static ToolError? Validate(string parameter, RoutePassageField field, double? value)
    {
        if (value is not { } v)
            return null;
        if (double.IsNaN(v) || double.IsInfinity(v))
            return new InvalidArgument(parameter, $"value {v} is not a finite number");
        if (field == RoutePassageField.Speed ? v <= 0 : v < 0)
            return new InvalidArgument(
                parameter,
                field == RoutePassageField.Speed
                    ? $"value {v} must be greater than 0"
                    : $"value {v} must not be negative");
        return null;
    }

    /// <summary>
    /// Validates a <c>clear</c> list: every name is known, and none is also
    /// supplied as a value in the same call.
    /// </summary>
    public static ToolError? ValidateClear(
        IReadOnlyList<string>? clear, IEnumerable<string> known, IEnumerable<string> supplied)
    {
        if (clear is null || clear.Count == 0)
            return null;

        var knownSet = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        foreach (var name in clear)
        {
            if (!knownSet.Contains(name))
                return new InvalidArgument(
                    "clear",
                    $"'{name}' is not one of: {string.Join(", ", knownSet)}");
        }

        var suppliedSet = new HashSet<string>(supplied, StringComparer.OrdinalIgnoreCase);
        foreach (var name in clear)
        {
            if (suppliedSet.Contains(name))
                return new InvalidArgument("clear", $"'{name}' is both set and cleared");
        }
        return null;
    }

    /// <summary>The <c>clear</c> list as a case-insensitive set.</summary>
    public static HashSet<string> ClearSet(IReadOnlyList<string>? clear)
        => new(clear ?? [], StringComparer.OrdinalIgnoreCase);
}

// ---------------------------------------------------------------------------
// get_passage
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="GetPassageTool"/>.</summary>
[Description("Request for get_passage: return a route's passage values.")]
internal sealed record GetPassageRequest(
    [property: Description("Id of the route to read; omit to use the active route.")] string? RouteId = null);

/// <summary>Returns a route's passage values.</summary>
internal sealed class GetPassageTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "get_passage";

    /// <summary>Creates a new <see cref="GetPassageTool"/>.</summary>
    public GetPassageTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<GetPassageResult>> InvokeAsync(
        GetPassageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            return route is null
                ? ToolResult<GetPassageResult>.Err(error!)
                : ToolResult<GetPassageResult>.Ok(new GetPassageResult(route.Id, RouteProjection.Passage(route)));
        });
    }
}

// ---------------------------------------------------------------------------
// set_passage
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SetPassageTool"/>.</summary>
/// <remarks>
/// Every value is optional. A supplied value overwrites; an omitted (null)
/// value is left unchanged; a name listed in <see cref="Clear"/> is unset.
/// Legs inherit each value unless they override it, so a change reaches only
/// the inheriting legs.
/// </remarks>
[Description("Request for set_passage: update a route's passage values, which legs inherit unless they override them. All values optional; supplied values overwrite, omitted values are left unchanged, names in 'clear' are unset.")]
internal sealed record SetPassageRequest(
    [property: Description("Id of the route to edit; omit to use the active route.")] string? RouteId = null,
    [property: Description("Draft in metres (>= 0).")] double? DraftMeters = null,
    [property: Description("Fixed squat allowance in metres (>= 0).")] double? SquatMeters = null,
    [property: Description("Planned speed over ground in knots (> 0).")] double? SpeedKnots = null,
    [property: Description("Planned departure (ISO-8601). Unset means ETAs use the view time.")] DateTimeOffset? Departure = null,
    [property: Description("Required static under-keel clearance in metres (>= 0).")] double? RequiredUkcMeters = null,
    [property: Description("Cross-track distance limit in metres for both port and starboard (>= 0). Don't combine with portCrossTrackMeters or starboardCrossTrackMeters.")] double? CrossTrackMeters = null,
    [property: Description("Port cross-track distance limit in metres (>= 0).")] double? PortCrossTrackMeters = null,
    [property: Description("Starboard cross-track distance limit in metres (>= 0).")] double? StarboardCrossTrackMeters = null,
    [property: Description("Names of values to unset: draftMeters, squatMeters, speedKnots, departure, requiredUkcMeters, crossTrackMeters (both sides), portCrossTrackMeters, starboardCrossTrackMeters.")] IReadOnlyList<string>? Clear = null);

/// <summary>Updates a route's passage values.</summary>
internal sealed class SetPassageTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "set_passage";

    private static readonly string[] ClearableNames =
    [
        "draftMeters", "squatMeters", "speedKnots", "departure", "requiredUkcMeters",
        "crossTrackMeters", "portCrossTrackMeters", "starboardCrossTrackMeters",
    ];

    /// <summary>Creates a new <see cref="SetPassageTool"/>.</summary>
    public SetPassageTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        SetPassageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.CrossTrackMeters.HasValue
            && (request.PortCrossTrackMeters.HasValue || request.StarboardCrossTrackMeters.HasValue))
            return Task.FromResult(ToolResult<RouteDetail>.Err(new InvalidArgument(
                "crossTrackMeters",
                "set both sides with crossTrackMeters, or each side with portCrossTrackMeters and starboardCrossTrackMeters, not both")));

        var port = request.CrossTrackMeters ?? request.PortCrossTrackMeters;
        var starboard = request.CrossTrackMeters ?? request.StarboardCrossTrackMeters;
        var values = new (string Name, RoutePassageField Field, double? Value)[]
        {
            ("draftMeters", RoutePassageField.Draft, request.DraftMeters),
            ("squatMeters", RoutePassageField.Squat, request.SquatMeters),
            ("speedKnots", RoutePassageField.Speed, request.SpeedKnots),
            ("requiredUkcMeters", RoutePassageField.RequiredUkc, request.RequiredUkcMeters),
            (request.CrossTrackMeters.HasValue ? "crossTrackMeters" : "portCrossTrackMeters", RoutePassageField.PortCrossTrack, port),
            (request.CrossTrackMeters.HasValue ? "crossTrackMeters" : "starboardCrossTrackMeters", RoutePassageField.StarboardCrossTrack, starboard),
        };

        foreach (var (name, field, value) in values)
        {
            if (PassageValues.Validate(name, field, value) is { } invalid)
                return Task.FromResult(ToolResult<RouteDetail>.Err(invalid));
        }

        var supplied = values.Where(v => v.Value.HasValue).Select(v => v.Name).ToList();
        if (request.Departure.HasValue)
            supplied.Add("departure");
        // Clearing both sides conflicts with setting either side, and the reverse.
        if (port.HasValue || starboard.HasValue)
            supplied.Add("crossTrackMeters");
        if (request.CrossTrackMeters.HasValue)
            supplied.AddRange(["portCrossTrackMeters", "starboardCrossTrackMeters"]);
        if (PassageValues.ValidateClear(request.Clear, ClearableNames, supplied) is { } clearError)
            return Task.FromResult(ToolResult<RouteDetail>.Err(clearError));

        var clear = PassageValues.ClearSet(request.Clear);
        if (clear.Contains("crossTrackMeters"))
        {
            clear.Add("portCrossTrackMeters");
            clear.Add("starboardCrossTrackMeters");
        }

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);

            route.History.Execute(RouteEditLabels.EditPassage, () =>
            {
                foreach (var (_, field, value) in values)
                {
                    if (clear.Contains(ClearName(field)))
                        route.SetPassageValue(field, null);
                    else if (value.HasValue)
                        route.SetPassageValue(field, value);
                }

                if (clear.Contains("departure"))
                    route.SetDeparture(null);
                else if (request.Departure.HasValue)
                    route.SetDeparture(request.Departure);
            });

            return Ok(route, Routes);
        });
    }

    private static string ClearName(RoutePassageField field) => field switch
    {
        RoutePassageField.Draft => "draftMeters",
        RoutePassageField.Squat => "squatMeters",
        RoutePassageField.Speed => "speedKnots",
        RoutePassageField.RequiredUkc => "requiredUkcMeters",
        RoutePassageField.PortCrossTrack => "portCrossTrackMeters",
        _ => "starboardCrossTrackMeters",
    };
}

// ---------------------------------------------------------------------------
// select_route_item
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SelectRouteItemTool"/>.</summary>
[Description("Request for select_route_item: select a waypoint or a leg of a route, or clear the selection.")]
internal sealed record SelectRouteItemRequest(
    [property: Description("Id of the route; omit to use the active route. Selecting makes the route active.")] string? RouteId = null,
    [property: Description("Index of the waypoint to select. Give this or legIndex, not both; give neither to clear the selection.")] int? WaypointIndex = null,
    [property: Description("Index of the leg to select (leg i joins waypoint i to i+1). Give this or waypointIndex, not both.")] int? LegIndex = null);

/// <summary>
/// Selects a waypoint or a leg, as clicking its row in the Routes panel does.
/// The map highlights the selected waypoint, or haloes the selected leg and
/// always shows its label. <c>get_route</c> reports the selection.
/// </summary>
internal sealed class SelectRouteItemTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "select_route_item";

    /// <summary>Creates a new <see cref="SelectRouteItemTool"/>.</summary>
    public SelectRouteItemTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        SelectRouteItemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.WaypointIndex.HasValue && request.LegIndex.HasValue)
            return Task.FromResult(ToolResult<RouteDetail>.Err(new InvalidArgument(
                "legIndex", "give waypointIndex or legIndex, not both")));

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);

            if (request.WaypointIndex is { } w && (w < 0 || w >= route.Waypoints.Count))
                return ToolResult<RouteDetail>.Err(new InvalidArgument(
                    "waypointIndex", $"value {w} is outside the waypoint range [0, {route.Waypoints.Count - 1}]"));
            if (request.LegIndex is { } l && (l < 0 || l >= route.Legs.Count))
                return ToolResult<RouteDetail>.Err(new InvalidArgument(
                    "legIndex", $"value {l} is outside the leg range [0, {route.Legs.Count - 1}]"));

            Routes.Routes.SetActiveRoute(route);
            if (request.LegIndex is { } leg)
            {
                Routes.SelectedLegIndex = leg;
            }
            else
            {
                Routes.SelectedLegIndex = null;
                Routes.SelectedWaypointIndex = request.WaypointIndex;
            }
            return Ok(route, Routes);
        });
    }
}

// ---------------------------------------------------------------------------
// set_passage_card
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SetPassageCardTool"/>.</summary>
[Description("Request for set_passage_card: expand or collapse the Routes panel's Passage card.")]
internal sealed record SetPassageCardRequest(
    [property: Description("True to expand the Passage card, false to collapse it to its one-line summary.")] bool Expanded);

/// <summary>
/// Expands or collapses the Routes panel's Passage card, as its header does.
/// The card resets when the active route changes: collapsed for a route with
/// no passage values, expanded otherwise. Read the state from
/// <c>list_routes</c>'s <c>passageCardExpanded</c>.
/// </summary>
internal sealed class SetPassageCardTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "set_passage_card";

    /// <summary>Creates a new <see cref="SetPassageCardTool"/>.</summary>
    public SetPassageCardTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<PassageCardResult>> InvokeAsync(
        SetPassageCardRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Invoker.InvokeAsync(() =>
        {
            Routes.IsPassageCardExpanded = request.Expanded;
            return ToolResult<PassageCardResult>.Ok(new PassageCardResult(Routes.IsPassageCardExpanded));
        });
    }
}
