using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Protocol;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Unit tests for the agent-facing route MCP tools. The tools are exercised
/// with an inline <see cref="IRouteEditInvoker"/> so no Avalonia dispatcher
/// is required.
/// </summary>
public class RouteMcpToolsTests
{
    /// <summary>Runs the action inline and counts invocations.</summary>
    private sealed class InlineRouteEditInvoker : IRouteEditInvoker
    {
        public int Invocations { get; private set; }

        public Task<T> InvokeAsync<T>(System.Func<T> action)
        {
            Invocations++;
            return Task.FromResult(action());
        }
    }

    private static (RoutesService routes, InlineRouteEditInvoker invoker) Make()
        => (new RoutesService(), new InlineRouteEditInvoker());

    private static T Value<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetValue(out var value), "expected a success result");
        return value!;
    }

    private static ToolError Error<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetError(out var error), "expected an error result");
        return error!;
    }

    // ----- create / list / get / delete -----------------------------------

    [Fact]
    public async Task CreateRoute_adds_active_route_and_marshals()
    {
        var (routes, invoker) = Make();
        var tool = new CreateRouteTool(routes, invoker);

        var detail = Value(await tool.InvokeAsync(new CreateRouteRequest(Name: "Transit", Id: "r1")));

        Assert.Equal("r1", detail.RouteId);
        Assert.Equal("Transit", detail.Name);
        Assert.True(detail.IsActive);
        Assert.Empty(detail.Waypoints);
        Assert.Same(routes.Routes.FindById("r1"), routes.Routes.ActiveRoute);
        Assert.Equal(1, invoker.Invocations);
    }

    [Fact]
    public async Task CreateRoute_duplicate_id_is_invalid_argument()
    {
        var (routes, invoker) = Make();
        var tool = new CreateRouteTool(routes, invoker);
        await tool.InvokeAsync(new CreateRouteRequest(Id: "dup"));

        var error = Error(await tool.InvokeAsync(new CreateRouteRequest(Id: "dup")));

        Assert.IsType<InvalidArgument>(error);
        Assert.Equal("invalid_argument", error.Code);
    }

    [Fact]
    public async Task ListRoutes_reports_all_routes_and_active()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "a"));
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "b"));

        var result = Value(await new ListRoutesTool(routes, invoker).InvokeAsync(new ListRoutesRequest()));

        Assert.Equal(2, result.Routes.Count);
        Assert.Equal("b", result.ActiveRouteId);
        Assert.Contains(result.Routes, r => r is { RouteId: "b", IsActive: true });
    }

    [Fact]
    public async Task GetRoute_defaults_to_active_route()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "act"));

        var detail = Value(await new GetRouteTool(routes, invoker).InvokeAsync(new GetRouteRequest()));

        Assert.Equal("act", detail.RouteId);
    }

    [Fact]
    public async Task GetRoute_unknown_id_is_route_not_found()
    {
        var (routes, invoker) = Make();

        var error = Error(await new GetRouteTool(routes, invoker).InvokeAsync(new GetRouteRequest("nope")));

        Assert.IsType<RouteNotFound>(error);
        Assert.Equal("route_not_found", error.Code);
    }

    [Fact]
    public async Task GetRoute_with_no_routes_reports_active_sentinel()
    {
        var (routes, invoker) = Make();

        var error = Error(await new GetRouteTool(routes, invoker).InvokeAsync(new GetRouteRequest()));

        var notFound = Assert.IsType<RouteNotFound>(error);
        Assert.Equal("(active)", notFound.RouteId);
    }

    [Fact]
    public async Task DeleteRoute_removes_and_reports_new_active()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "a"));
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "b"));

        var result = Value(await new DeleteRouteTool(routes, invoker).InvokeAsync(new DeleteRouteRequest("b")));

        Assert.True(result.Deleted);
        Assert.Equal("b", result.RouteId);
        Assert.Equal("a", result.ActiveRouteId);
        Assert.Null(routes.Routes.FindById("b"));
    }

    // ----- waypoint editing ------------------------------------------------

    [Fact]
    public async Task AppendWaypoint_adds_point_with_metadata()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"));

        var detail = Value(await new AppendWaypointTool(routes, invoker).InvokeAsync(
            new AppendWaypointRequest(47.6, -122.3, Name: "WP1", Number: 1, TurnRadiusNm: 0.5)));

        var wp = Assert.Single(detail.Waypoints);
        Assert.Equal(47.6, wp.Lat);
        Assert.Equal(-122.3, wp.Lon);
        Assert.Equal("WP1", wp.Name);
        Assert.Equal(1, wp.Number);
        Assert.Equal(0.5, wp.TurnRadiusNm);
    }

    [Fact]
    public async Task AppendWaypoint_invalid_latitude_is_rejected()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"));

        var error = Error(await new AppendWaypointTool(routes, invoker).InvokeAsync(
            new AppendWaypointRequest(120.0, 0.0)));

        var invalid = Assert.IsType<InvalidArgument>(error);
        Assert.Equal("lat", invalid.Parameter);
    }

    [Fact]
    public async Task AppendWaypoint_with_no_active_route_is_route_not_found()
    {
        var (routes, invoker) = Make();

        var error = Error(await new AppendWaypointTool(routes, invoker).InvokeAsync(
            new AppendWaypointRequest(10.0, 10.0)));

        Assert.IsType<RouteNotFound>(error);
    }

    [Fact]
    public async Task InsertWaypoint_builds_legs_and_validates_index()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"));
        var append = new AppendWaypointTool(routes, invoker);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 0.0));
        await append.InvokeAsync(new AppendWaypointRequest(1.0, 0.0));

        var insert = new InsertWaypointTool(routes, invoker);
        var detail = Value(await insert.InvokeAsync(new InsertWaypointRequest(1, 0.5, 0.0)));

        Assert.Equal(3, detail.Waypoints.Count);
        Assert.Equal(2, detail.Legs.Count);
        Assert.Equal(0.5, detail.Waypoints[1].Lat);

        var error = Error(await insert.InvokeAsync(new InsertWaypointRequest(99, 0.5, 0.0)));
        var invalid = Assert.IsType<InvalidArgument>(error);
        Assert.Equal("index", invalid.Parameter);
    }

    [Fact]
    public async Task MoveWaypoint_updates_position_and_validates_index()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"));
        var append = new AppendWaypointTool(routes, invoker);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 0.0));

        var move = new MoveWaypointTool(routes, invoker);
        var detail = Value(await move.InvokeAsync(new MoveWaypointRequest(0, 5.0, 6.0)));
        Assert.Equal(5.0, detail.Waypoints[0].Lat);
        Assert.Equal(6.0, detail.Waypoints[0].Lon);

        var error = Error(await move.InvokeAsync(new MoveWaypointRequest(7, 1.0, 1.0)));
        Assert.Equal("index", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task DeleteWaypoint_removes_point_and_validates_index()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"));
        var append = new AppendWaypointTool(routes, invoker);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 0.0));
        await append.InvokeAsync(new AppendWaypointRequest(1.0, 1.0));

        var del = new DeleteWaypointTool(routes, invoker);
        var detail = Value(await del.InvokeAsync(new DeleteWaypointRequest(0)));
        Assert.Single(detail.Waypoints);
        Assert.Empty(detail.Legs);

        var error = Error(await del.InvokeAsync(new DeleteWaypointRequest(5)));
        Assert.Equal("index", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    // ----- leg attributes / route info -------------------------------------

    [Fact]
    public async Task SetLegAttributes_updates_geometry_and_envelope()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"));
        var append = new AppendWaypointTool(routes, invoker);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 0.0));
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 10.0));

        var tool = new SetLegAttributesTool(routes, invoker);
        var detail = Value(await tool.InvokeAsync(new SetLegAttributesRequest(
            0, GeometryType: "geodesic",
            StarboardCrossTrackDistanceLimitMeters: 185.2,
            SafetyContourMeters: 10.0,
            Note: "mid-channel")));

        var leg = Assert.Single(detail.Legs);
        Assert.Equal("geodesic", leg.GeometryType);
        Assert.Equal(185.2, leg.StarboardCrossTrackDistanceLimitMeters);
        Assert.Equal(10.0, leg.SafetyContourMeters);
        Assert.Equal("mid-channel", leg.Note);
    }

    [Fact]
    public async Task SetLegAttributes_rejects_unknown_geometry()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"));
        var append = new AppendWaypointTool(routes, invoker);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 0.0));
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 1.0));

        var error = Error(await new SetLegAttributesTool(routes, invoker).InvokeAsync(
            new SetLegAttributesRequest(0, GeometryType: "spiral")));

        Assert.Equal("geometryType", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task SetLegAttributes_validates_leg_index()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"));

        var error = Error(await new SetLegAttributesTool(routes, invoker).InvokeAsync(
            new SetLegAttributesRequest(0)));

        Assert.Equal("legIndex", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task SetRouteInfo_updates_metadata_and_creates_vessel()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"));

        var detail = Value(await new SetRouteInfoTool(routes, invoker).InvokeAsync(
            new SetRouteInfoRequest(
                Name: "Pilotage", Author: "Pilot", DeparturePortId: "PORTA",
                VesselName: "MV Test", VesselMmsi: "366000000", VesselLengthMeters: 180.0)));

        Assert.Equal("Pilotage", detail.Name);
        Assert.Equal("Pilot", detail.Info.Author);
        Assert.Equal("PORTA", detail.Info.DeparturePortId);
        Assert.NotNull(detail.Info.Vessel);
        Assert.Equal("MV Test", detail.Info.Vessel!.Name);
        Assert.Equal("366000000", detail.Info.Vessel.Mmsi);
        Assert.Equal(180.0, detail.Info.Vessel.LengthMeters);
    }

    [Fact]
    public async Task Mutation_raises_routes_service_changed()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"));

        var raised = 0;
        routes.Changed += (_, _) => raised++;

        await new AppendWaypointTool(routes, invoker).InvokeAsync(new AppendWaypointRequest(1.0, 1.0));

        Assert.True(raised > 0);
    }

    // ----- adapter wire shapes ---------------------------------------------

    [Fact]
    public void Adapter_success_payload_is_camel_case_route_detail()
    {
        var routes = new RoutesService();
        var route = routes.Routes.CreateRoute("Demo", "rid");
        var detail = RouteProjection.Detail(route, routes);

        var call = CreateRouteMcpAdapter.TranslateResult(ToolResult<RouteDetail>.Ok(detail));

        Assert.False(call.IsError);
        var text = Assert.IsType<TextContentBlock>(call.Content.Single()).Text;
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("rid", doc.RootElement.GetProperty("routeId").GetString());
        Assert.Equal("Demo", doc.RootElement.GetProperty("name").GetString());
        Assert.True(doc.RootElement.GetProperty("isActive").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("waypoints", out _));
    }

    [Fact]
    public void Adapter_failure_payload_has_code_message_details()
    {
        var call = GetRouteMcpAdapter.TranslateResult(
            ToolResult<RouteDetail>.Err(new RouteNotFound("ghost")));

        Assert.True(call.IsError);
        var text = Assert.IsType<TextContentBlock>(call.Content.Single()).Text;
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("route_not_found", doc.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("message").GetString()));
        Assert.Equal("ghost", doc.RootElement.GetProperty("details").GetProperty("routeId").GetString());
    }
}
