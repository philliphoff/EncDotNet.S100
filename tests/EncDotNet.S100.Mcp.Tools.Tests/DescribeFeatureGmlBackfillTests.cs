using System.Collections.Immutable;
using System.Text.Json;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S131;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class DescribeFeatureGmlBackfillTests
{
    [Fact]
    public async Task S122_feature_is_described_via_generic_GmlFeatureDescriber()
    {
        var feature = new S122Feature
        {
            Id = "mpa-1",
            FeatureType = "MarineProtectedArea",
            GeometryType = GmlGeometryType.Surface,
            Points = default,
            Curves = default,
            ExteriorRing = ImmutableArray.Create<(double, double)>((0, 0), (0, 1), (1, 1), (0, 0)),
            InteriorRings = default,
            Attributes = ImmutableDictionary<string, string>.Empty
                .Add("categoryOfMarineProtectedArea", "nature reserve"),
            ComplexAttributes = ImmutableArray<S122ComplexAttribute>.Empty,
        };
        var model = new S122Dataset
        {
            Features = ImmutableArray.Create(feature),
            InformationTypes = ImmutableArray<S122InformationType>.Empty,
        };
        var catalog = new FakeDatasetCatalog();
        catalog.Add(new LoadedDataset(
            new DatasetId("mpa"),
            LoadedDatasetFactory.S122Spec,
            LoadedDatasetFactory.Box(0, 0, 1, 1),
            null,
            new S122DatasetData(model)));

        var tool = new DescribeFeatureTool(catalog);
        var result = await tool.InvokeAsync(new DescribeFeatureRequest(
            new DatasetId("mpa"),
            "mpa-1"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("MarineProtectedArea", value.FeatureTypeName);
        Assert.Empty(value.References);
        var attrs = value.Attributes;
        Assert.Equal("mpa-1", attrs.GetProperty("id").GetString());
        Assert.Equal("Surface", attrs.GetProperty("geometryType").GetString());
        Assert.Equal(
            "nature reserve",
            attrs.GetProperty("attributes").GetProperty("categoryOfMarineProtectedArea").GetString());
    }

    [Fact]
    public async Task S131_geometryless_authority_feature_round_trips_attributes()
    {
        var authority = new S131Feature
        {
            Id = "auth-1",
            FeatureType = "Authority",
            GeometryType = GmlGeometryType.None,
            Points = default,
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = ImmutableDictionary<string, string>.Empty
                .Add("authorityName", "Port of Test"),
            ComplexAttributes = ImmutableArray<S131ComplexAttribute>.Empty,
            References = ImmutableArray<S131Reference>.Empty,
        };
        var model = new S131Dataset
        {
            Features = ImmutableArray.Create(authority),
            InformationTypes = ImmutableArray<S131InformationType>.Empty,
        };
        var catalog = new FakeDatasetCatalog();
        catalog.Add(new LoadedDataset(
            new DatasetId("harbour"),
            LoadedDatasetFactory.S131Spec,
            LoadedDatasetFactory.Box(0, 0, 1, 1),
            null,
            new S131DatasetData(model)));

        var tool = new DescribeFeatureTool(catalog);
        var result = await tool.InvokeAsync(new DescribeFeatureRequest(
            new DatasetId("harbour"),
            "auth-1"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("Authority", value.FeatureTypeName);
        Assert.Equal(
            "Port of Test",
            value.Attributes.GetProperty("attributes").GetProperty("authorityName").GetString());
    }

    [Fact]
    public async Task Missing_feature_id_returns_FeatureNotFound_for_generic_spec()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S122("mpa", bounds: LoadedDatasetFactory.Box()));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(new DescribeFeatureRequest(
            new DatasetId("mpa"),
            "does-not-exist"));

        Assert.False(result.TryGetValue(out _));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<FeatureNotFound>(err);
    }
}
