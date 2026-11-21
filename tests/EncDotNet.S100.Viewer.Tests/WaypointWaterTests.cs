using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// append_waypoint, insert_waypoint and move_waypoint report the water at the
/// point as sample reads it (#1628). With no chart loaded every point is
/// uncharted, so it's never navigable: enough to see the defaults, the dry
/// run and the reporting. sample's own tests cover the charted cases.
/// </summary>
public class WaypointWaterTests
{
    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class EmptyCatalog : IDatasetCatalog
    {
        public IReadOnlyList<LoadedDataset> Datasets => [];

        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }

    private static (RoutesService Routes, InlineInvoker Invoker, WaypointWater Water) Make()
    {
        var transforms = new ProjNetCrsTransformFactory();
        var sample = new SampleTool(new EmptyCatalog(), transforms, new RouteCheckSourceCache(transforms), TimeProvider.System);
        var routes = new RoutesService(new ClockViewTime(TimeProvider.System));
        var invoker = new InlineInvoker();
        routes.Routes.CreateRoute("Test", "r");
        return (routes, invoker, new WaypointWater(sample));
    }

    private static T Value<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var error) ? error.Message : "");
        return value!;
    }

    [Fact]
    public async Task Without_a_draft_or_minimum_the_water_is_reported_but_not_judged()
    {
        var (routes, invoker, water) = Make();

        var detail = Value(await new AppendWaypointTool(routes, invoker, water).InvokeAsync(new AppendWaypointRequest(48.5, -122.7), TestContext.Current.CancellationToken));

        var report = detail.Water!;
        Assert.Equal(0, report.WaypointIndex);
        Assert.True(report.Applied);
        Assert.Equal("uncharted", report.WaterMask);
        Assert.Null(report.Depth);
        Assert.Null(report.MinDepthMeters);
        Assert.Null(report.Navigable);
        Assert.Single(detail.Waypoints);
    }

    [Fact]
    public async Task The_routes_draft_squat_and_required_ukc_are_the_default_minimum()
    {
        var (routes, invoker, water) = Make();
        await new SetPassageTool(routes, invoker).InvokeAsync(new SetPassageRequest(DraftMeters: 1.1, SquatMeters: 0.2, RequiredUkcMeters: 0.3), TestContext.Current.CancellationToken);

        var detail = Value(await new AppendWaypointTool(routes, invoker, water).InvokeAsync(new AppendWaypointRequest(48.5, -122.7), TestContext.Current.CancellationToken));

        var report = detail.Water!;
        Assert.Equal(1.6, report.MinDepthMeters);
        Assert.Equal("route", report.MinDepthFrom);
        Assert.False(report.Navigable);
        Assert.Equal("uncharted", report.NotNavigableReason);
        Assert.NotNull(report.NearestNavigable!.Reason);
    }

    [Fact]
    public async Task Validate_reports_without_editing()
    {
        var (routes, invoker, water) = Make();
        await new AppendWaypointTool(routes, invoker).InvokeAsync(new AppendWaypointRequest(48.5, -122.7), TestContext.Current.CancellationToken);

        var moved = Value(await new MoveWaypointTool(routes, invoker, water).InvokeAsync(
            new MoveWaypointRequest(0, 48.6, -122.8, MinDepthMeters: 2, Validate: true), TestContext.Current.CancellationToken));
        var inserted = Value(await new InsertWaypointTool(routes, invoker, water).InvokeAsync(
            new InsertWaypointRequest(1, 48.7, -122.9, Validate: true), TestContext.Current.CancellationToken));

        Assert.False(moved.Water!.Applied);
        Assert.Equal("request", moved.Water.MinDepthFrom);
        Assert.Equal(2, moved.Water.MinDepthMeters);
        Assert.Equal(48.5, moved.Waypoints[0].Lat);
        Assert.False(inserted.Water!.Applied);
        Assert.Equal(1, inserted.Water.WaypointIndex);
        Assert.Single(routes.Routes.FindById("r")!.Waypoints);
    }

    [Fact]
    public async Task An_invalid_edit_fails_before_the_water_is_checked()
    {
        var (routes, invoker, water) = Make();

        var result = await new MoveWaypointTool(routes, invoker, water).InvokeAsync(new MoveWaypointRequest(3, 48.6, -122.8), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("index", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Theory]
    [InlineData(48.5, -122.7, null, null, null)]
    [InlineData(null, null, 48.5, -122.7, null)]
    [InlineData(48.5, -122.7, 48.5, -122.7, null)]
    [InlineData(48.5, -122.7, 48.6, null, "latitude")]
    [InlineData(null, null, null, null, "latitude")]
    [InlineData(48.5, null, null, null, "longitude")]
    public void Waypoint_positions_take_latitude_and_longitude_or_the_older_lat_and_lon(double? latitude, double? longitude, double? lat, double? lon, string? error)
    {
        var problem = AppendWaypointMcpAdapter.Position(latitude, longitude, lat, lon, out var position);

        Assert.Equal(error, problem?.Parameter);
        if (error is null)
        {
            Assert.Equal(48.5, position.Latitude);
            Assert.Equal(-122.7, position.Longitude);
        }
    }
}
