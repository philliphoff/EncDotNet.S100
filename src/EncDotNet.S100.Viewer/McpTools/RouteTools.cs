using System.ComponentModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Mcp.Tools;
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
/// affinity.
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
                new ListRoutesResult(summaries, Routes.Routes.ActiveRoute?.Id));
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

            var waypoint = route.AppendWaypoint(new GeoPosition(request.Lat, request.Lon));
            if (HasWaypointMetadata(request.Number, request.Name, request.Fixed, request.TurnRadiusNm))
            {
                ApplyWaypointMetadata(waypoint, request.Number, request.Name, request.Fixed, request.TurnRadiusNm);
                route.NotifyChanged();
            }
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

            var waypoint = route.InsertWaypoint(request.Index, new GeoPosition(request.Lat, request.Lon));
            if (HasWaypointMetadata(request.Number, request.Name, request.Fixed, request.TurnRadiusNm))
            {
                ApplyWaypointMetadata(waypoint, request.Number, request.Name, request.Fixed, request.TurnRadiusNm);
                route.NotifyChanged();
            }
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

            route.MoveWaypoint(request.Index, new GeoPosition(request.Lat, request.Lon));
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

            route.RemoveWaypoint(request.Index);
            return Ok(route, Routes);
        });
    }
}

// ---------------------------------------------------------------------------
// set_leg_attributes
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SetLegAttributesTool"/>.</summary>
/// <remarks>
/// Every attribute is optional and overwrites the corresponding leg field
/// when supplied; an omitted (null) field leaves the existing value
/// unchanged. There is no facility to clear a previously-set field in this
/// version.
/// </remarks>
[Description("Request for set_leg_attributes: update one leg's geometry type and/or navigational envelope. All attributes optional; supplied values overwrite, omitted values are left unchanged.")]
internal sealed record SetLegAttributesRequest(
    [property: Description("Index of the leg to edit in [0, legCount); leg i joins waypoint i to waypoint i+1.")] int LegIndex,
    [property: Description("Id of the route to edit; omit to use the active route.")] string? RouteId = null,
    [property: Description("Leg path geometry: \"loxodrome\" (rhumb line) or \"geodesic\" (great circle).")] string? GeometryType = null,
    [property: Description("Starboard cross-track distance limit in metres.")] double? StarboardCrossTrackDistanceLimitMeters = null,
    [property: Description("Port cross-track distance limit in metres.")] double? PortCrossTrackDistanceLimitMeters = null,
    [property: Description("Starboard channel limit in metres.")] double? StarboardChannelLimitMeters = null,
    [property: Description("Port channel limit in metres.")] double? PortChannelLimitMeters = null,
    [property: Description("Safety contour for the leg in metres.")] double? SafetyContourMeters = null,
    [property: Description("Safety depth for the leg in metres.")] double? SafetyDepthMeters = null,
    [property: Description("Minimum speed over ground in knots.")] double? SpeedOverGroundMinKnots = null,
    [property: Description("Maximum speed over ground in knots.")] double? SpeedOverGroundMaxKnots = null,
    [property: Description("Minimum speed through water in knots.")] double? SpeedThroughWaterMinKnots = null,
    [property: Description("Maximum speed through water in knots.")] double? SpeedThroughWaterMaxKnots = null,
    [property: Description("Planned draft for the leg in metres.")] double? DraftMeters = null,
    [property: Description("Static under-keel clearance in metres.")] double? StaticUnderKeelClearanceMeters = null,
    [property: Description("Dynamic under-keel clearance in metres.")] double? DynamicUnderKeelClearanceMeters = null,
    [property: Description("Safety margin in metres.")] double? SafetyMarginMeters = null,
    [property: Description("Free-text note for the leg.")] string? Note = null);

/// <summary>Updates a single route leg's geometry type and navigational envelope.</summary>
internal sealed class SetLegAttributesTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "set_leg_attributes";

    /// <summary>Creates a new <see cref="SetLegAttributesTool"/>.</summary>
    public SetLegAttributesTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

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
            if (geometry.HasValue) leg.GeometryType = geometry.Value;
            if (request.StarboardCrossTrackDistanceLimitMeters.HasValue) leg.StarboardCrossTrackDistanceLimitMeters = request.StarboardCrossTrackDistanceLimitMeters;
            if (request.PortCrossTrackDistanceLimitMeters.HasValue) leg.PortCrossTrackDistanceLimitMeters = request.PortCrossTrackDistanceLimitMeters;
            if (request.StarboardChannelLimitMeters.HasValue) leg.StarboardChannelLimitMeters = request.StarboardChannelLimitMeters;
            if (request.PortChannelLimitMeters.HasValue) leg.PortChannelLimitMeters = request.PortChannelLimitMeters;
            if (request.SafetyContourMeters.HasValue) leg.SafetyContourMeters = request.SafetyContourMeters;
            if (request.SafetyDepthMeters.HasValue) leg.SafetyDepthMeters = request.SafetyDepthMeters;
            if (request.SpeedOverGroundMinKnots.HasValue) leg.SpeedOverGroundMinKnots = request.SpeedOverGroundMinKnots;
            if (request.SpeedOverGroundMaxKnots.HasValue) leg.SpeedOverGroundMaxKnots = request.SpeedOverGroundMaxKnots;
            if (request.SpeedThroughWaterMinKnots.HasValue) leg.SpeedThroughWaterMinKnots = request.SpeedThroughWaterMinKnots;
            if (request.SpeedThroughWaterMaxKnots.HasValue) leg.SpeedThroughWaterMaxKnots = request.SpeedThroughWaterMaxKnots;
            if (request.DraftMeters.HasValue) leg.DraftMeters = request.DraftMeters;
            if (request.StaticUnderKeelClearanceMeters.HasValue) leg.StaticUnderKeelClearanceMeters = request.StaticUnderKeelClearanceMeters;
            if (request.DynamicUnderKeelClearanceMeters.HasValue) leg.DynamicUnderKeelClearanceMeters = request.DynamicUnderKeelClearanceMeters;
            if (request.SafetyMarginMeters.HasValue) leg.SafetyMarginMeters = request.SafetyMarginMeters;
            if (request.Note is not null) leg.Note = request.Note;

            route.NotifyChanged();
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
            return Ok(route, Routes);
        });
    }
}
