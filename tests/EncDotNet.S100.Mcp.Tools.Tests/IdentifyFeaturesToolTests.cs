using System.Collections.ObjectModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class IdentifyFeaturesToolTests
{
    private static S124Feature Point(string id, double lat, double lon, string type = "Light") => new()
    {
        Id = id,
        FeatureType = type,
        GeometryType = S100GeometryType.Point,
        Points = [new GeoPosition(lat, lon)],
        Attributes = ReadOnlyDictionary<string, string>.Empty,
        ComplexAttributes = [],
    };

    private static S124Feature Curve(string id, params GeoPosition[] vertices) => new()
    {
        Id = id,
        FeatureType = "Fairway",
        GeometryType = S100GeometryType.Curve,
        Curves = [vertices.ToArray()],
        Attributes = ReadOnlyDictionary<string, string>.Empty,
        ComplexAttributes = [],
    };

    private static S124Feature Square(string id, double half, params IReadOnlyList<GeoPosition>[] holes)
    {
        IReadOnlyList<GeoPosition> ring = [
            new GeoPosition(-half, -half), new GeoPosition(-half, half), new GeoPosition(half, half), new GeoPosition(half, -half), new GeoPosition(-half, -half)];
        return new S124Feature
        {
            Id = id,
            FeatureType = "RestrictedArea",
            GeometryType = S100GeometryType.Surface,
            ExteriorRing = ring,
            InteriorRings = holes.ToArray(),
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
        };
    }

    private static IReadOnlyList<GeoPosition> Hole(double half) =>
        [
            new GeoPosition(-half, -half), new GeoPosition(-half, half), new GeoPosition(half, half), new GeoPosition(half, -half), new GeoPosition(-half, -half)];

    [Fact]
    public async Task Ranks_point_before_curve_before_smaller_then_larger_area()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(
            Square("big", 0.9),
            Square("small", 0.1),
            Curve("curve", new GeoPosition(0.0, 0.0), new GeoPosition(0.0, 0.5)),
            Point("pt", 0.0, 0.0))));
        var tool = new IdentifyFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new IdentifyFeaturesRequest(0.0, 0.0));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(4, value.TotalMatched);
        Assert.False(value.Truncated);

        Assert.Equal("point", value.Features[0].Geometry);
        Assert.Equal("pt", value.Features[0].FeatureId);
        Assert.Equal("near", value.Features[0].Containment);

        Assert.Equal("curve", value.Features[1].Geometry);
        Assert.Equal("curve", value.Features[1].FeatureId);

        Assert.Equal("surface", value.Features[2].Geometry);
        Assert.Equal("small", value.Features[2].FeatureId);
        Assert.Equal("inside", value.Features[2].Containment);
        Assert.Equal(0.0, value.Features[2].DistanceMeters);

        Assert.Equal("big", value.Features[3].FeatureId);
    }

    [Fact]
    public async Task A_pick_inside_an_interior_hole_does_not_match_the_area()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(
            Square("donut", 0.5, Hole(0.2)))));
        var tool = new IdentifyFeaturesTool(catalog);

        var inHole = await tool.InvokeAsync(new IdentifyFeaturesRequest(0.0, 0.0));
        Assert.True(inHole.TryGetValue(out var holeValue));
        Assert.Empty(holeValue.Features);

        var inRing = await tool.InvokeAsync(new IdentifyFeaturesRequest(0.35, 0.0));
        Assert.True(inRing.TryGetValue(out var ringValue));
        var match = Assert.Single(ringValue.Features);
        Assert.Equal("donut", match.FeatureId);
        Assert.Equal("inside", match.Containment);
    }

    [Fact]
    public async Task Radius_governs_point_matches()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(
            Point("near", 0.0003, 0.0))));
        var tool = new IdentifyFeaturesTool(catalog);

        var wide = await tool.InvokeAsync(new IdentifyFeaturesRequest(0.0, 0.0, RadiusMeters: 50));
        Assert.True(wide.TryGetValue(out var wideValue));
        Assert.Single(wideValue.Features);
        Assert.True(wideValue.Features[0].DistanceMeters > 0);

        var tight = await tool.InvokeAsync(new IdentifyFeaturesRequest(0.0, 0.0, RadiusMeters: 10));
        Assert.True(tight.TryGetValue(out var tightValue));
        Assert.Empty(tightValue.Features);
    }

    [Fact]
    public async Task Radius_filters_curve_whose_bbox_contains_pick_but_vertices_are_far()
    {
        // A curve whose bounding box spans the pick point, but whose nearest
        // vertex is ~55 km away — far beyond the default radius. The coarse
        // bbox pre-filter admits it, so the precise radius test must reject it.
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(
            Curve("far", new GeoPosition(0.0, -0.5), new GeoPosition(0.0, 0.5)))));
        var tool = new IdentifyFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new IdentifyFeaturesRequest(0.0, 0.0, RadiusMeters: 50));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Features);
    }

    [Fact]
    public async Task Non_finite_radius_returns_invalid_argument()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(Point("pt", 0.0, 0.0))));
        var tool = new IdentifyFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new IdentifyFeaturesRequest(0.0, 0.0, RadiusMeters: double.NaN));

        Assert.False(result.TryGetValue(out _));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Spec_filter_restricts_results()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(Point("pt", 0.0, 0.0))));
        catalog.Add(LoadedDatasetFactory.S101("enc", S101Synth.DatasetWithPointFeatures(
            "enc",
            new (uint, ushort, double, double)[] { (100u, 75, 0.0, 0.0) },
            new Dictionary<ushort, string> { [75] = "LIGHTS" }.ToDictionary())));
        var tool = new IdentifyFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new IdentifyFeaturesRequest(
            0.0, 0.0, Spec: new SpecRef("S-124", default)));

        Assert.True(result.TryGetValue(out var value));
        Assert.All(value.Features, f => Assert.Equal("S-124", f.Spec.Name));
        Assert.Single(value.Features);
    }

    [Fact]
    public async Task Picks_features_across_specs_including_s101()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(Square("area", 0.5))));
        catalog.Add(LoadedDatasetFactory.S101("enc", S101Synth.DatasetWithPointFeatures(
            "enc",
            new (uint, ushort, double, double)[] { (100u, 75, 0.0, 0.0) },
            new Dictionary<ushort, string> { [75] = "LIGHTS" }.ToDictionary())));
        var tool = new IdentifyFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new IdentifyFeaturesRequest(0.0, 0.0));

        Assert.True(result.TryGetValue(out var value));
        Assert.Contains(value.Features, f => f.Spec.Name == "S-101" && f.Geometry == "point");
        Assert.Contains(value.Features, f => f.Spec.Name == "S-124" && f.Geometry == "surface");
        // Point (S-101 light) ranks above the area.
        Assert.Equal("point", value.Features[0].Geometry);
    }

    [Fact]
    public async Task MaxResults_truncates_and_flags()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(
            Square("big", 0.9),
            Square("small", 0.1),
            Point("pt", 0.0, 0.0))));
        var tool = new IdentifyFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new IdentifyFeaturesRequest(0.0, 0.0, MaxResults: 1));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal(3, value.TotalMatched);
        Assert.True(value.Truncated);
        Assert.Equal("pt", value.Features[0].FeatureId);
    }

    [Fact]
    public async Task Coverage_products_yield_no_matches()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S102("depth"));
        var tool = new IdentifyFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new IdentifyFeaturesRequest(0.0, 0.0));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Features);
        Assert.Equal(0, value.TotalMatched);
    }

    [Fact]
    public async Task Out_of_range_latitude_is_rejected()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new IdentifyFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new IdentifyFeaturesRequest(120.0, 0.0));

        Assert.False(result.TryGetValue(out _));
    }
}
