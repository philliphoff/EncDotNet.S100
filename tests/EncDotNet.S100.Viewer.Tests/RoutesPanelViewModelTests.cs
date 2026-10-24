using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class RoutesPanelViewModelTests
{
    private static (RoutesService Service, RoutesPanelViewModel Vm) Create()
    {
        var svc = new RoutesService();
        var vm = new RoutesPanelViewModel(svc);
        return (svc, vm);
    }

    private static RouteLegRowViewModel FirstLegRow(RoutesPanelViewModel vm)
        => vm.Timeline.OfType<RouteLegRowViewModel>().First();

    [Fact]
    public void Initial_NoRoutes()
    {
        var (_, vm) = Create();
        Assert.False(vm.HasRoutes);
        Assert.Empty(vm.Routes);
        Assert.Null(vm.SelectedRoute);
    }

    [Fact]
    public void AddRouteCommand_AddsAndSelectsRoute()
    {
        var (svc, vm) = Create();

        vm.AddRouteCommand.Execute(null);

        Assert.True(vm.HasRoutes);
        Assert.Single(vm.Routes);
        Assert.NotNull(vm.SelectedRoute);
        Assert.Same(svc.Routes.ActiveRoute, vm.SelectedRoute!.Route);
    }

    [Fact]
    public void RemoveRouteCommand_RemovesSelectedRoute()
    {
        var (svc, vm) = Create();
        vm.AddRouteCommand.Execute(null);

        vm.RemoveRouteCommand.Execute(null);

        Assert.False(vm.HasRoutes);
        Assert.Empty(svc.Routes.Routes);
    }

    [Fact]
    public void RemoveRouteCommand_RemovesPassedRow()
    {
        var (svc, vm) = Create();
        vm.AddRouteCommand.Execute(null);
        vm.AddRouteCommand.Execute(null);
        var firstRow = vm.Routes[0];

        vm.RemoveRouteCommand.Execute(firstRow);

        Assert.Single(svc.Routes.Routes);
        Assert.DoesNotContain(svc.Routes.Routes, r => ReferenceEquals(r, firstRow.Route));
    }

    [Fact]
    public void SelectingRoute_SetsActiveRoute()
    {
        var (svc, vm) = Create();
        vm.AddRouteCommand.Execute(null);
        var first = svc.Routes.Routes[0];
        vm.AddRouteCommand.Execute(null);
        var second = svc.Routes.Routes[1];

        var firstRow = vm.Routes.First(r => r.Route == first);
        vm.SelectedRoute = firstRow;

        Assert.Same(first, svc.Routes.ActiveRoute);
        Assert.NotSame(second, svc.Routes.ActiveRoute);
    }

    [Fact]
    public void Timeline_InterleavesWaypointsAndLegs()
    {
        var (svc, vm) = Create();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(0, 1));
        route.AppendWaypoint(new GeoPosition(0, 2));

        Assert.Equal(5, vm.Timeline.Count);
        Assert.IsType<RouteWaypointRowViewModel>(vm.Timeline[0]);
        Assert.IsType<RouteLegRowViewModel>(vm.Timeline[1]);
        Assert.IsType<RouteWaypointRowViewModel>(vm.Timeline[2]);
        Assert.IsType<RouteLegRowViewModel>(vm.Timeline[3]);
        Assert.IsType<RouteWaypointRowViewModel>(vm.Timeline[4]);
        Assert.NotNull(vm.ActiveRouteDetailMeta);
        Assert.Equal("R1", vm.ActiveRouteName);
    }

    [Fact]
    public void SelectWaypointCommand_HighlightsRow()
    {
        var (svc, vm) = Create();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(0, 1));

        var secondWp = vm.Timeline.OfType<RouteWaypointRowViewModel>().First(w => w.Index == 1);
        vm.SelectWaypointCommand.Execute(secondWp);

        Assert.Equal(1, svc.SelectedWaypointIndex);
        var highlighted = vm.Timeline.OfType<RouteWaypointRowViewModel>().First(w => w.Index == 1);
        Assert.True(highlighted.IsSelected);
    }

    [Fact]
    public void DeleteWaypointCommand_RemovesSelectedWaypoint()
    {
        var (svc, vm) = Create();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(0, 1));
        route.AppendWaypoint(new GeoPosition(0, 2));

        svc.SelectedWaypointIndex = 1;
        vm.DeleteWaypointCommand.Execute(null);

        Assert.Equal(2, route.Waypoints.Count);
    }

    [Fact]
    public void WaypointRow_InsertAfterCommand_InsertsAfterThatRow()
    {
        var (svc, vm) = Create();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(0, 2));

        var firstWp = vm.Timeline.OfType<RouteWaypointRowViewModel>().First(w => w.Index == 0);
        firstWp.InsertAfterCommand.Execute(firstWp);

        Assert.Equal(3, route.Waypoints.Count);
        Assert.Equal(1.0, route.Waypoints[1].Position.Longitude, 6);
    }

    [Fact]
    public void WaypointRow_DeleteCommand_RemovesThatRow()
    {
        var (svc, vm) = Create();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(0, 1));
        route.AppendWaypoint(new GeoPosition(0, 2));

        var middleWp = vm.Timeline.OfType<RouteWaypointRowViewModel>().First(w => w.Index == 1);
        middleWp.DeleteCommand.Execute(middleWp);

        Assert.Equal(2, route.Waypoints.Count);
        Assert.Equal(0, route.Waypoints[0].Position.Longitude, 6);
        Assert.Equal(2, route.Waypoints[1].Position.Longitude, 6);
    }

    [Fact]
    public void ToggleLegGeometryAtCommand_FlipsLegGeometry()
    {
        var (svc, vm) = Create();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(0, 1));

        Assert.Equal(RouteLegGeometryType.Loxodrome, route.Legs[0].GeometryType);

        vm.ToggleLegGeometryAtCommand.Execute(FirstLegRow(vm));

        Assert.Equal(RouteLegGeometryType.Geodesic, route.Legs[0].GeometryType);
    }

    [Fact]
    public void ReverseActiveRouteCommand_ReversesWaypointOrder()
    {
        var (svc, vm) = Create();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(0, 1));
        route.AppendWaypoint(new GeoPosition(0, 2));

        vm.ReverseActiveRouteCommand.Execute(null);

        Assert.Equal(2.0, route.Waypoints[0].Position.Longitude, 6);
        Assert.Equal(0.0, route.Waypoints[2].Position.Longitude, 6);
    }

    [Fact]
    public void RenameFlow_UpdatesRouteName()
    {
        var (svc, vm) = Create();
        vm.AddRouteCommand.Execute(null);

        vm.BeginRenameCommand.Execute(null);
        Assert.True(vm.IsRenamingActiveRoute);
        vm.RenameText = "Approach";
        vm.CommitRenameCommand.Execute(null);

        Assert.False(vm.IsRenamingActiveRoute);
        Assert.Equal("Approach", svc.Routes.ActiveRoute!.Name);
    }

    [Fact]
    public void Choosing_another_route_ends_the_rename_on_the_route_being_renamed()
    {
        var (svc, vm) = Create();
        var first = svc.Routes.CreateRoute("First");
        var second = svc.Routes.CreateRoute("Second");
        svc.Routes.SetActiveRoute(first);

        vm.BeginRenameCommand.Execute(null);
        vm.RenameText = "Renamed";
        svc.Routes.SetActiveRoute(second);

        Assert.False(vm.IsRenamingActiveRoute);
        Assert.Equal("Renamed", first.Name);
        Assert.Equal("Second", second.Name);
    }

    [Fact]
    public void CancelRename_LeavesRouteNameUnchanged()
    {
        var (svc, vm) = Create();
        var route = svc.Routes.CreateRoute("Original");

        vm.BeginRenameCommand.Execute(null);
        vm.RenameText = "Changed";
        vm.CancelRenameCommand.Execute(null);

        Assert.False(vm.IsRenamingActiveRoute);
        Assert.Equal("Original", route.Name);
    }

    private static RouteWaypointRowViewModel WaypointRow(RoutesPanelViewModel vm, int index)
        => vm.Timeline.OfType<RouteWaypointRowViewModel>().Single(r => r.Index == index);

    private static Route TwoPointRoute(RoutesService svc)
    {
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(50.0, -1.0));
        route.AppendWaypoint(new GeoPosition(50.0, -1.5));
        return route;
    }

    [Fact]
    public void Panel_edits_are_undoable_one_step_at_a_time()
    {
        var (svc, vm) = Create();
        var route = TwoPointRoute(svc);
        Assert.False(vm.CanUndo);

        vm.InsertWaypointAfterCommand.Execute(WaypointRow(vm, 0));
        vm.ToggleLegGeometryAtCommand.Execute(FirstLegRow(vm));
        vm.ReverseActiveRouteCommand.Execute(null);

        Assert.True(vm.CanUndo);
        Assert.Equal(3, route.History.UndoCount);

        vm.UndoCommand.Execute(null);
        Assert.Equal(-1.0, route.Waypoints[0].Position.Longitude, 6);
        vm.UndoCommand.Execute(null);
        Assert.Equal(RouteLegGeometryType.Loxodrome, route.Legs[0].GeometryType);
        vm.UndoCommand.Execute(null);
        Assert.Equal(2, route.Waypoints.Count);
        Assert.False(vm.CanUndo);
        Assert.True(vm.CanRedo);

        vm.RedoCommand.Execute(null);
        Assert.Equal(3, route.Waypoints.Count);
    }

    [Fact]
    public void Rename_is_undoable()
    {
        var (svc, vm) = Create();
        var route = TwoPointRoute(svc);

        vm.BeginRenameCommand.Execute(null);
        vm.RenameText = "Approach";
        vm.CommitRenameCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        Assert.Equal("R1", route.Name);
    }

    [Fact]
    public void Undo_follows_the_active_route()
    {
        var (svc, vm) = Create();
        var first = TwoPointRoute(svc);
        first.History.Execute(RouteEditLabels.Reverse, first.Reverse);
        var second = svc.Routes.CreateRoute("R2");

        Assert.Same(second, svc.Routes.ActiveRoute);
        Assert.False(vm.CanUndo);

        vm.SelectedRoute = vm.Routes[0];
        Assert.True(vm.CanUndo);
    }

    [Fact]
    public void Waypoint_name_commits_and_blank_clears_it()
    {
        var (svc, vm) = Create();
        var route = TwoPointRoute(svc);

        var row = WaypointRow(vm, 1);
        row.NameText = "Nab Tower";
        row.CommitNameCommand.Execute(null);
        Assert.Equal("Nab Tower", route.Waypoints[1].Name);
        Assert.Equal("Nab Tower", WaypointRow(vm, 1).Label);

        row = WaypointRow(vm, 1);
        row.NameText = "  ";
        row.CommitNameCommand.Execute(null);
        Assert.Null(route.Waypoints[1].Name);

        vm.UndoCommand.Execute(null);
        Assert.Equal("Nab Tower", route.Waypoints[1].Name);
    }

    [Fact]
    public void Waypoint_position_shows_the_app_format()
    {
        var (svc, vm) = Create();
        TwoPointRoute(svc);

        Assert.Equal(LatLonFormatter.Format(50.0, -1.0), WaypointRow(vm, 0).CoordsText);
    }

    [Fact]
    public void Typed_position_moves_the_waypoint_and_is_undoable()
    {
        var (svc, vm) = Create();
        var route = TwoPointRoute(svc);

        var row = WaypointRow(vm, 0);
        row.CoordsText = "50°46.024'N 001°15.558'W";
        row.CommitCoordsCommand.Execute(null);

        Assert.Equal(50.76707, route.Waypoints[0].Position.Latitude, 4);
        Assert.Equal(-1.2593, route.Waypoints[0].Position.Longitude, 4);
        vm.UndoCommand.Execute(null);
        Assert.Equal(50.0, route.Waypoints[0].Position.Latitude, 6);
    }

    [Fact]
    public void Bad_position_shows_an_error_and_does_not_move_the_waypoint()
    {
        var (svc, vm) = Create();
        var route = TwoPointRoute(svc);

        var row = WaypointRow(vm, 0);
        row.CoordsText = "somewhere nice";
        row.CommitCoordsCommand.Execute(null);

        Assert.True(row.HasCoordsError);
        Assert.Equal(Strings.Routes_CoordinateError, row.CoordsError);
        Assert.Equal(50.0, route.Waypoints[0].Position.Latitude, 6);
        Assert.False(route.History.CanUndo);

        row.RevertCoordsCommand.Execute(null);
        Assert.False(row.HasCoordsError);
        Assert.Equal(LatLonFormatter.Format(50.0, -1.0), row.CoordsText);
    }

    [Fact]
    public void Selection_moves_the_highlight_without_rebuilding_rows()
    {
        var (svc, vm) = Create();
        TwoPointRoute(svc);
        var row = WaypointRow(vm, 1);
        row.NameText = "typing…";

        svc.SelectedWaypointIndex = 1;

        Assert.Same(row, WaypointRow(vm, 1));
        Assert.True(row.IsSelected);
        Assert.Equal("typing…", row.NameText);
    }

    [Fact]
    public void Edit_toggle_follows_and_sets_edit_mode()
    {
        var (svc, vm) = Create();
        TwoPointRoute(svc);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.IsEditing = true;
        Assert.True(svc.IsEditModeActive);

        svc.IsEditModeActive = false;
        Assert.False(vm.IsEditing);
        Assert.Contains(nameof(RoutesPanelViewModel.IsEditing), changes);
    }

    [Fact]
    public void Turning_edit_off_commits_an_open_rename_and_leaves_edit_mode()
    {
        var (svc, vm) = Create();
        var route = TwoPointRoute(svc);
        vm.IsEditing = true;
        vm.BeginRenameCommand.Execute(null);
        vm.RenameText = "Committed";

        vm.IsEditing = false;

        Assert.False(svc.IsEditModeActive);
        Assert.False(vm.IsRenamingActiveRoute);
        Assert.Equal("Committed", route.Name);
    }

    [Fact]
    public void Check_passage_tooltip_asks_for_waypoints_then_a_draft()
    {
        var (svc, vm) = Create();
        svc.Routes.CreateRoute("Empty");
        Assert.Equal("Add two waypoints to check the route", vm.CheckPassageTooltip);

        TwoPointRoute(svc);

        Assert.False(vm.CanCheckPassage); // no check service in this panel
        Assert.False(vm.IsCheckingPassage);
        Assert.Equal("Check the route against your settings; add a draft to check clearance too", vm.CheckPassageTooltip);
    }

    [Fact]
    public void Edit_hint_names_the_shortcut()
    {
        var (_, vm) = Create();

        Assert.Equal($"{ShortcutText.Command("E")} edit · Esc done", vm.EditHint);
    }

    [Fact]
    public void Zoom_to_route_covers_every_waypoint()
    {
        var route = new Route();
        route.AppendWaypoint(new GeoPosition(50.0, -1.0));
        route.AppendWaypoint(new GeoPosition(50.5, -1.5));

        var extent = RoutesPanelViewModel.ResolveZoomExtent(route)!;
        var (west, south) = Mapsui.Projections.SphericalMercator.FromLonLat(-1.5, 50.0);
        var (east, north) = Mapsui.Projections.SphericalMercator.FromLonLat(-1.0, 50.5);

        Assert.Equal(west, extent.MinX, 3);
        Assert.Equal(south, extent.MinY, 3);
        Assert.Equal(east, extent.MaxX, 3);
        Assert.Equal(north, extent.MaxY, 3);
    }

    [Fact]
    public void Zoom_to_a_one_waypoint_route_uses_a_small_box()
    {
        var route = new Route();
        route.AppendWaypoint(new GeoPosition(50.0, -1.0));

        var extent = RoutesPanelViewModel.ResolveZoomExtent(route)!;

        Assert.True(extent.Width > 0);
        Assert.True(extent.Height > 0);
    }

    [Fact]
    public void Selecting_a_leg_row_selects_the_leg_and_highlights_only_it()
    {
        var (svc, vm) = Create();
        TwoPointRoute(svc);
        svc.SelectedWaypointIndex = 0;

        vm.SelectLegCommand.Execute(FirstLegRow(vm));

        Assert.Equal(0, svc.SelectedLegIndex);
        Assert.True(FirstLegRow(vm).IsSelected);
        Assert.False(WaypointRow(vm, 0).IsSelected);
    }
}
