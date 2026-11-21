using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Value hydration on the read tools (#556): identify_features,
/// query_features and nearest_features report a feature's depths as numbers,
/// and its attributes on request, over a synthetic S-101 cell with a 10 m
/// depth area west of 0.025° and a 1 m one east of it.
/// </summary>
public sealed class FeatureValuesTests
{
    [Fact]
    public async Task Identify_reports_the_depth_area_range_as_numbers()
    {
        var result = await new IdentifyFeaturesTool(Catalog()).InvokeAsync(new IdentifyFeaturesRequest(0.02, 0.01), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var area = Assert.Single(value!.Features, f => f.FeatureType == "DepthArea");
        Assert.Equal(10, area.Depth!.DepthRangeMinimumMeters);
        Assert.Null(area.Attributes);
    }

    [Fact]
    public async Task Identify_lists_the_attributes_on_request()
    {
        var result = await new IdentifyFeaturesTool(Catalog()).InvokeAsync(new IdentifyFeaturesRequest(0.02, 0.01, IncludeAttributes: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var area = Assert.Single(value!.Features, f => f.FeatureType == "DepthArea");
        var drval1 = Assert.Single(area.Attributes!, a => a.Code == "depthRangeMinimumValue");
        Assert.Equal("10", drval1.RawValue);
    }

    [Fact]
    public async Task Query_hydrates_the_page_it_returns()
    {
        var query = new GeoQuery.Box(new GeoBoundingBox(0.01, 0.01, 0.03, 0.04));

        var result = await new QueryFeaturesTool(Catalog()).InvokeAsync(new QueryFeaturesRequest(query, FeatureType: "DepthArea", IncludeAttributes: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal([1.0, 10.0], value!.Features.Select(f => f.Depth!.DepthRangeMinimumMeters!.Value).Order());
        Assert.All(value.Features, f => Assert.Contains(f.Attributes!, a => a.Code == "depthRangeMinimumValue"));
    }

    [Fact]
    public async Task Nearest_reports_the_depth_of_the_area_the_point_is_in()
    {
        var result = await new NearestFeaturesTool(Catalog()).InvokeAsync(new NearestFeaturesRequest(0.02, 0.03, FeatureType: "DepthArea", Limit: 1), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var nearest = Assert.Single(value!.Features);
        Assert.Equal("inside", nearest.Containment);
        Assert.Equal(1, nearest.Depth!.DepthRangeMinimumMeters);
    }

    [Fact]
    public void A_wrecks_sounding_and_water_level_effect_are_read_from_either_encoding()
    {
        Feature Wreck(Dictionary<string, object?> attributes) => new()
        {
            FeatureType = "Wreck",
            Id = 1,
            GeometryType = GeometryType.Point,
            Coordinates = [new GeoPosition(0, 0)],
            Attributes = attributes,
        };

        var s101 = FeatureDepthValues.From(Wreck(new() { ["valueOfSounding"] = "2.4", ["waterLevelEffect"] = "3" }))!;
        var s57 = FeatureDepthValues.From(Wreck(new() { ["VALSOU"] = 2.4, ["WATLEV"] = 3 }))!;

        Assert.Equal(new FeatureDepthValues(null, null, 2.4, 3), s101);
        Assert.Equal(s101, s57);
        Assert.Null(FeatureDepthValues.From(Wreck(new() { ["objectName"] = "Wreck" })));
    }

    private static FakeDatasetCatalog Catalog()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101(
            "101VAL",
            S101AreaSynth.Cell(
            [
                S101AreaSynth.DataCoverage(22_000, S101AreaSynth.Box(0, 0, 0.05, 0.05)),
                S101AreaSynth.DepthArea(10, S101AreaSynth.Box(0, 0, 0.05, 0.025)),
                S101AreaSynth.DepthArea(1, S101AreaSynth.Box(0, 0.025, 0.05, 0.05)),
            ]),
            LoadedDatasetFactory.Box(0, 0, 0.05, 0.05)));
        return catalog;
    }
}
