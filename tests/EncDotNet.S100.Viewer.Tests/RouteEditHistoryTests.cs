using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Tests;

public class RouteEditHistoryTests
{
    private static GeoPosition P(double lat, double lon) => new(lat, lon);

    private static Route ThreePointRoute()
    {
        var route = new Route("r", "Original");
        route.AppendWaypoint(P(0, 0));
        route.AppendWaypoint(P(0, 1));
        route.AppendWaypoint(P(0, 2));
        return route;
    }

    private static double[] Longitudes(Route route) => route.Waypoints.Select(w => w.Position.Longitude).ToArray();

    [Fact]
    public void Edits_made_directly_on_the_route_are_not_recorded()
    {
        var route = ThreePointRoute();

        Assert.False(route.History.CanUndo);
        Assert.False(route.History.CanRedo);
    }

    [Fact]
    public void Undo_reverts_a_drag_an_insert_a_delete_and_a_rename_one_step_at_a_time_and_redo_replays_them()
    {
        var route = ThreePointRoute();
        var history = route.History;

        // A drag: many moves, one step.
        using (history.BeginGesture(RouteEditLabels.MoveWaypoint))
        {
            route.MoveWaypoint(1, P(0.1, 1));
            route.MoveWaypoint(1, P(0.2, 1));
            route.MoveWaypoint(1, P(0.3, 1));
        }
        history.Execute(RouteEditLabels.InsertWaypoint, () => route.InsertWaypoint(1, P(0, 0.5)));
        history.Execute(RouteEditLabels.DeleteWaypoint, () => route.RemoveWaypoint(3));
        history.Execute(RouteEditLabels.Rename, () => route.Name = "Renamed");

        Assert.Equal(4, history.UndoCount);
        Assert.Equal("Renamed", route.Name);
        Assert.Equal(new[] { 0.0, 0.5, 1.0 }, Longitudes(route));

        Assert.True(history.Undo());
        Assert.Equal("Original", route.Name);
        Assert.Equal(RouteEditLabels.Rename, history.RedoLabel);

        Assert.True(history.Undo());
        Assert.Equal(new[] { 0.0, 0.5, 1.0, 2.0 }, Longitudes(route));

        Assert.True(history.Undo());
        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, Longitudes(route));
        Assert.Equal(0.3, route.Waypoints[1].Position.Latitude, 9);

        Assert.True(history.Undo());
        Assert.Equal(0.0, route.Waypoints[1].Position.Latitude, 9);
        Assert.False(history.CanUndo);
        Assert.False(history.Undo());

        Assert.True(history.Redo());
        Assert.Equal(0.3, route.Waypoints[1].Position.Latitude, 9);
        Assert.True(history.Redo());
        Assert.Equal(4, route.Waypoints.Count);
        Assert.True(history.Redo());
        Assert.Equal(3, route.Waypoints.Count);
        Assert.True(history.Redo());
        Assert.Equal("Renamed", route.Name);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Undo_restores_leg_attributes_and_route_info()
    {
        var route = ThreePointRoute();
        route.Info.Vessel = new RouteVesselInfo { Name = "Before" };

        route.History.Execute(RouteEditLabels.EditLeg, () =>
        {
            route.Legs[0].GeometryType = RouteLegGeometryType.Geodesic;
            route.Legs[0].DraftMeters = 11.5;
            route.Info.Vessel!.Name = "After";
            route.NotifyChanged();
        });

        route.History.Undo();

        Assert.Equal(RouteLegGeometryType.Loxodrome, route.Legs[0].GeometryType);
        Assert.Null(route.Legs[0].DraftMeters);
        Assert.Equal("Before", route.Info.Vessel!.Name);
    }

    [Fact]
    public void Undo_restores_the_legs_merged_by_a_delete()
    {
        var route = ThreePointRoute();
        route.Legs[1].Note = "outbound";

        route.History.Execute(RouteEditLabels.DeleteWaypoint, () => route.RemoveWaypoint(1));
        Assert.Single(route.Legs);

        route.History.Undo();

        Assert.Equal(2, route.Legs.Count);
        Assert.Equal("outbound", route.Legs[1].Note);
    }

    [Fact]
    public void A_new_edit_clears_redo()
    {
        var route = ThreePointRoute();
        route.History.Execute(RouteEditLabels.Reverse, route.Reverse);
        route.History.Undo();
        Assert.True(route.History.CanRedo);

        route.History.Execute(RouteEditLabels.AppendWaypoint, () => route.AppendWaypoint(P(0, 3)));

        Assert.False(route.History.CanRedo);
    }

    [Fact]
    public void An_edit_that_changes_nothing_records_no_step()
    {
        var route = ThreePointRoute();

        route.History.Execute(RouteEditLabels.MoveWaypoint, () => route.MoveWaypoint(0, P(0, 0)));
        using (route.History.BeginGesture(RouteEditLabels.MoveWaypoint)) { }

        Assert.False(route.History.CanUndo);
    }

    [Fact]
    public void Edits_inside_an_open_edit_fold_into_one_step()
    {
        var route = ThreePointRoute();

        route.History.Execute("outer", () =>
        {
            route.History.Execute(RouteEditLabels.AppendWaypoint, () => route.AppendWaypoint(P(0, 3)));
            route.History.Execute(RouteEditLabels.AppendWaypoint, () => route.AppendWaypoint(P(0, 4)));
            Assert.False(route.History.CanUndo);
        });

        Assert.Equal(1, route.History.UndoCount);
        Assert.Equal("outer", route.History.UndoLabel);
        route.History.Undo();
        Assert.Equal(3, route.Waypoints.Count);
    }

    [Fact]
    public void Each_route_has_its_own_history()
    {
        var routes = new RouteCollection();
        var a = routes.CreateRoute("A");
        var b = routes.CreateRoute("B");
        var historyChanges = 0;
        routes.HistoryChanged += (_, _) => historyChanges++;

        a.History.Execute(RouteEditLabels.AppendWaypoint, () => a.AppendWaypoint(P(0, 0)));
        b.History.Execute(RouteEditLabels.AppendWaypoint, () => b.AppendWaypoint(P(1, 1)));
        a.History.Undo();

        Assert.Empty(a.Waypoints);
        Assert.Single(b.Waypoints);
        Assert.True(b.History.CanUndo);
        Assert.Equal(3, historyChanges);
    }

    [Fact]
    public void The_history_is_bounded()
    {
        var route = new Route();
        for (var i = 0; i < RouteEditHistory.MaxDepth + 10; i++)
            route.History.Execute(RouteEditLabels.AppendWaypoint, () => route.AppendWaypoint(P(0, i * 0.001)));

        Assert.Equal(RouteEditHistory.MaxDepth, route.History.UndoCount);
    }

    [Fact]
    public void Undo_raises_route_changed_once()
    {
        var route = ThreePointRoute();
        route.History.Execute(RouteEditLabels.Reverse, route.Reverse);
        var changes = 0;
        route.Changed += (_, _) => changes++;

        route.History.Undo();

        Assert.Equal(1, changes);
    }
}
