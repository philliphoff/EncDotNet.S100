using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class QueryFeaturesAttributeFilterTests
{
    private static S124Feature Feature(
        string id,
        double lat,
        double lon,
        IDictionary<string, string>? attributes = null,
        IEnumerable<S124ComplexAttribute>? complex = null)
    {
        return new S124Feature
        {
            Id = id,
            FeatureType = "NavwarnPart",
            GeometryType = GmlGeometryType.Point,
            Points = ImmutableArray.Create((lat, lon)),
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = (attributes ?? new Dictionary<string, string>()).ToImmutableDictionary(),
            ComplexAttributes = (complex ?? Array.Empty<S124ComplexAttribute>()).ToImmutableArray(),
        };
    }

    private static (QueryFeaturesTool tool, GeoQuery query) Make(params S124Feature[] features)
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124(
            "a", S124Synth.Dataset(features), bounds: LoadedDatasetFactory.Box(-90, -180, 90, 180)));
        return (new QueryFeaturesTool(catalog), new GeoQuery.Box(new GeoBoundingBox(-90, -180, 90, 180)));
    }

    private static AttributeFilter Filter(params (string Code, string? Value)[] predicates)
        => new(predicates.Select(p => new AttributePredicate(p.Code, p.Value)).ToImmutableArray());

    [Fact]
    public async Task Null_filter_matches_every_feature()
    {
        var (tool, query) = Make(
            Feature("a", 1, 1, new Dictionary<string, string> { ["status"] = "1" }),
            Feature("b", 2, 2));

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(query, Attributes: null));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.TotalCount);
    }

    [Fact]
    public async Task Value_predicate_matches_case_insensitively()
    {
        var (tool, query) = Make(
            Feature("a", 1, 1, new Dictionary<string, string> { ["categoryOfRestrictedArea"] = "Anchoring" }),
            Feature("b", 2, 2, new Dictionary<string, string> { ["categoryOfRestrictedArea"] = "Fishing" }));

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            query, Attributes: Filter(("categoryOfRestrictedArea", "anchoring"))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal("a", value.Features[0].FeatureId);
    }

    [Fact]
    public async Task Presence_predicate_matches_any_value()
    {
        var (tool, query) = Make(
            Feature("a", 1, 1, new Dictionary<string, string> { ["restriction"] = "7" }),
            Feature("b", 2, 2, new Dictionary<string, string> { ["status"] = "1" }));

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            query, Attributes: Filter(("restriction", null))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal("a", value.Features[0].FeatureId);
    }

    [Fact]
    public async Task Multiple_predicates_are_combined_with_and()
    {
        var (tool, query) = Make(
            Feature("both", 1, 1, new Dictionary<string, string> { ["status"] = "1", ["restriction"] = "7" }),
            Feature("onlyStatus", 2, 2, new Dictionary<string, string> { ["status"] = "1" }));

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            query, Attributes: Filter(("status", "1"), ("restriction", "7"))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal("both", value.Features[0].FeatureId);
    }

    [Fact]
    public async Task Complex_sub_attributes_are_searched()
    {
        var complex = new S124ComplexAttribute
        {
            Code = "fixedDateRange",
            SubAttributes = new Dictionary<string, string> { ["dateStart"] = "2024-01-01" }.ToImmutableDictionary(),
        };
        var (tool, query) = Make(
            Feature("a", 1, 1, complex: new[] { complex }),
            Feature("b", 2, 2));

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            query, Attributes: Filter(("dateStart", "2024-01-01"))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal("a", value.Features[0].FeatureId);
    }

    [Fact]
    public async Task Non_matching_value_excludes_the_feature()
    {
        var (tool, query) = Make(
            Feature("a", 1, 1, new Dictionary<string, string> { ["status"] = "1" }));

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            query, Attributes: Filter(("status", "2"))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Features);
    }
}
