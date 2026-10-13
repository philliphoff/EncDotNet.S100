using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class RoutesServiceTests
{
    [Fact]
    public void Changed_FiresWhenRouteEdited()
    {
        var svc = new RoutesService();
        var route = svc.Routes.CreateRoute("R1");
        var fired = 0;
        svc.Changed += (_, _) => fired++;

        route.AppendWaypoint(new GeoPosition(10, 20));

        Assert.True(fired >= 1);
    }

    [Fact]
    public void SelectedWaypointIndex_ClampsToActiveRouteWaypoints()
    {
        var svc = new RoutesService();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(1, 1));

        svc.SelectedWaypointIndex = 5; // out of range
        Assert.Null(svc.SelectedWaypointIndex);

        svc.SelectedWaypointIndex = 1;
        Assert.Equal(1, svc.SelectedWaypointIndex);
    }

    [Fact]
    public void SelectedWaypointIndex_NegativeBecomesNull()
    {
        var svc = new RoutesService();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));

        svc.SelectedWaypointIndex = -3;
        Assert.Null(svc.SelectedWaypointIndex);
    }

    [Fact]
    public void SelectionChanged_RaisedOnChange()
    {
        var svc = new RoutesService();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(1, 1));

        var fired = 0;
        svc.SelectionChanged += (_, _) => fired++;

        svc.SelectedWaypointIndex = 1;
        Assert.Equal(1, fired);

        // Setting to the same value does not re-raise.
        svc.SelectedWaypointIndex = 1;
        Assert.Equal(1, fired);
    }

    [Fact]
    public void RemovingWaypoint_NormalizesNowInvalidSelection()
    {
        var svc = new RoutesService();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(1, 1));
        svc.SelectedWaypointIndex = 1;

        route.RemoveWaypoint(1);

        Assert.Null(svc.SelectedWaypointIndex);
    }

    [Fact]
    public void ChangingActiveRoute_ClearsSelectionWhenOutOfRange()
    {
        var svc = new RoutesService();
        var r1 = svc.Routes.CreateRoute("R1");
        r1.AppendWaypoint(new GeoPosition(0, 0));
        r1.AppendWaypoint(new GeoPosition(1, 1));
        svc.SelectedWaypointIndex = 1;

        var r2 = svc.Routes.CreateRoute("R2"); // becomes active, has 0 waypoints
        svc.Routes.SetActiveRoute(r2);

        Assert.Null(svc.SelectedWaypointIndex);
    }

    [Fact]
    public void Selecting_a_leg_clears_the_waypoint_and_the_other_way_round()
    {
        var svc = new RoutesService();
        var route = svc.Routes.CreateRoute("R");
        route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(0, 0));
        route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(0, 1));
        route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(0, 2));
        var changes = 0;
        svc.SelectionChanged += (_, _) => changes++;

        svc.SelectedWaypointIndex = 1;
        svc.SelectedLegIndex = 1;
        Assert.Null(svc.SelectedWaypointIndex);
        Assert.Equal(1, svc.SelectedLegIndex);

        svc.SelectedWaypointIndex = 2;
        Assert.Null(svc.SelectedLegIndex);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void The_selected_leg_is_cleared_when_it_no_longer_exists()
    {
        var svc = new RoutesService();
        var route = svc.Routes.CreateRoute("R");
        route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(0, 0));
        route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(0, 1));
        route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(0, 2));

        svc.SelectedLegIndex = 5;
        Assert.Null(svc.SelectedLegIndex);

        svc.SelectedLegIndex = 1;
        route.RemoveWaypoint(2);
        Assert.Null(svc.SelectedLegIndex);
    }
}
