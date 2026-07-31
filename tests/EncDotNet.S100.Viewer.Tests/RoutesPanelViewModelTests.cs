using EncDotNet.S100.DataModel;
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
    public void InsertAfterSelectedCommand_InsertsMidpoint()
    {
        var (svc, vm) = Create();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(0, 2));

        svc.SelectedWaypointIndex = 0;
        vm.InsertAfterSelectedCommand.Execute(null);

        Assert.Equal(3, route.Waypoints.Count);
        Assert.Equal(1.0, route.Waypoints[1].Position.Longitude, 6);
        Assert.Equal(1, svc.SelectedWaypointIndex);
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
}
