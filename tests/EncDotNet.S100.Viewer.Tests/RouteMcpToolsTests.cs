using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Protocol;

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

        var detail = Value(await tool.InvokeAsync(new CreateRouteRequest(Name: "Transit", Id: "r1"), TestContext.Current.CancellationToken));

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
        await tool.InvokeAsync(new CreateRouteRequest(Id: "dup"), TestContext.Current.CancellationToken);

        var error = Error(await tool.InvokeAsync(new CreateRouteRequest(Id: "dup"), TestContext.Current.CancellationToken));

        Assert.IsType<InvalidArgument>(error);
        Assert.Equal("invalid_argument", error.Code);
    }

    [Fact]
    public async Task ListRoutes_reports_all_routes_and_active()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "a"), TestContext.Current.CancellationToken);
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "b"), TestContext.Current.CancellationToken);

        var result = Value(await new ListRoutesTool(routes, invoker).InvokeAsync(new ListRoutesRequest(), TestContext.Current.CancellationToken));

        Assert.Equal(2, result.Routes.Count);
        Assert.Equal("b", result.ActiveRouteId);
        Assert.Contains(result.Routes, r => r is { RouteId: "b", IsActive: true });
    }

    [Fact]
    public async Task GetRoute_defaults_to_active_route()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "act"), TestContext.Current.CancellationToken);

        var detail = Value(await new GetRouteTool(routes, invoker).InvokeAsync(new GetRouteRequest(), TestContext.Current.CancellationToken));

        Assert.Equal("act", detail.RouteId);
    }

    [Fact]
    public async Task GetRoute_unknown_id_is_route_not_found()
    {
        var (routes, invoker) = Make();

        var error = Error(await new GetRouteTool(routes, invoker).InvokeAsync(new GetRouteRequest("nope"), TestContext.Current.CancellationToken));

        Assert.IsType<RouteNotFound>(error);
        Assert.Equal("route_not_found", error.Code);
    }

    [Fact]
    public async Task GetRoute_with_no_routes_reports_active_sentinel()
    {
        var (routes, invoker) = Make();

        var error = Error(await new GetRouteTool(routes, invoker).InvokeAsync(new GetRouteRequest(), TestContext.Current.CancellationToken));

        var notFound = Assert.IsType<RouteNotFound>(error);
        Assert.Equal("(active)", notFound.RouteId);
    }

    [Fact]
    public async Task DeleteRoute_removes_and_reports_new_active()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "a"), TestContext.Current.CancellationToken);
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "b"), TestContext.Current.CancellationToken);

        var result = Value(await new DeleteRouteTool(routes, invoker).InvokeAsync(new DeleteRouteRequest("b"), TestContext.Current.CancellationToken));

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
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"), TestContext.Current.CancellationToken);

        var detail = Value(await new AppendWaypointTool(routes, invoker).InvokeAsync(
            new AppendWaypointRequest(47.6, -122.3, Name: "WP1", Number: 1, TurnRadiusNm: 0.5),
            TestContext.Current.CancellationToken));

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
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"), TestContext.Current.CancellationToken);

        var error = Error(await new AppendWaypointTool(routes, invoker).InvokeAsync(
            new AppendWaypointRequest(120.0, 0.0),
            TestContext.Current.CancellationToken));

        var invalid = Assert.IsType<InvalidArgument>(error);
        Assert.Equal("lat", invalid.Parameter);
    }

    [Fact]
    public async Task AppendWaypoint_with_no_active_route_is_route_not_found()
    {
        var (routes, invoker) = Make();

        var error = Error(await new AppendWaypointTool(routes, invoker).InvokeAsync(
            new AppendWaypointRequest(10.0, 10.0),
            TestContext.Current.CancellationToken));

        Assert.IsType<RouteNotFound>(error);
    }

    [Fact]
    public async Task InsertWaypoint_builds_legs_and_validates_index()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"), TestContext.Current.CancellationToken);
        var append = new AppendWaypointTool(routes, invoker);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 0.0), TestContext.Current.CancellationToken);
        await append.InvokeAsync(new AppendWaypointRequest(1.0, 0.0), TestContext.Current.CancellationToken);

        var insert = new InsertWaypointTool(routes, invoker);
        var detail = Value(await insert.InvokeAsync(new InsertWaypointRequest(1, 0.5, 0.0), TestContext.Current.CancellationToken));

        Assert.Equal(3, detail.Waypoints.Count);
        Assert.Equal(2, detail.Legs.Count);
        Assert.Equal(0.5, detail.Waypoints[1].Lat);

        var error = Error(await insert.InvokeAsync(new InsertWaypointRequest(99, 0.5, 0.0), TestContext.Current.CancellationToken));
        var invalid = Assert.IsType<InvalidArgument>(error);
        Assert.Equal("index", invalid.Parameter);
    }

    [Fact]
    public async Task MoveWaypoint_updates_position_and_validates_index()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"), TestContext.Current.CancellationToken);
        var append = new AppendWaypointTool(routes, invoker);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 0.0), TestContext.Current.CancellationToken);

        var move = new MoveWaypointTool(routes, invoker);
        var detail = Value(await move.InvokeAsync(new MoveWaypointRequest(0, 5.0, 6.0), TestContext.Current.CancellationToken));
        Assert.Equal(5.0, detail.Waypoints[0].Lat);
        Assert.Equal(6.0, detail.Waypoints[0].Lon);

        var error = Error(await move.InvokeAsync(new MoveWaypointRequest(7, 1.0, 1.0), TestContext.Current.CancellationToken));
        Assert.Equal("index", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task DeleteWaypoint_removes_point_and_validates_index()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"), TestContext.Current.CancellationToken);
        var append = new AppendWaypointTool(routes, invoker);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 0.0), TestContext.Current.CancellationToken);
        await append.InvokeAsync(new AppendWaypointRequest(1.0, 1.0), TestContext.Current.CancellationToken);

        var del = new DeleteWaypointTool(routes, invoker);
        var detail = Value(await del.InvokeAsync(new DeleteWaypointRequest(0), TestContext.Current.CancellationToken));
        Assert.Single(detail.Waypoints);
        Assert.Empty(detail.Legs);

        var error = Error(await del.InvokeAsync(new DeleteWaypointRequest(5), TestContext.Current.CancellationToken));
        Assert.Equal("index", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    // ----- leg attributes / route info -------------------------------------

    [Fact]
    public async Task SetLegAttributes_updates_geometry_and_envelope()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"), TestContext.Current.CancellationToken);
        var append = new AppendWaypointTool(routes, invoker);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 0.0), TestContext.Current.CancellationToken);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 10.0), TestContext.Current.CancellationToken);

        var tool = new SetLegAttributesTool(routes, invoker);
        var detail = Value(await tool.InvokeAsync(new SetLegAttributesRequest(
            0, GeometryType: "geodesic",
            StarboardCrossTrackDistanceLimitMeters: 185.2,
            SafetyContourMeters: 10.0,
            Note: "mid-channel"), TestContext.Current.CancellationToken));

        var leg = Assert.Single(detail.Legs);
        Assert.Equal("geodesic", leg.GeometryType);
        Assert.Equal(185.2, leg.Overrides.StarboardCrossTrackMeters);
        Assert.Equal(185.2, leg.Effective.StarboardCrossTrackMeters);
        Assert.Equal(10.0, leg.SafetyContourMeters);
        Assert.Equal("mid-channel", leg.Note);
    }

    [Fact]
    public async Task SetLegAttributes_rejects_unknown_geometry()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"), TestContext.Current.CancellationToken);
        var append = new AppendWaypointTool(routes, invoker);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 0.0), TestContext.Current.CancellationToken);
        await append.InvokeAsync(new AppendWaypointRequest(0.0, 1.0), TestContext.Current.CancellationToken);

        var error = Error(await new SetLegAttributesTool(routes, invoker).InvokeAsync(
            new SetLegAttributesRequest(0, GeometryType: "spiral"),
            TestContext.Current.CancellationToken));

        Assert.Equal("geometryType", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task SetLegAttributes_validates_leg_index()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"), TestContext.Current.CancellationToken);

        var error = Error(await new SetLegAttributesTool(routes, invoker).InvokeAsync(
            new SetLegAttributesRequest(0),
            TestContext.Current.CancellationToken));

        Assert.Equal("legIndex", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task SetRouteInfo_updates_metadata_and_creates_vessel()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"), TestContext.Current.CancellationToken);

        var detail = Value(await new SetRouteInfoTool(routes, invoker).InvokeAsync(
            new SetRouteInfoRequest(
                Name: "Pilotage", Author: "Pilot", DeparturePortId: "PORTA",
                VesselName: "MV Test", VesselMmsi: "366000000", VesselLengthMeters: 180.0),
            TestContext.Current.CancellationToken));

        Assert.Equal("Pilotage", detail.Name);
        Assert.Equal("Pilot", detail.Info.Author);
        Assert.Equal("PORTA", detail.Info.DeparturePortId);
        Assert.NotNull(detail.Info.Vessel);
        Assert.Equal("MV Test", detail.Info.Vessel!.Name);
        Assert.Equal("366000000", detail.Info.Vessel.Mmsi);
        Assert.Equal(180.0, detail.Info.Vessel.LengthMeters);
    }

    // ----- passage (#897) ---------------------------------------------------

    private static async Task<(RoutesService routes, InlineRouteEditInvoker invoker)> MakeThreeLegRoute()
    {
        var (routes, invoker) = Make();
        var ct = TestContext.Current.CancellationToken;
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"), ct);
        var append = new AppendWaypointTool(routes, invoker);
        for (var i = 0; i < 4; i++)
            await append.InvokeAsync(new AppendWaypointRequest(50.0 + (i * 0.1), -1.0), ct);
        return (routes, invoker);
    }

    [Fact]
    public async Task SetPassage_sets_values_that_legs_inherit()
    {
        var (routes, invoker) = await MakeThreeLegRoute();
        var departure = new DateTimeOffset(2026, 10, 12, 6, 40, 0, TimeSpan.Zero);

        var detail = Value(await new SetPassageTool(routes, invoker).InvokeAsync(
            new SetPassageRequest(DraftMeters: 4.8, SquatMeters: 0.3, SpeedKnots: 8, Departure: departure,
                RequiredUkcMeters: 0.5, CrossTrackMeters: 50),
            TestContext.Current.CancellationToken));

        Assert.Equal(4.8, detail.Passage.DraftMeters);
        Assert.Equal(0.3, detail.Passage.SquatMeters);
        Assert.Equal(departure, detail.Passage.Departure);
        Assert.Equal(50, detail.Passage.PortCrossTrackMeters);
        Assert.Equal(50, detail.Passage.StarboardCrossTrackMeters);
        Assert.All(detail.Legs, leg =>
        {
            Assert.Equal(8, leg.Effective.SpeedKnots);
            Assert.Equal(4.8, leg.Effective.DraftMeters);
            Assert.Equal(0.5, leg.Effective.RequiredUkcMeters);
            Assert.Null(leg.Overrides.SpeedKnots);
            Assert.Null(leg.Overrides.DraftMeters);
        });

        var passage = Value(await new GetPassageTool(routes, invoker).InvokeAsync(
            new GetPassageRequest(), TestContext.Current.CancellationToken));
        Assert.Equal("r", passage.RouteId);
        Assert.Equal(detail.Passage, passage.Passage);
    }

    [Fact]
    public async Task SetPassage_clear_unsets_values()
    {
        var (routes, invoker) = await MakeThreeLegRoute();
        var tool = new SetPassageTool(routes, invoker);
        var ct = TestContext.Current.CancellationToken;
        await tool.InvokeAsync(new SetPassageRequest(SpeedKnots: 8, Departure: DateTimeOffset.UnixEpoch, CrossTrackMeters: 20), ct);

        var detail = Value(await tool.InvokeAsync(
            new SetPassageRequest(Clear: ["speedKnots", "departure", "crossTrackMeters"]), ct));

        Assert.Null(detail.Passage.SpeedKnots);
        Assert.Null(detail.Passage.Departure);
        Assert.Null(detail.Passage.PortCrossTrackMeters);
        Assert.Null(detail.Passage.StarboardCrossTrackMeters);
        Assert.True(routes.Routes.ActiveRoute!.Passage.IsEmpty);
    }

    [Theory]
    [InlineData(0.0, null, "speedKnots")]
    [InlineData(null, -1.0, "draftMeters")]
    public async Task SetPassage_rejects_out_of_range_values(double? speed, double? draft, string parameter)
    {
        var (routes, invoker) = await MakeThreeLegRoute();

        var error = Error(await new SetPassageTool(routes, invoker).InvokeAsync(
            new SetPassageRequest(SpeedKnots: speed, DraftMeters: draft), TestContext.Current.CancellationToken));

        Assert.Equal(parameter, Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task SetPassage_rejects_unknown_clear_names_and_set_and_clear_of_one_value()
    {
        var (routes, invoker) = await MakeThreeLegRoute();
        var tool = new SetPassageTool(routes, invoker);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal("clear", Assert.IsType<InvalidArgument>(Error(await tool.InvokeAsync(
            new SetPassageRequest(Clear: ["warpFactor"]), ct))).Parameter);
        Assert.Equal("clear", Assert.IsType<InvalidArgument>(Error(await tool.InvokeAsync(
            new SetPassageRequest(SpeedKnots: 8, Clear: ["speedKnots"]), ct))).Parameter);
        Assert.Equal("clear", Assert.IsType<InvalidArgument>(Error(await tool.InvokeAsync(
            new SetPassageRequest(PortCrossTrackMeters: 8, Clear: ["crossTrackMeters"]), ct))).Parameter);
        Assert.Equal("crossTrackMeters", Assert.IsType<InvalidArgument>(Error(await tool.InvokeAsync(
            new SetPassageRequest(CrossTrackMeters: 8, PortCrossTrackMeters: 9), ct))).Parameter);
    }

    [Fact]
    public async Task SetLegAttributes_sets_an_override_only_when_it_differs()
    {
        var (routes, invoker) = await MakeThreeLegRoute();
        var ct = TestContext.Current.CancellationToken;
        await new SetPassageTool(routes, invoker).InvokeAsync(new SetPassageRequest(SpeedKnots: 10, DraftMeters: 4), ct);
        var tool = new SetLegAttributesTool(routes, invoker);

        var detail = Value(await tool.InvokeAsync(
            new SetLegAttributesRequest(1, SpeedKnots: 6, DraftMeters: 4), ct));

        Assert.Equal(6, detail.Legs[1].Overrides.SpeedKnots);
        Assert.Equal(6, detail.Legs[1].Effective.SpeedKnots);
        Assert.Null(detail.Legs[1].Overrides.DraftMeters);
        Assert.Equal(4, detail.Legs[1].Effective.DraftMeters);

        // Changing the passage speed changes the inheriting legs only.
        detail = Value(await new SetPassageTool(routes, invoker).InvokeAsync(new SetPassageRequest(SpeedKnots: 12), ct));
        Assert.Equal([12.0, 6.0, 12.0], detail.Legs.Select(l => l.Effective.SpeedKnots!.Value));
    }

    [Fact]
    public async Task SetLegAttributes_clear_resets_overrides_and_unsets_envelope_values()
    {
        var (routes, invoker) = await MakeThreeLegRoute();
        var ct = TestContext.Current.CancellationToken;
        await new SetPassageTool(routes, invoker).InvokeAsync(new SetPassageRequest(SpeedKnots: 10), ct);
        var tool = new SetLegAttributesTool(routes, invoker);
        await tool.InvokeAsync(new SetLegAttributesRequest(0, SpeedKnots: 6, SafetyContourMeters: 10, Note: "n"), ct);

        var detail = Value(await tool.InvokeAsync(
            new SetLegAttributesRequest(0, Clear: ["speedKnots", "SafetyContourMeters", "note"]), ct));

        Assert.Null(detail.Legs[0].Overrides.SpeedKnots);
        Assert.Equal(10, detail.Legs[0].Effective.SpeedKnots);
        Assert.Null(detail.Legs[0].SafetyContourMeters);
        Assert.Null(detail.Legs[0].Note);
    }

    [Fact]
    public async Task SetLegAttributes_rejects_bad_clear_and_out_of_range_overrides()
    {
        var (routes, invoker) = await MakeThreeLegRoute();
        var tool = new SetLegAttributesTool(routes, invoker);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal("clear", Assert.IsType<InvalidArgument>(Error(await tool.InvokeAsync(
            new SetLegAttributesRequest(0, Clear: ["geometryType"]), ct))).Parameter);
        Assert.Equal("clear", Assert.IsType<InvalidArgument>(Error(await tool.InvokeAsync(
            new SetLegAttributesRequest(0, DraftMeters: 3, Clear: ["draftMeters"]), ct))).Parameter);
        Assert.Equal("speedKnots", Assert.IsType<InvalidArgument>(Error(await tool.InvokeAsync(
            new SetLegAttributesRequest(0, SpeedKnots: -2), ct))).Parameter);
        Assert.Equal("safetyMarginMeters", Assert.IsType<InvalidArgument>(Error(await tool.InvokeAsync(
            new SetLegAttributesRequest(0, SafetyMarginMeters: double.NaN), ct))).Parameter);
    }

    [Fact]
    public async Task GetPassage_unknown_route_is_route_not_found()
    {
        var (routes, invoker) = Make();

        var error = Error(await new GetPassageTool(routes, invoker).InvokeAsync(
            new GetPassageRequest("ghost"), TestContext.Current.CancellationToken));

        Assert.IsType<RouteNotFound>(error);
    }

    [Fact]
    public async Task Mutation_raises_routes_service_changed()
    {
        var (routes, invoker) = Make();
        await new CreateRouteTool(routes, invoker).InvokeAsync(new CreateRouteRequest(Id: "r"), TestContext.Current.CancellationToken);

        var raised = 0;
        routes.Changed += (_, _) => raised++;

        await new AppendWaypointTool(routes, invoker).InvokeAsync(new AppendWaypointRequest(1.0, 1.0), TestContext.Current.CancellationToken);

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

    [Fact]
    public void Adapter_route_detail_reports_passage_and_effective_and_overridden_leg_values()
    {
        var routes = new RoutesService();
        var route = routes.Routes.CreateRoute("Demo", "rid");
        route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(50.0, -1.0));
        route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(50.1, -1.1));
        route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(50.2, -1.2));
        route.SetPassageValue(EncDotNet.S100.Viewer.Routing.RoutePassageField.Speed, 10);
        route.SetLegOverride(1, EncDotNet.S100.Viewer.Routing.RoutePassageField.Speed, 6);

        var call = GetRouteMcpAdapter.TranslateResult(ToolResult<RouteDetail>.Ok(RouteProjection.Detail(route, routes)));

        var text = Assert.IsType<TextContentBlock>(call.Content.Single()).Text;
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(10, root.GetProperty("passage").GetProperty("speedKnots").GetDouble());
        Assert.False(root.GetProperty("passage").TryGetProperty("draftMeters", out _));
        var legs = root.GetProperty("legs");
        Assert.Equal(10, legs[0].GetProperty("effective").GetProperty("speedKnots").GetDouble());
        Assert.Empty(legs[0].GetProperty("overrides").EnumerateObject());
        Assert.Equal(6, legs[1].GetProperty("effective").GetProperty("speedKnots").GetDouble());
        Assert.Equal(6, legs[1].GetProperty("overrides").GetProperty("speedKnots").GetDouble());
    }

    [Fact]
    public void Adapter_get_passage_payload_is_camel_case()
    {
        var routes = new RoutesService();
        var route = routes.Routes.CreateRoute("Demo", "rid");
        route.Info.Vessel = new EncDotNet.S100.Viewer.Routing.RouteVesselInfo { Name = "MV Test" };
        route.SetPassageValue(EncDotNet.S100.Viewer.Routing.RoutePassageField.Draft, 4.8);
        route.SetDeparture(new DateTimeOffset(2026, 10, 12, 6, 40, 0, TimeSpan.Zero));

        var call = GetPassageMcpAdapter.TranslateResult(
            ToolResult<GetPassageResult>.Ok(new GetPassageResult(route.Id, RouteProjection.Passage(route))));

        Assert.False(call.IsError);
        var text = Assert.IsType<TextContentBlock>(call.Content.Single()).Text;
        using var doc = JsonDocument.Parse(text);
        var passage = doc.RootElement.GetProperty("passage");
        Assert.Equal("rid", doc.RootElement.GetProperty("routeId").GetString());
        Assert.Equal(4.8, passage.GetProperty("draftMeters").GetDouble());
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 6, 40, 0, TimeSpan.Zero), passage.GetProperty("departure").GetDateTimeOffset());
        Assert.Equal("MV Test", passage.GetProperty("vessel").GetProperty("name").GetString());
        Assert.False(passage.TryGetProperty("speedKnots", out _));
    }

    [Fact]
    public void Adapters_expose_the_passage_parameters()
    {
        var (routes, invoker) = Make();
        var setPassage = SetPassageMcpAdapter.Create(new SetPassageTool(routes, invoker));
        var setLeg = SetLegAttributesMcpAdapter.Create(new SetLegAttributesTool(routes, invoker));

        Assert.Equal("set_passage", setPassage.ProtocolTool.Name);
        var passageProps = setPassage.ProtocolTool.InputSchema.GetProperty("properties");
        foreach (var name in new[] { "draftMeters", "squatMeters", "speedKnots", "departure", "requiredUkcMeters", "crossTrackMeters", "portCrossTrackMeters", "starboardCrossTrackMeters", "clear" })
            Assert.True(passageProps.TryGetProperty(name, out _), $"set_passage is missing {name}");

        var legProps = setLeg.ProtocolTool.InputSchema.GetProperty("properties");
        Assert.True(legProps.TryGetProperty("speedKnots", out _));
        Assert.Contains("array", legProps.GetProperty("clear").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_route_wire_property_has_a_description()
    {
        var types = typeof(RouteDetail).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(RouteDetail).Namespace
                && (t.Name.EndsWith("Dto", StringComparison.Ordinal) || t.Name.EndsWith("Request", StringComparison.Ordinal)
                    || t.Name is nameof(RouteDetail) or nameof(RouteSummary) or nameof(GetPassageResult) or nameof(ListRoutesResult) or nameof(DeleteRouteResult))
                && (t.Name.Contains("Route", StringComparison.Ordinal) || t.Name.Contains("Leg", StringComparison.Ordinal)
                    || t.Name.Contains("Passage", StringComparison.Ordinal) || t.Name.Contains("Waypoint", StringComparison.Ordinal)
                    || t.Name.Contains("Vessel", StringComparison.Ordinal)))
            .ToList();
        Assert.Contains(typeof(PassageDto), types);
        Assert.Contains(typeof(SetPassageRequest), types);

        var missing = types
            .SelectMany(t => t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(p => p.Name != "EqualityContract")
                .Where(p => string.IsNullOrWhiteSpace(
                    System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>(p)?.Description))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();
        Assert.Empty(missing);
    }
}
