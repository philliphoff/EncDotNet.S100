using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The route edits the Routes panel and the MCP route tools share (#1400).</summary>
public class RouteEditCommandsTests
{
    private static Route Route(params (double Lat, double Lon)[] points)
    {
        var route = new Route("R1", "Route");
        foreach (var (lat, lon) in points)
            route.AppendWaypoint(new GeoPosition(lat, lon));
        return route;
    }

    [Fact]
    public void Insert_after_a_waypoint_goes_to_the_middle_of_its_outbound_leg()
    {
        var route = Route((50, -1), (51, 1), (52, 3));

        Assert.Equal(1, RouteEditCommands.InsertWaypointAfter(route, 0));

        Assert.Equal(4, route.Waypoints.Count);
        Assert.Equal(new GeoPosition(50.5, 0), route.Waypoints[1].Position);
        Assert.Equal(RouteEditLabels.InsertWaypoint, route.History.UndoLabel);
    }

    [Fact]
    public void Insert_after_the_last_waypoint_extends_the_route_by_one_inbound_leg()
    {
        var route = Route((50, -1), (51, 1));

        Assert.Equal(2, RouteEditCommands.InsertWaypointAfter(route, 1));

        Assert.Equal(new GeoPosition(52, 3), route.Waypoints[2].Position);
    }

    [Fact]
    public void Insert_after_a_lone_waypoint_nudges_east()
    {
        var route = Route((50, -1));

        RouteEditCommands.InsertWaypointAfter(route, 0);

        Assert.Equal(50, route.Waypoints[1].Position.Latitude);
        Assert.Equal(-0.99, route.Waypoints[1].Position.Longitude, 1e-9);
    }

    [Fact]
    public void Insert_with_metadata_is_one_undo_step()
    {
        var route = Route((50, -1), (51, 1));

        var waypoint = RouteEditCommands.InsertWaypoint(route, 1, new GeoPosition(50.5, 0), new RouteWaypointMetadata(7, "Mid", Fixed: true, TurnRadiusNm: 0.2));

        Assert.Equal((7, "Mid", true, 0.2), (waypoint.Number, waypoint.Name, waypoint.Fixed, waypoint.TurnRadiusNm));
        Assert.Equal(1, route.History.UndoCount);
        route.History.Undo();
        Assert.Equal(2, route.Waypoints.Count);
    }

    [Fact]
    public void Delete_and_leg_edits_are_undoable_and_a_no_op_leg_edit_records_nothing()
    {
        var route = Route((50, -1), (51, 1), (52, 3));

        RouteEditCommands.DeleteWaypoint(route, 1);
        Assert.Equal(RouteEditLabels.DeleteWaypoint, route.History.UndoLabel);
        Assert.Equal(2, route.Waypoints.Count);

        var geometry = route.Legs[0].GeometryType;
        RouteEditCommands.ToggleLegGeometry(route, 0);
        Assert.NotEqual(geometry, route.Legs[0].GeometryType);
        Assert.Equal(RouteEditLabels.EditLeg, route.History.UndoLabel);

        var steps = route.History.UndoCount;
        RouteEditCommands.EditLeg(route, 0, overrides: null, attributes: leg => leg.Note = leg.Note);
        Assert.Equal(steps, route.History.UndoCount);
    }
}
