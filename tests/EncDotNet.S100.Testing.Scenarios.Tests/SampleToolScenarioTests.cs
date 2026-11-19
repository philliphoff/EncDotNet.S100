using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios.World;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// The MCP <c>sample</c> tool's depth (#1557) on "Spit across scales": the
/// same charted depth, water level and available depth as the clearance
/// check reads at each of its samples, and the finest cell's depth area
/// with its range where the cells overlap.
/// </summary>
public sealed class SampleToolScenarioTests(SpitAcrossScalesFixture fixture) : IClassFixture<SpitAcrossScalesFixture>
{
    private const string Harbour = "101ZZ00SPIT5";
    private static readonly DateTimeOffset Departure = new(2026, 3, 1, 4, 0, 0, TimeSpan.Zero);

    private readonly FileDatasetCatalog _catalog = ScenarioLoading.Catalog(fixture.Scenario);

    private ScenarioDefinition Definition => fixture.Definition;

    private SyntheticWorld World => fixture.Definition.World;

    [Fact]
    public async Task Depth_agrees_with_the_clearance_check_at_every_sample()
    {
        var check = CheckRoute();
        var tool = Tool();

        Assert.NotEmpty(check.Samples);
        foreach (var sample in check.Samples)
        {
            var result = await Depth(tool, sample.Position, sample.Time);
            var expected = Assert.IsType<ChartedDepth>(sample.Depth);
            var depth = Assert.IsType<ChartedDepthValue>(result.Depth);

            Assert.Equal(expected.DepthMeters, depth.DepthMeters);
            Assert.Equal(expected.DatasetId, depth.DatasetId);
            Assert.Equal(expected.IsLowConfidence, depth.LowConfidence);
            Assert.Equal(expected.Kind == ChartedDepthKind.Bathymetry ? "bathymetry" : "depth_area", depth.Kind);
            Assert.Equal("water", result.WaterMask);

            var level = Assert.IsType<SampleWaterLevel>(result.WaterLevel);
            Assert.Equal("applied", level.Status);
            Assert.Equal(sample.WaterLevel.Source!.DatasetId, level.DatasetId);
            Assert.Equal(sample.WaterLevel.HeightMeters!.Value, level.HeightMeters!.Value, 3);
            Assert.Equal(expected.DepthMeters + sample.WaterLevel.HeightMeters!.Value, result.AvailableDepthMeters!.Value, 3);
        }
    }

    [Fact]
    public async Task Where_cells_overlap_the_finest_cells_depth_area_and_its_range_are_read()
    {
        var result = await Depth(Tool(), At("spit-shoulder"), time: null);

        var depth = Assert.IsType<ChartedDepthValue>(result.Depth);
        Assert.Equal("depth_area", depth.Kind);
        Assert.Equal(Harbour, depth.DatasetId);
        Assert.Equal(2.0, depth.DepthRangeMinimumMeters);
        Assert.Equal(5.0, depth.DepthRangeMaximumMeters);
        Assert.NotNull(depth.FeatureId);
        Assert.Null(depth.GridCell);
        Assert.True(depth.LowConfidence);
        Assert.Equal(WorldDatums.LowestAstronomicalTide, depth.VerticalDatum);
        Assert.Null(result.WaterLevel);
        Assert.Null(result.AvailableDepthMeters);
    }

    [Fact]
    public async Task On_the_S102_surface_the_grid_cell_is_named()
    {
        var result = await Depth(Tool(), At("channel-west"), time: null);

        var depth = Assert.IsType<ChartedDepthValue>(result.Depth);
        Assert.Equal("bathymetry", depth.Kind);
        Assert.NotNull(depth.GridCell);
        Assert.Null(depth.FeatureId);
        Assert.Null(depth.DepthRangeMinimumMeters);
        Assert.False(depth.LowConfidence);
    }

    [Fact]
    public async Task On_the_mainland_the_mask_is_land()
    {
        var result = await Depth(Tool(), World.Frame.ToGeo(3600, 400), time: null);

        Assert.Equal("land", result.WaterMask);
    }

    [Fact]
    public async Task A_profile_names_its_shoalest_sample_and_keeps_the_vertices()
    {
        var points = Definition.RoutePositions;
        var polyline = new GeoPolyline(points.Select(p => new GeoPoint(p.Latitude, p.Longitude)).ToList());

        var result = await Tool().InvokeAsync(
            new SampleRequest(SampleQuantity.Depth, Polyline: polyline, SpacingMeters: 100, Time: Departure),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var output), result.TryGetError(out var e) ? e.Message : "");
        var profile = Assert.IsType<SampleProfileResult>(output!.Profile);
        Assert.Equal("depth", profile.Quantity);
        Assert.True(profile.Samples.Count > points.Count);
        Assert.Equal(Enumerable.Range(0, points.Count), profile.Samples.Where(s => s.VertexIndex is not null).Select(s => s.VertexIndex!.Value));
        Assert.All(profile.Samples.Zip(profile.Samples.Skip(1)), pair =>
        {
            Assert.True(pair.Second.DistanceMeters >= pair.First.DistanceMeters);
            Assert.True(pair.Second.DistanceMeters - pair.First.DistanceMeters <= 100.5);
        });

        var shoalest = Assert.IsType<SampleProfileShoalest>(profile.Shoalest);
        var lowest = profile.Samples.Where(s => s.Depth?.AvailableDepthMeters is not null).Min(s => s.Depth!.AvailableDepthMeters!.Value);
        Assert.Equal(lowest, shoalest.AvailableDepthMeters);
        Assert.Equal(lowest, profile.Samples[shoalest.Index].Depth!.AvailableDepthMeters);
    }

    private SampleTool Tool()
    {
        var transforms = new ProjNetCrsTransformFactory();
        return new SampleTool(_catalog, transforms, new RouteCheckSourceCache(transforms), TimeProvider.System);
    }

    private static async Task<ChartedDepthSampleResult> Depth(SampleTool tool, GeoPosition position, DateTimeOffset? time)
    {
        var result = await tool.InvokeAsync(
            new SampleRequest(SampleQuantity.Depth, Latitude: position.Latitude, Longitude: position.Longitude, Time: time),
            TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var output), result.TryGetError(out var e) ? e.Message : "");
        return Assert.IsType<ChartedDepthSampleResult>(output!.Depth);
    }

    // Just north-east of a named place, so an S-102 lookup lands in the cell
    // whose south-west node is the place.
    private GeoPosition At(string place)
    {
        var (x, y) = Definition.Places[place];
        return World.Frame.ToGeo(x + 0.1, y + 0.1);
    }

    private PassageCheckResult CheckRoute()
    {
        var points = Definition.RoutePositions;
        var legs = points.Zip(points.Skip(1), (a, b) => PassageLegs.Leg([a, b], 6.0, speedKnots: 8, squat: 0.3, ukc: 1.0)).ToList();
        return ScenarioChecks.Clearance(ScenarioLoading.Sources(fixture.Scenario), new PassagePlan(legs, Departure), PassageLegs.Settings(Departure));
    }
}
