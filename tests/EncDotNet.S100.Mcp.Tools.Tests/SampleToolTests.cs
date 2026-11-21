using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// The <c>sample</c> tool's request rules and its depth result with no
/// charts loaded (#1557). Depth over generated charts is tested in the
/// scenario tests (<c>SampleToolScenarioTests</c>).
/// </summary>
public class SampleToolTests
{
    private static readonly GeoPolyline Line = new([new GeoPoint(0, 0), new GeoPoint(0, 0.01)]);

    private static async Task<ToolError> Err(SampleRequest request)
    {
        var result = await TestSampleTool.Create(new FakeDatasetCatalog()).InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.TryGetError(out var error));
        return error!;
    }

    private static async Task<SampleOutput> Ok(SampleRequest request)
    {
        var result = await TestSampleTool.Create(new FakeDatasetCatalog()).InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    [Fact]
    public async Task A_quantity_or_a_spec_is_needed()
    {
        var error = Assert.IsType<InvalidArgument>(await Err(new SampleRequest(null, Latitude: 0, Longitude: 0)));
        Assert.Equal("quantity", error.Parameter);
    }

    [Fact]
    public async Task A_point_and_a_polyline_are_not_both_taken()
    {
        Assert.IsType<InvalidArgument>(await Err(new SampleRequest(SampleQuantity.Depth, Latitude: 0, Longitude: 0, Polyline: Line)));
        Assert.IsType<InvalidArgument>(await Err(new SampleRequest(SampleQuantity.Depth)));
        Assert.IsType<InvalidArgument>(await Err(new SampleRequest(SampleQuantity.Depth, Latitude: 0)));
    }

    [Fact]
    public async Task A_spec_must_carry_the_quantity()
    {
        var error = Assert.IsType<InvalidArgument>(await Err(new SampleRequest(SampleQuantity.Current, new SpecRef("S-104", default), 0, 0)));
        Assert.Equal("spec", error.Parameter);
    }

    [Fact]
    public async Task Spacing_applies_to_a_polyline_and_is_positive()
    {
        Assert.Equal("spacingMeters", Assert.IsType<InvalidArgument>(await Err(new SampleRequest(SampleQuantity.Depth, Latitude: 0, Longitude: 0, SpacingMeters: 10))).Parameter);
        Assert.Equal("spacingMeters", Assert.IsType<InvalidArgument>(await Err(new SampleRequest(SampleQuantity.Depth, Polyline: Line, SpacingMeters: 0))).Parameter);
    }

    [Fact]
    public async Task Too_many_samples_suggest_a_wider_spacing()
    {
        // About 1.1 km at 0.1 m spacing: thousands of samples.
        var error = Assert.IsType<InvalidArgument>(await Err(new SampleRequest(SampleQuantity.Depth, Polyline: Line, SpacingMeters: 0.1)));
        Assert.Equal("spacingMeters", error.Parameter);
        Assert.Contains("at least", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Depth_takes_one_time()
    {
        var times = new TimeQuery.Range(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1));
        Assert.Equal("times", Assert.IsType<InvalidArgument>(await Err(new SampleRequest(SampleQuantity.Depth, Latitude: 0, Longitude: 0, Times: times))).Parameter);
    }

    [Fact]
    public async Task Depth_with_no_charts_is_uncharted_with_no_depth()
    {
        var output = await Ok(new SampleRequest(SampleQuantity.Depth, Latitude: 1, Longitude: 2, Time: DateTimeOffset.UnixEpoch));

        var depth = Assert.IsType<ChartedDepthSampleResult>(output.Depth);
        Assert.Same(depth, output.Payload);
        Assert.Equal("uncharted", depth.WaterMask);
        Assert.Null(depth.Depth);
        Assert.Null(depth.WaterLevel);
        Assert.Equal(DateTimeOffset.UnixEpoch, depth.Time);
    }

    [Fact]
    public async Task A_depth_profile_with_no_charts_has_no_shoalest()
    {
        var output = await Ok(new SampleRequest(SampleQuantity.Depth, Polyline: Line, SpacingMeters: 500));

        var profile = Assert.IsType<SampleProfileResult>(output.Profile);
        Assert.Equal("depth", profile.Quantity);
        Assert.Equal(4, profile.Samples.Count); // 1.1 km in three 371 m stretches
        Assert.Equal([0, null, null, 1], profile.Samples.Select(s => s.VertexIndex));
        Assert.All(profile.Samples, s => Assert.Equal(0, s.LegIndex));
        Assert.Null(profile.Shoalest);
    }

    [Fact]
    public async Task Weather_with_nothing_loaded_covers_nothing()
    {
        Assert.IsType<NoDatasetCoversPoint>(await Err(new SampleRequest(SampleQuantity.Weather, Latitude: 0, Longitude: 0)));
    }

    [Theory]
    [InlineData("depth", SampleQuantity.Depth)]
    [InlineData("water_level", SampleQuantity.WaterLevel)]
    [InlineData("Water-Level", SampleQuantity.WaterLevel)]
    [InlineData("current", SampleQuantity.Current)]
    [InlineData("weather", SampleQuantity.Weather)]
    public void Quantities_read_in_snake_case(string text, SampleQuantity expected)
    {
        Assert.Equal(expected, SampleTool.ParseQuantity(text));
        Assert.Null(SampleTool.ParseQuantity(" "));
        Assert.Throws<ArgumentException>(() => SampleTool.ParseQuantity("salinity"));
    }
}
