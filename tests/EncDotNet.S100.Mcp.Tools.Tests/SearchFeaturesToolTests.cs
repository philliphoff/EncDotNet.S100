using System.Collections.Immutable;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class SearchFeaturesToolTests
{
    private const ushort ObjnamCode = 116;

    private static S122Feature NamedMpa(string id, string name, double lat = 5, double lon = 5)
        => new()
        {
            Id = id,
            FeatureType = "MarineProtectedArea",
            GeometryType = S100GeometryType.Point,
            Points = ImmutableArray.Create((lat, lon)),
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray.Create(new S122ComplexAttribute
            {
                Code = "featureName",
                SubAttributes = ImmutableDictionary<string, string>.Empty.Add("name", name),
            }),
        };

    private static LoadedDataset S122With(string id, BoundingBox? bounds, params S122Feature[] features)
    {
        var model = new S122Dataset
        {
            Features = features.ToImmutableArray(),
            InformationTypes = ImmutableArray<S122InformationType>.Empty,
        };
        return new LoadedDataset(
            new DatasetId(id),
            LoadedDatasetFactory.S122Spec,
            bounds ?? LoadedDatasetFactory.Box(0, 0, 10, 10),
            null,
            new S122DatasetData(model));
    }

    private static LoadedDataset S101WithNamedPoint(string id, uint rcid, string name, double lat, double lon)
    {
        const ushort lightsCode = 75;
        const int factor = 10_000_000;
        var feature = S101Synth.Feature(rcid, lightsCode, attributes: new[] { (ObjnamCode, name) });
        var point = new S101PointRecord
        {
            RecordId = rcid,
            Y = (int)Math.Round(lat * factor),
            X = (int)Math.Round(lon * factor),
        };
        var dataset = S101Synth.Dataset(
            id,
            ImmutableArray.Create(feature),
            featureTypes: new Dictionary<ushort, string> { [lightsCode] = "LIGHTS" }.ToImmutableDictionary(),
            attributeTypes: new Dictionary<ushort, string> { [ObjnamCode] = "OBJNAM" }.ToImmutableDictionary(),
            points: new Dictionary<uint, S101PointRecord> { [rcid] = point }.ToImmutableDictionary());
        return LoadedDatasetFactory.S101(id, dataset, LoadedDatasetFactory.Box(-1, -1, 10, 10));
    }

    [Fact]
    public async Task Substring_match_is_case_insensitive_by_default()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S122With("mpa", null, NamedMpa("a", "North Channel"), NamedMpa("b", "South Bank")));
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new SearchFeaturesRequest("channel"));

        Assert.True(result.TryGetValue(out var value));
        var match = Assert.Single(value.Features);
        Assert.Equal("a", match.FeatureId);
        Assert.Equal("North Channel", match.MatchedName);
        Assert.Equal("featureName.name", match.MatchedAttribute);
    }

    [Fact]
    public async Task Finds_s101_features_by_OBJNAM()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S101WithNamedPoint("enc", 100, "Nab Tower", 1.0, 1.0));
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new SearchFeaturesRequest("nab"));

        Assert.True(result.TryGetValue(out var value));
        var match = Assert.Single(value.Features);
        Assert.Equal("100", match.FeatureId);
        Assert.Equal("LIGHTS", match.FeatureType);
        Assert.Equal("Nab Tower", match.MatchedName);
        Assert.Equal("OBJNAM", match.MatchedAttribute);
        Assert.NotNull(match.Bounds);
    }

    [Fact]
    public async Task Case_sensitive_flag_is_honoured()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S122With("mpa", null, NamedMpa("a", "North Channel")));
        var tool = new SearchFeaturesTool(catalog);

        var insensitive = await tool.InvokeAsync(new SearchFeaturesRequest("north"));
        Assert.True(insensitive.TryGetValue(out var iv));
        Assert.Single(iv.Features);

        var sensitive = await tool.InvokeAsync(new SearchFeaturesRequest("north", CaseSensitive: true));
        Assert.True(sensitive.TryGetValue(out var sv));
        Assert.Empty(sv.Features);
    }

    [Fact]
    public async Task Exact_flag_requires_whole_name_equality()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S122With("mpa", null, NamedMpa("a", "North Channel")));
        var tool = new SearchFeaturesTool(catalog);

        var partial = await tool.InvokeAsync(new SearchFeaturesRequest("North", Exact: true));
        Assert.True(partial.TryGetValue(out var pv));
        Assert.Empty(pv.Features);

        var whole = await tool.InvokeAsync(new SearchFeaturesRequest("North Channel", Exact: true));
        Assert.True(whole.TryGetValue(out var wv));
        Assert.Single(wv.Features);
    }

    [Fact]
    public async Task Spec_filter_scopes_the_search()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S122With("mpa", null, NamedMpa("a", "Shared Name")));
        catalog.Add(S101WithNamedPoint("enc", 100, "Shared Name", 1.0, 1.0));
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new SearchFeaturesRequest(
            "Shared", Spec: new SpecRef("S-101", default)));

        Assert.True(result.TryGetValue(out var value));
        var match = Assert.Single(value.Features);
        Assert.Equal("S-101", match.Spec.Name);
    }

    [Fact]
    public async Task Dataset_filter_scopes_the_search()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S122With("one", null, NamedMpa("a", "Shared Name")));
        catalog.Add(S122With("two", null, NamedMpa("b", "Shared Name")));
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new SearchFeaturesRequest(
            "Shared", Dataset: new DatasetId("two")));

        Assert.True(result.TryGetValue(out var value));
        var match = Assert.Single(value.Features);
        Assert.Equal("b", match.FeatureId);
    }

    [Fact]
    public async Task Spatial_query_filters_by_geometry()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S122With("mpa", LoadedDatasetFactory.Box(0, 0, 10, 10),
            NamedMpa("inside", "Channel A", 5, 5),
            NamedMpa("outside", "Channel B", 9, 9)));
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new SearchFeaturesRequest(
            "Channel",
            Query: new GeoQuery.Box(new GeoBoundingBox(0, 0, 6, 6))));

        Assert.True(result.TryGetValue(out var value));
        var match = Assert.Single(value.Features);
        Assert.Equal("inside", match.FeatureId);
    }

    [Fact]
    public async Task Blank_text_returns_invalid_argument()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new SearchFeaturesRequest("   "));

        var err = Assert.IsType<ToolResult<SearchFeaturesResult>.ErrResult>(result);
        Assert.Equal("invalid_argument", err.Error.Code);
    }

    [Fact]
    public async Task Pages_through_results()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S122With("mpa", null,
            NamedMpa("a", "Channel 1"),
            NamedMpa("b", "Channel 2"),
            NamedMpa("c", "Channel 3")));
        var tool = new SearchFeaturesTool(catalog);

        var first = await tool.InvokeAsync(new SearchFeaturesRequest("Channel", Page: 0, PageSize: 2));
        Assert.True(first.TryGetValue(out var fv));
        Assert.Equal(3, fv.TotalCount);
        Assert.Equal(2, fv.Features.Length);
        Assert.True(fv.HasMore);

        var second = await tool.InvokeAsync(new SearchFeaturesRequest("Channel", Page: 1, PageSize: 2));
        Assert.True(second.TryGetValue(out var sv));
        Assert.Single(sv.Features);
        Assert.False(sv.HasMore);
    }

    [Fact]
    public async Task Feature_with_multiple_matching_names_is_returned_once()
    {
        var feature = new S122Feature
        {
            Id = "a",
            FeatureType = "MarineProtectedArea",
            GeometryType = S100GeometryType.Point,
            Points = ImmutableArray.Create((5.0, 5.0)),
            Attributes = ImmutableDictionary<string, string>.Empty.Add("objectName", "Channel Marker"),
            ComplexAttributes = ImmutableArray.Create(new S122ComplexAttribute
            {
                Code = "featureName",
                SubAttributes = ImmutableDictionary<string, string>.Empty.Add("name", "Channel Marker"),
            }),
        };
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S122With("mpa", null, feature));
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new SearchFeaturesRequest("Channel"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal(1, value.TotalCount);
    }
}
