using System.Collections.Immutable;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class QueryFeaturesToolAttributeTests
{
    private static S122Feature Feature(string id, string featureType, params (string Key, string Value)[] attributes)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (key, value) in attributes)
        {
            builder[key] = value;
        }

        return new S122Feature
        {
            Id = id,
            FeatureType = featureType,
            GeometryType = S100GeometryType.Point,
            Points = ImmutableArray.Create((5.0, 5.0)),
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = builder.ToImmutable(),
            ComplexAttributes = ImmutableArray<S122ComplexAttribute>.Empty,
        };
    }

    private static FakeDatasetCatalog BuildCatalog(params S122Feature[] features)
    {
        var dataset = new S122Dataset
        {
            Features = features.ToImmutableArray(),
            InformationTypes = ImmutableArray<S122InformationType>.Empty,
        };
        var catalog = new FakeDatasetCatalog();
        catalog.Add(new LoadedDataset(
            new DatasetId("mpa"),
            LoadedDatasetFactory.S122Spec,
            LoadedDatasetFactory.Box(0, 0, 10, 10),
            null,
            new S122DatasetData(dataset)));
        return catalog;
    }

    private static GeoQuery Box => new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10));

    [Fact]
    public async Task Equality_predicate_filters_matched_features()
    {
        var catalog = BuildCatalog(
            Feature("a", "MarineProtectedArea", ("categoryOfMarineProtectedArea", "1")),
            Feature("b", "MarineProtectedArea", ("categoryOfMarineProtectedArea", "2")));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            Box,
            Attributes: ImmutableArray.Create(
                new AttributePredicate("categoryOfMarineProtectedArea", AttributeOperator.Eq, "1"))));

        Assert.True(result.TryGetValue(out var value));
        var match = Assert.Single(value.Features);
        Assert.Equal("a", match.FeatureId);
        Assert.Equal(1, value.TotalCount);
    }

    [Fact]
    public async Task Numeric_predicate_filters_matched_features()
    {
        var catalog = BuildCatalog(
            Feature("shallow", "MarineProtectedArea", ("valueOfDepth", "5")),
            Feature("deep", "MarineProtectedArea", ("valueOfDepth", "20")));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            Box,
            Attributes: ImmutableArray.Create(
                new AttributePredicate("valueOfDepth", AttributeOperator.Ge, "10"))));

        Assert.True(result.TryGetValue(out var value));
        var match = Assert.Single(value.Features);
        Assert.Equal("deep", match.FeatureId);
    }

    [Fact]
    public async Task Type_breakdown_reflects_attribute_filtered_set()
    {
        var catalog = BuildCatalog(
            Feature("a", "MarineProtectedArea", ("restriction", "1")),
            Feature("b", "MarineProtectedArea"),
            Feature("c", "RestrictedAreaRegulatory", ("restriction", "1")));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            Box,
            Attributes: ImmutableArray.Create(
                new AttributePredicate("restriction", AttributeOperator.Exists, null))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.TotalCount);
        Assert.Equal(2, value.TypeBreakdown.Length);
        Assert.Contains(value.TypeBreakdown, t => t.FeatureType == "MarineProtectedArea" && t.Count == 1);
        Assert.Contains(value.TypeBreakdown, t => t.FeatureType == "RestrictedAreaRegulatory" && t.Count == 1);
    }

    [Fact]
    public async Task No_predicates_returns_all_features()
    {
        var catalog = BuildCatalog(
            Feature("a", "MarineProtectedArea", ("x", "1")),
            Feature("b", "MarineProtectedArea"));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(Box));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.TotalCount);
    }
}
