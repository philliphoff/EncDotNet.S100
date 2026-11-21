using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// sample's nearest navigable point (#1559) over a synthetic S-101 cell:
/// 10 m of water west of 0.025°, 1 m east of it.
/// </summary>
public sealed class SampleNavigableTests
{
    [Fact]
    public async Task A_point_in_deep_enough_water_is_navigable_and_does_not_move()
    {
        var depth = await Depth(new SampleRequest(SampleQuantity.Depth, Latitude: 0.02, Longitude: 0.01, MinDepthMeters: 2));

        Assert.True(depth.Navigable);
        Assert.Null(depth.NotNavigableReason);
        Assert.Null(depth.NearestNavigable);
    }

    [Fact]
    public async Task A_point_on_the_shoal_gets_the_nearest_navigable_point_west_of_it()
    {
        var depth = await Depth(new SampleRequest(SampleQuantity.Depth, Latitude: 0.02, Longitude: 0.027, MinDepthMeters: 2));

        Assert.False(depth.Navigable);
        Assert.Equal("shoal", depth.NotNavigableReason);
        var nearest = depth.NearestNavigable!;
        Assert.True(nearest.Longitude < 0.025);
        Assert.InRange(nearest.BearingDegrees!.Value, 260, 280);
        Assert.InRange(nearest.DistanceMeters!.Value, 200, 400);
        Assert.Equal(10, nearest.DepthMeters);
        Assert.Equal("depth_area", nearest.Depth!.Kind);
        Assert.Equal("101SNAP", nearest.Depth.DatasetId);
        Assert.Null(nearest.Reason);

        // sample agrees that the point it gave is navigable.
        var there = await Depth(new SampleRequest(SampleQuantity.Depth, Latitude: nearest.Latitude, Longitude: nearest.Longitude, MinDepthMeters: 2));
        Assert.True(there.Navigable);
    }

    [Fact]
    public async Task Clearance_moves_a_point_off_the_shoal_edge()
    {
        var bare = await Depth(new SampleRequest(SampleQuantity.Depth, Latitude: 0.02, Longitude: 0.0245, MinDepthMeters: 2));
        var roomy = await Depth(new SampleRequest(SampleQuantity.Depth, Latitude: 0.02, Longitude: 0.0245, MinDepthMeters: 2, ClearanceMeters: 300));

        Assert.True(bare.Navigable);
        Assert.Equal("too_close", roomy.NotNavigableReason);
        Assert.True(roomy.NearestNavigable!.ClearanceMeters >= 300);
    }

    [Fact]
    public async Task Nothing_in_the_radius_gives_a_reason()
    {
        var depth = await Depth(new SampleRequest(SampleQuantity.Depth, Latitude: 0.02, Longitude: 0.045, MinDepthMeters: 2, SearchRadiusMeters: 500));

        Assert.False(depth.Navigable);
        Assert.Null(depth.NearestNavigable!.Latitude);
        Assert.Contains("searchRadiusMeters", depth.NearestNavigable.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("clearanceMeters")]
    [InlineData("searchRadiusMeters")]
    [InlineData("minDepthMeters")]
    public async Task Bad_navigability_arguments_are_refused(string parameter)
    {
        var request = parameter switch
        {
            "clearanceMeters" => new SampleRequest(SampleQuantity.Depth, Latitude: 0, Longitude: 0, ClearanceMeters: 10),
            "searchRadiusMeters" => new SampleRequest(SampleQuantity.Depth, Latitude: 0, Longitude: 0, MinDepthMeters: 2, SearchRadiusMeters: 50_000),
            _ => new SampleRequest(SampleQuantity.WaterLevel, Latitude: 0, Longitude: 0, MinDepthMeters: 2),
        };

        var result = await Tool().InvokeAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(parameter, Assert.IsType<InvalidArgument>(error).Parameter);
    }

    private static async Task<ChartedDepthSampleResult> Depth(SampleRequest request)
    {
        var result = await Tool().InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var output), result.TryGetError(out var e) ? e.Message : "");
        return output!.Depth!;
    }

    private static SampleTool Tool()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101(
            "101SNAP",
            S101AreaSynth.Cell(
            [
                S101AreaSynth.DataCoverage(22_000, S101AreaSynth.Box(0, 0, 0.05, 0.05)),
                S101AreaSynth.DepthArea(10, S101AreaSynth.Box(0, 0, 0.05, 0.025)),
                S101AreaSynth.DepthArea(1, S101AreaSynth.Box(0, 0.025, 0.05, 0.05)),
            ]),
            LoadedDatasetFactory.Box(0, 0, 0.05, 0.05)));
        var transforms = new ProjNetCrsTransformFactory();
        return new SampleTool(catalog, transforms, new RouteCheckSourceCache(transforms), TimeProvider.System);
    }
}
