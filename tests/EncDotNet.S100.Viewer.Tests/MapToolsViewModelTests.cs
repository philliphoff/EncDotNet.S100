using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class MapToolsViewModelTests
{
    private static MapToolsViewModel CreateTools(RoutesService? routes = null) =>
        TestMapTools.Create(
            new ViewerSettingsService(new ViewerSettings()),
            routes ?? new RoutesService(new ClockViewTime(TimeProvider.System)));

    [Fact]
    public void IsPickModeActive_DefaultsToFalse()
    {
        var vm = CreateTools();
        Assert.False(vm.IsPickModeActive);
    }

    [Fact]
    public void TogglePickModeCommand_FlipsState()
    {
        var vm = CreateTools();

        vm.TogglePickModeCommand.Execute(null);
        Assert.True(vm.IsPickModeActive);

        vm.TogglePickModeCommand.Execute(null);
        Assert.False(vm.IsPickModeActive);
    }

    [Fact]
    public void ExitPickModeCommand_TurnsOffAndIsIdempotent()
    {
        var vm = CreateTools();
        vm.IsPickModeActive = true;

        vm.ExitPickModeCommand.Execute(null);
        Assert.False(vm.IsPickModeActive);

        vm.ExitPickModeCommand.Execute(null);
        Assert.False(vm.IsPickModeActive);
    }

    [Fact]
    public void IsPickModeActive_RaisesPropertyChanged()
    {
        var vm = CreateTools();
        var fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MapToolsViewModel.IsPickModeActive))
                fired++;
        };

        vm.TogglePickModeCommand.Execute(null);
        vm.TogglePickModeCommand.Execute(null);

        Assert.Equal(2, fired);
    }

    [Fact]
    public void ToggleRouteEditModeCommand_FlipsState()
    {
        var vm = CreateTools();

        vm.ToggleRouteEditModeCommand.Execute(null);
        Assert.True(vm.IsRouteEditModeActive);

        vm.ToggleRouteEditModeCommand.Execute(null);
        Assert.False(vm.IsRouteEditModeActive);
    }

    [Fact]
    public void RouteEditMode_AndPickMode_AreMutuallyExclusive()
    {
        var vm = CreateTools();

        vm.IsPickModeActive = true;
        vm.ToggleRouteEditModeCommand.Execute(null);

        Assert.True(vm.IsRouteEditModeActive);
        Assert.False(vm.IsPickModeActive);
    }

    [Fact]
    public void PromoteMeasurementToRouteCommand_DisabledWithoutMeasurement()
    {
        var vm = CreateTools();
        Assert.False(vm.PromoteMeasurementToRouteCommand.CanExecute(null));
    }

    [Fact]
    public void PromoteMeasurement_KeepsGreatCircleGeometry()
    {
        var routes = new RoutesService(new ClockViewTime(TimeProvider.System));
        var vm = CreateTools(routes);
        vm.ToggleMeasureModeCommand.Execute(null);
        vm.MeasureSession.SetGeometry(EncDotNet.S100.Navigation.LegGeometry.GreatCircle);
        vm.MeasureSession.State.SetWaypoints([new(0, 0), new(10, 10), new(20, 0)]);
        vm.MeasureSession.NotifyChanged();
        Assert.True(vm.PromoteMeasurementToRouteCommand.CanExecute(null));

        vm.PromoteMeasurementToRouteCommand.Execute(null);

        var route = routes.Routes.Routes[^1];
        Assert.Equal(2, route.Legs.Count);
        Assert.All(route.Legs, l => Assert.Equal(Routing.RouteLegGeometryType.Geodesic, l.GeometryType));
        Assert.True(vm.IsRouteEditModeActive);
    }

    [Fact]
    public void MeasureCard_Close_AndActivationRequest_DriveTheTool()
    {
        var vm = CreateTools();

        vm.MeasureSession.RequestActivation();
        Assert.True(vm.IsMeasureModeActive);

        vm.MeasureCard.CloseCommand.Execute(null);
        Assert.False(vm.IsMeasureModeActive);
    }

    [Fact]
    public void IsToolSummaryVisible_TrueWhileRouteEditing()
    {
        var vm = CreateTools();
        Assert.False(vm.IsToolSummaryVisible);

        vm.ToggleRouteEditModeCommand.Execute(null);
        Assert.True(vm.IsToolSummaryVisible);
    }
}
