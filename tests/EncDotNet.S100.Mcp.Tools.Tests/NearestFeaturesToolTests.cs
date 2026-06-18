using System.Collections.Immutable;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class NearestFeaturesToolTests
{
    private static S124Feature Point(string id, double lat, double lon, string type = "Light") => new()
    {
        Id = id,
        FeatureType = type,
        GeometryType = S100GeometryType.Point,
        Points = ImmutableArray.Create((lat, lon)),
        Attributes = ImmutableDictionary<string, string>.Empty,
        ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
    };

    private static S124Feature Square(string id, double half)
    {
        var ring = ImmutableArray.Create<(double, double)>(
            (-half, -half), (-half, half), (half, half), (half, -half), (-half, -half));
        return new S124Feature
        {
            Id = id,
            FeatureType = "RestrictedArea",
            GeometryType = S100GeometryType.Surface,
            ExteriorRing = ring,
            InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
        };
    }

    [Fact]
    public async Task Ranks_features_nearest_first()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(
            Point("far", 0.0, 0.5),
            Point("near", 0.0, 0.1),
            Point("mid", 0.0, 0.3))));
        var tool = new NearestFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new NearestFeaturesRequest(0.0, 0.0));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.TotalMatched);
        Assert.False(value.Truncated);
        Assert.Equal(new[] { "near", "mid", "far" }, value.Features.Select(f => f.FeatureId));
        Assert.All(value.Features, f => Assert.Equal("near", f.Containment));
        Assert.True(value.Features[0].DistanceMeters < value.Features[1].DistanceMeters);
        Assert.NotNull(value.Features[0].BearingDegrees);
    }

    [Fact]
    public async Task A_point_inside_an_area_reports_zero_distance_and_inside()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(
            Square("area", 0.5),
            Point("pt", 0.0, 0.2))));
        var tool = new NearestFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new NearestFeaturesRequest(0.0, 0.0));

        Assert.True(result.TryGetValue(out var value));
        var area = value.Features.Single(f => f.FeatureId == "area");
        Assert.Equal("inside", area.Containment);
        Assert.Equal(0.0, area.DistanceMeters);
        Assert.Null(area.BearingDegrees);
        Assert.Equal("surface", area.Geometry);
    }

    [Fact]
    public async Task Max_distance_excludes_far_features()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(
            Point("near", 0.0, 0.001),   // ~111 m
            Point("far", 0.0, 1.0))));    // ~111 km
        var tool = new NearestFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new NearestFeaturesRequest(0.0, 0.0, MaxDistanceMeters: 1000.0));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(1, value.TotalMatched);
        Assert.Equal("near", Assert.Single(value.Features).FeatureId);
    }

    [Fact]
    public async Task Feature_type_filter_is_applied()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(
            Point("a", 0.0, 0.1, type: "Light"),
            Point("b", 0.0, 0.2, type: "Buoy"))));
        var tool = new NearestFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new NearestFeaturesRequest(0.0, 0.0, FeatureType: "Buoy"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("b", Assert.Single(value.Features).FeatureId);
    }

    [Fact]
    public async Task Limit_truncates_results()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(
            Point("a", 0.0, 0.1),
            Point("b", 0.0, 0.2),
            Point("c", 0.0, 0.3))));
        var tool = new NearestFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new NearestFeaturesRequest(0.0, 0.0, Limit: 2));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.TotalMatched);
        Assert.True(value.Truncated);
        Assert.Equal(2, value.Features.Length);
    }

    [Fact]
    public async Task Dataset_filter_restricts_search()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a", S124Synth.Dataset(Point("ina", 0.0, 0.1))));
        catalog.Add(LoadedDatasetFactory.S124("b", S124Synth.Dataset(Point("inb", 0.0, 0.05))));
        var tool = new NearestFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new NearestFeaturesRequest(0.0, 0.0, Dataset: new DatasetId("a")));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("ina", Assert.Single(value.Features).FeatureId);
    }

    [Theory]
    [InlineData(91.0, 0.0, "latitude")]
    [InlineData(0.0, 181.0, "longitude")]
    public async Task Rejects_out_of_range_coordinates(double lat, double lon, string parameter)
    {
        var tool = new NearestFeaturesTool(new FakeDatasetCatalog());

        var result = await tool.InvokeAsync(new NearestFeaturesRequest(lat, lon));

        var err = Assert.IsType<ToolResult<NearestFeaturesResult>.ErrResult>(result);
        Assert.Equal("invalid_argument", err.Error.Code);
        Assert.Contains(parameter, err.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_negative_max_distance()
    {
        var tool = new NearestFeaturesTool(new FakeDatasetCatalog());

        var result = await tool.InvokeAsync(new NearestFeaturesRequest(0.0, 0.0, MaxDistanceMeters: -1.0));

        var err = Assert.IsType<ToolResult<NearestFeaturesResult>.ErrResult>(result);
        Assert.Equal("invalid_argument", err.Error.Code);
    }
}
