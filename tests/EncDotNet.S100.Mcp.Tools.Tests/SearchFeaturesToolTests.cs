using System.Collections.ObjectModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Features;
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
            Points = [new GeoPosition(lat, lon)],
            Curves = [],
            ExteriorRing = [],
            InteriorRings = [],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [new S122ComplexAttribute
            {
                Code = "featureName",
                SubAttributes = new Dictionary<string, string> { ["name"] = name },
            }],
        };

    private static LoadedDataset S122With(string id, BoundingBox? bounds, params S122Feature[] features)
    {
        var model = new S122Dataset
        {
            Features = features.ToArray(),
            InformationTypes = [],
        };
        return new LoadedDataset(
            new DatasetId(id),
            LoadedDatasetFactory.S122Spec,
            bounds ?? LoadedDatasetFactory.Box(0, 0, 10, 10),
            null,
            new S122DatasetData(model));
    }

    private static LoadedDataset S101WithNamedPoint(string id, uint rcid, string name, double lat, double lon)
        => S101WithNamedPoint(id, rcid, name, lat, lon, LoadedDatasetFactory.Box(-1, -1, 10, 10), compilationScale: null);

    private static LoadedDataset S101WithNamedPoint(
        string id, uint rcid, string name, double lat, double lon, BoundingBox bounds, int? compilationScale)
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
            [feature],
            featureTypes: new Dictionary<ushort, string> { [lightsCode] = "LIGHTS" }.ToDictionary(),
            attributeTypes: new Dictionary<ushort, string> { [ObjnamCode] = "OBJNAM" }.ToDictionary(),
            points: new Dictionary<uint, S101PointRecord> { [rcid] = point }.ToDictionary());
        return LoadedDatasetFactory.S101(id, dataset, bounds, compilationScale);
    }

    // Two copies of one object (as NOAA charts "Friday Harbor Marina Entrance"
    // in each usage band) plus a second object charted only in the coarse cell.
    private static FakeDatasetCatalog OverlappingBands()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S101WithNamedPoint("approach", 1, "Marina Entrance", 2.0, 2.0, LoadedDatasetFactory.Box(0, 0, 10, 10), 80_000));
        catalog.Add(S101WithNamedPoint("harbour", 2, "Marina Entrance", 2.0, 2.0, LoadedDatasetFactory.Box(1, 1, 3, 3), 10_000));
        catalog.Add(S101WithNamedPoint("approach-east", 3, "Marina Entrance", 8.0, 8.0, LoadedDatasetFactory.Box(0, 0, 10, 10), 80_000));
        return catalog;
    }

    [Fact]
    public async Task Overlapping_bands_return_every_copy_by_default()
    {
        var tool = new SearchFeaturesTool(OverlappingBands());

        var result = await tool.InvokeAsync(new SearchFeaturesRequest("marina"), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.TotalCount);
    }

    [Fact]
    public async Task FinestOnly_keeps_one_hit_per_object_from_the_finest_cell_there()
    {
        var tool = new SearchFeaturesTool(OverlappingBands());

        var result = await tool.InvokeAsync(
            new SearchFeaturesRequest("marina", FinestOnly: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.TotalCount);
        Assert.Equal(["harbour", "approach-east"], value.Features.Select(f => f.DatasetId.Value));
    }

    [Fact]
    public async Task FinestOnly_ranks_against_cells_outside_the_dataset_filter()
    {
        var tool = new SearchFeaturesTool(OverlappingBands());

        var result = await tool.InvokeAsync(
            new SearchFeaturesRequest("marina", Dataset: new DatasetId("approach"), FinestOnly: true),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Features);
    }

    [Fact]
    public async Task FinestOnly_keeps_both_copies_from_cells_of_the_same_scale()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S101WithNamedPoint("west", 1, "Marina Entrance", 2.0, 2.0, LoadedDatasetFactory.Box(0, 0, 3, 3), 20_000));
        catalog.Add(S101WithNamedPoint("east", 2, "Marina Entrance", 2.0, 2.0, LoadedDatasetFactory.Box(1, 1, 4, 4), 20_000));
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(
            new SearchFeaturesRequest("marina", FinestOnly: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.TotalCount);
    }

    [Fact]
    public async Task FinestOnly_keeps_hits_outside_chart_cells()
    {
        var catalog = OverlappingBands();
        catalog.Add(S122With("mpa", null, NamedMpa("a", "Marina Entrance Reserve", 2.0, 2.0)));
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(
            new SearchFeaturesRequest("marina", FinestOnly: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Contains(value.Features, f => f.DatasetId.Value == "mpa");
        Assert.Equal(3, value.TotalCount);
    }

    [Fact]
    public async Task Substring_match_is_case_insensitive_by_default()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S122With("mpa", null, NamedMpa("a", "North Channel"), NamedMpa("b", "South Bank")));
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new SearchFeaturesRequest("channel"), TestContext.Current.CancellationToken);

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

        var result = await tool.InvokeAsync(new SearchFeaturesRequest("nab"), TestContext.Current.CancellationToken);

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

        var insensitive = await tool.InvokeAsync(new SearchFeaturesRequest("north"), TestContext.Current.CancellationToken);
        Assert.True(insensitive.TryGetValue(out var iv));
        Assert.Single(iv.Features);

        var sensitive = await tool.InvokeAsync(new SearchFeaturesRequest("north", CaseSensitive: true), TestContext.Current.CancellationToken);
        Assert.True(sensitive.TryGetValue(out var sv));
        Assert.Empty(sv.Features);
    }

    [Fact]
    public async Task Exact_flag_requires_whole_name_equality()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S122With("mpa", null, NamedMpa("a", "North Channel")));
        var tool = new SearchFeaturesTool(catalog);

        var partial = await tool.InvokeAsync(new SearchFeaturesRequest("North", Exact: true), TestContext.Current.CancellationToken);
        Assert.True(partial.TryGetValue(out var pv));
        Assert.Empty(pv.Features);

        var whole = await tool.InvokeAsync(new SearchFeaturesRequest("North Channel", Exact: true), TestContext.Current.CancellationToken);
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
            "Shared", Spec: new SpecRef("S-101", default)), TestContext.Current.CancellationToken);

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
            "Shared", Dataset: new DatasetId("two")), TestContext.Current.CancellationToken);

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
            Query: new GeoQuery.Box(new GeoBoundingBox(0, 0, 6, 6))), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var match = Assert.Single(value.Features);
        Assert.Equal("inside", match.FeatureId);
    }

    [Fact]
    public async Task Blank_text_returns_invalid_argument()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new SearchFeaturesRequest("   "), TestContext.Current.CancellationToken);

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

        var first = await tool.InvokeAsync(new SearchFeaturesRequest("Channel", Page: 0, PageSize: 2), TestContext.Current.CancellationToken);
        Assert.True(first.TryGetValue(out var fv));
        Assert.Equal(3, fv.TotalCount);
        Assert.Equal(2, fv.Features.Count);
        Assert.True(fv.HasMore);

        var second = await tool.InvokeAsync(new SearchFeaturesRequest("Channel", Page: 1, PageSize: 2), TestContext.Current.CancellationToken);
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
            Points = [new GeoPosition(5.0, 5.0)],
            Attributes = new Dictionary<string, string> { ["objectName"] = "Channel Marker" },
            ComplexAttributes = [new S122ComplexAttribute
            {
                Code = "featureName",
                SubAttributes = new Dictionary<string, string> { ["name"] = "Channel Marker" },
            }],
        };
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S122With("mpa", null, feature));
        var tool = new SearchFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new SearchFeaturesRequest("Channel"), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal(1, value.TotalCount);
    }
}
