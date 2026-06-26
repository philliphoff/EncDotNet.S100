using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Shared JSON options and result-translation helpers for the route MCP
/// adapters. Centralises the success / failure / internal-error wire shapes
/// so every <c>Route*McpAdapter</c> emits an identical payload contract.
/// </summary>
internal static class RouteAdapterShared
{
    /// <summary>
    /// The serializer options every route adapter uses. A configured
    /// <c>TypeInfoResolver</c> is required so the MCP SDK can call
    /// <see cref="JsonSerializerOptions.MakeReadOnly()"/> in the published
    /// (reflection-disabled) viewer without throwing.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    /// <summary>Runs <paramref name="resultFactory"/> and translates its outcome.</summary>
    public static async Task<CallToolResult> DispatchAsync<T>(Func<Task<ToolResult<T>>> resultFactory)
    {
        try
        {
            var result = await resultFactory().ConfigureAwait(false);
            return Translate(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return InternalError(ex);
        }
    }

    /// <summary>Translates a completed <see cref="ToolResult{T}"/> to a wire result.</summary>
    public static CallToolResult Translate<T>(ToolResult<T> result)
    {
        if (result.TryGetValue(out var value))
            return Success(value);
        result.TryGetError(out var err);
        return Failure(err!);
    }

    private static CallToolResult Success<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, Options) ?? new JsonObject();
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = node.ToJsonString(Options) }],
            IsError = false,
        };
    }

    private static CallToolResult Failure(ToolError error)
    {
        var details = JsonSerializer.SerializeToNode(error, error.GetType(), Options) as JsonObject
            ?? new JsonObject();
        details.Remove("code");
        details.Remove("message");
        details.Remove("Code");
        details.Remove("Message");

        var payload = new JsonObject
        {
            ["code"] = error.Code,
            ["message"] = error.Message,
            ["details"] = details,
        };
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = payload.ToJsonString(Options) }],
            IsError = true,
        };
    }

    private static CallToolResult InternalError(Exception ex)
    {
        var payload = new JsonObject
        {
            ["code"] = "internal_error",
            ["message"] = ex.Message,
            ["details"] = new JsonObject { ["exceptionType"] = ex.GetType().FullName },
        };
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = payload.ToJsonString(Options) }],
            IsError = true,
        };
    }
}

/// <summary>Wraps <see cref="CreateRouteTool"/> as an <see cref="McpServerTool"/>.</summary>
internal static class CreateRouteMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = RouteAdapterShared.Options;

    private const string Description =
        "Creates a new, empty editable route in the live viewer and makes it the active route. " +
        "Returns the new route's full state. Add waypoints with append_waypoint. Viewer-injected tool.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(CreateRouteTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [System.ComponentModel.Description("Optional route name.")] string? name = null,
            [System.ComponentModel.Description("Optional stable route id; a GUID is generated when omitted. Must be unique.")] string? id = null,
            CancellationToken ct = default) =>
            RouteAdapterShared.DispatchAsync(() => inner.InvokeAsync(new CreateRouteRequest(name, id), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = CreateRouteTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<RouteDetail> result)
        => RouteAdapterShared.Translate(result);
}

/// <summary>Wraps <see cref="ListRoutesTool"/> as an <see cref="McpServerTool"/>.</summary>
internal static class ListRoutesMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = RouteAdapterShared.Options;

    private const string Description =
        "Lists every editable route in the live viewer with its id, name, waypoint/leg counts, " +
        "total distance, and whether it is the active route. Viewer-injected tool.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(ListRoutesTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) =>
            RouteAdapterShared.DispatchAsync(() => inner.InvokeAsync(new ListRoutesRequest(), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = ListRoutesTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<ListRoutesResult> result)
        => RouteAdapterShared.Translate(result);
}

/// <summary>Wraps <see cref="GetRouteTool"/> as an <see cref="McpServerTool"/>.</summary>
internal static class GetRouteMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = RouteAdapterShared.Options;

    private const string Description =
        "Returns the full state of one route in the live viewer: identity, metadata, ordered " +
        "waypoints, and legs with computed distance/bearing. Omit routeId to read the active route. " +
        "Viewer-injected tool.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(GetRouteTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [System.ComponentModel.Description("Id of the route to return; omit to use the active route.")] string? routeId = null,
            CancellationToken ct = default) =>
            RouteAdapterShared.DispatchAsync(() => inner.InvokeAsync(new GetRouteRequest(routeId), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetRouteTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<RouteDetail> result)
        => RouteAdapterShared.Translate(result);
}

/// <summary>Wraps <see cref="DeleteRouteTool"/> as an <see cref="McpServerTool"/>.</summary>
internal static class DeleteRouteMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = RouteAdapterShared.Options;

    private const string Description =
        "Removes a route from the live viewer's collection. Omit routeId to delete the active route. " +
        "Returns the new active route id. Viewer-injected tool — mutates live state.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(DeleteRouteTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [System.ComponentModel.Description("Id of the route to delete; omit to delete the active route.")] string? routeId = null,
            CancellationToken ct = default) =>
            RouteAdapterShared.DispatchAsync(() => inner.InvokeAsync(new DeleteRouteRequest(routeId), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = DeleteRouteTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<DeleteRouteResult> result)
        => RouteAdapterShared.Translate(result);
}

/// <summary>Wraps <see cref="AppendWaypointTool"/> as an <see cref="McpServerTool"/>.</summary>
internal static class AppendWaypointMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = RouteAdapterShared.Options;

    private const string Description =
        "Appends a waypoint to the end of a route in the live viewer. Omit routeId to use the active " +
        "route. Returns the route's full updated state. Viewer-injected tool — mutates live state.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(AppendWaypointTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [System.ComponentModel.Description("WGS-84 latitude in decimal degrees [-90, 90].")] double lat,
            [System.ComponentModel.Description("WGS-84 longitude in decimal degrees [-180, 180].")] double lon,
            [System.ComponentModel.Description("Id of the route to append to; omit to use the active route.")] string? routeId = null,
            [System.ComponentModel.Description("Optional author-assigned ordinal (S-421 routeWaypointID).")] int? number = null,
            [System.ComponentModel.Description("Optional waypoint name.")] string? name = null,
            [System.ComponentModel.Description("Optional flag pinning the waypoint so it must not be moved.")] bool? @fixed = null,
            [System.ComponentModel.Description("Optional planned turn radius at the waypoint, in nautical miles.")] double? turnRadiusNm = null,
            CancellationToken ct = default) =>
            RouteAdapterShared.DispatchAsync(() => inner.InvokeAsync(
                new AppendWaypointRequest(lat, lon, routeId, number, name, @fixed, turnRadiusNm), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = AppendWaypointTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<RouteDetail> result)
        => RouteAdapterShared.Translate(result);
}

/// <summary>Wraps <see cref="InsertWaypointTool"/> as an <see cref="McpServerTool"/>.</summary>
internal static class InsertWaypointMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = RouteAdapterShared.Options;

    private const string Description =
        "Inserts a waypoint at a given index of a route in the live viewer, splitting the affected " +
        "leg. Index 0 prepends; waypointCount appends. Omit routeId to use the active route. Returns " +
        "the route's full updated state. Viewer-injected tool — mutates live state.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(InsertWaypointTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [System.ComponentModel.Description("Insertion index in [0, waypointCount]; 0 prepends, waypointCount appends.")] int index,
            [System.ComponentModel.Description("WGS-84 latitude in decimal degrees [-90, 90].")] double lat,
            [System.ComponentModel.Description("WGS-84 longitude in decimal degrees [-180, 180].")] double lon,
            [System.ComponentModel.Description("Id of the route to insert into; omit to use the active route.")] string? routeId = null,
            [System.ComponentModel.Description("Optional author-assigned ordinal (S-421 routeWaypointID).")] int? number = null,
            [System.ComponentModel.Description("Optional waypoint name.")] string? name = null,
            [System.ComponentModel.Description("Optional flag pinning the waypoint so it must not be moved.")] bool? @fixed = null,
            [System.ComponentModel.Description("Optional planned turn radius at the waypoint, in nautical miles.")] double? turnRadiusNm = null,
            CancellationToken ct = default) =>
            RouteAdapterShared.DispatchAsync(() => inner.InvokeAsync(
                new InsertWaypointRequest(index, lat, lon, routeId, number, name, @fixed, turnRadiusNm), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = InsertWaypointTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<RouteDetail> result)
        => RouteAdapterShared.Translate(result);
}

/// <summary>Wraps <see cref="MoveWaypointTool"/> as an <see cref="McpServerTool"/>.</summary>
internal static class MoveWaypointMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = RouteAdapterShared.Options;

    private const string Description =
        "Moves an existing waypoint of a route in the live viewer to a new position. Omit routeId to " +
        "use the active route. Returns the route's full updated state. Viewer-injected tool — mutates " +
        "live state.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(MoveWaypointTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [System.ComponentModel.Description("Index of the waypoint to move in [0, waypointCount).")] int index,
            [System.ComponentModel.Description("New WGS-84 latitude in decimal degrees [-90, 90].")] double lat,
            [System.ComponentModel.Description("New WGS-84 longitude in decimal degrees [-180, 180].")] double lon,
            [System.ComponentModel.Description("Id of the route to edit; omit to use the active route.")] string? routeId = null,
            CancellationToken ct = default) =>
            RouteAdapterShared.DispatchAsync(() => inner.InvokeAsync(
                new MoveWaypointRequest(index, lat, lon, routeId), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = MoveWaypointTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<RouteDetail> result)
        => RouteAdapterShared.Translate(result);
}

/// <summary>Wraps <see cref="DeleteWaypointTool"/> as an <see cref="McpServerTool"/>.</summary>
internal static class DeleteWaypointMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = RouteAdapterShared.Options;

    private const string Description =
        "Removes a waypoint from a route in the live viewer, merging the adjacent legs. Omit routeId " +
        "to use the active route. Returns the route's full updated state. Viewer-injected tool — " +
        "mutates live state.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(DeleteWaypointTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [System.ComponentModel.Description("Index of the waypoint to remove in [0, waypointCount).")] int index,
            [System.ComponentModel.Description("Id of the route to edit; omit to use the active route.")] string? routeId = null,
            CancellationToken ct = default) =>
            RouteAdapterShared.DispatchAsync(() => inner.InvokeAsync(
                new DeleteWaypointRequest(index, routeId), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = DeleteWaypointTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<RouteDetail> result)
        => RouteAdapterShared.Translate(result);
}

/// <summary>Wraps <see cref="SetLegAttributesTool"/> as an <see cref="McpServerTool"/>.</summary>
internal static class SetLegAttributesMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = RouteAdapterShared.Options;

    private const string Description =
        "Updates one leg of a route in the live viewer: its geometry type (loxodrome|geodesic) and/or " +
        "navigational envelope (cross-track and channel limits, safety contour/depth, speed and draft " +
        "limits, under-keel clearances, note). All attributes optional; supplied values overwrite. " +
        "Omit routeId to use the active route. Viewer-injected tool — mutates live state.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(SetLegAttributesTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [System.ComponentModel.Description("Index of the leg to edit in [0, legCount); leg i joins waypoint i to waypoint i+1.")] int legIndex,
            [System.ComponentModel.Description("Id of the route to edit; omit to use the active route.")] string? routeId = null,
            [System.ComponentModel.Description("Leg path geometry: \"loxodrome\" (rhumb line) or \"geodesic\" (great circle).")] string? geometryType = null,
            [System.ComponentModel.Description("Starboard cross-track distance limit in metres.")] double? starboardCrossTrackDistanceLimitMeters = null,
            [System.ComponentModel.Description("Port cross-track distance limit in metres.")] double? portCrossTrackDistanceLimitMeters = null,
            [System.ComponentModel.Description("Starboard channel limit in metres.")] double? starboardChannelLimitMeters = null,
            [System.ComponentModel.Description("Port channel limit in metres.")] double? portChannelLimitMeters = null,
            [System.ComponentModel.Description("Safety contour for the leg in metres.")] double? safetyContourMeters = null,
            [System.ComponentModel.Description("Safety depth for the leg in metres.")] double? safetyDepthMeters = null,
            [System.ComponentModel.Description("Minimum speed over ground in knots.")] double? speedOverGroundMinKnots = null,
            [System.ComponentModel.Description("Maximum speed over ground in knots.")] double? speedOverGroundMaxKnots = null,
            [System.ComponentModel.Description("Minimum speed through water in knots.")] double? speedThroughWaterMinKnots = null,
            [System.ComponentModel.Description("Maximum speed through water in knots.")] double? speedThroughWaterMaxKnots = null,
            [System.ComponentModel.Description("Planned draft for the leg in metres.")] double? draftMeters = null,
            [System.ComponentModel.Description("Static under-keel clearance in metres.")] double? staticUnderKeelClearanceMeters = null,
            [System.ComponentModel.Description("Dynamic under-keel clearance in metres.")] double? dynamicUnderKeelClearanceMeters = null,
            [System.ComponentModel.Description("Safety margin in metres.")] double? safetyMarginMeters = null,
            [System.ComponentModel.Description("Free-text note for the leg.")] string? note = null,
            CancellationToken ct = default) =>
            RouteAdapterShared.DispatchAsync(() => inner.InvokeAsync(
                new SetLegAttributesRequest(
                    legIndex, routeId, geometryType,
                    starboardCrossTrackDistanceLimitMeters, portCrossTrackDistanceLimitMeters,
                    starboardChannelLimitMeters, portChannelLimitMeters,
                    safetyContourMeters, safetyDepthMeters,
                    speedOverGroundMinKnots, speedOverGroundMaxKnots,
                    speedThroughWaterMinKnots, speedThroughWaterMaxKnots,
                    draftMeters, staticUnderKeelClearanceMeters, dynamicUnderKeelClearanceMeters,
                    safetyMarginMeters, note),
                ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetLegAttributesTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<RouteDetail> result)
        => RouteAdapterShared.Translate(result);
}

/// <summary>Wraps <see cref="SetRouteInfoTool"/> as an <see cref="McpServerTool"/>.</summary>
internal static class SetRouteInfoMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = RouteAdapterShared.Options;

    private const string Description =
        "Updates a route's metadata in the live viewer (name, author, description, departure/arrival " +
        "ports, validity window) and vessel particulars (name, MMSI, IMO, call sign, length, beam). " +
        "All fields optional; supplied values overwrite. Omit routeId to use the active route. " +
        "Viewer-injected tool — mutates live state.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(SetRouteInfoTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [System.ComponentModel.Description("Id of the route to edit; omit to use the active route.")] string? routeId = null,
            [System.ComponentModel.Description("Route name.")] string? name = null,
            [System.ComponentModel.Description("Route author / originator.")] string? author = null,
            [System.ComponentModel.Description("Free-text route description.")] string? description = null,
            [System.ComponentModel.Description("Departure port identifier.")] string? departurePortId = null,
            [System.ComponentModel.Description("Arrival port identifier.")] string? arrivalPortId = null,
            [System.ComponentModel.Description("Planned start of validity (UTC ISO-8601).")] DateTimeOffset? validityStart = null,
            [System.ComponentModel.Description("Planned end of validity (UTC ISO-8601).")] DateTimeOffset? validityEnd = null,
            [System.ComponentModel.Description("Vessel name (creates the vessel block when supplied).")] string? vesselName = null,
            [System.ComponentModel.Description("Vessel MMSI (creates the vessel block when supplied).")] string? vesselMmsi = null,
            [System.ComponentModel.Description("Vessel IMO number (creates the vessel block when supplied).")] string? vesselImo = null,
            [System.ComponentModel.Description("Vessel call sign (creates the vessel block when supplied).")] string? vesselCallsign = null,
            [System.ComponentModel.Description("Vessel overall length in metres (creates the vessel block when supplied).")] double? vesselLengthMeters = null,
            [System.ComponentModel.Description("Vessel beam in metres (creates the vessel block when supplied).")] double? vesselBeamMeters = null,
            CancellationToken ct = default) =>
            RouteAdapterShared.DispatchAsync(() => inner.InvokeAsync(
                new SetRouteInfoRequest(
                    routeId, name, author, description, departurePortId, arrivalPortId,
                    validityStart, validityEnd,
                    vesselName, vesselMmsi, vesselImo, vesselCallsign, vesselLengthMeters, vesselBeamMeters),
                ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetRouteInfoTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<RouteDetail> result)
        => RouteAdapterShared.Translate(result);
}
