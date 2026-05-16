using System.Collections.Immutable;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Mcp.Tools.Time;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class QueryFeaturesToolValidityTests
{
    private static S122Feature MpaWithFixedRange(string id, string? start, string? end)
    {
        var sub = ImmutableDictionary.CreateBuilder<string, string>();
        if (start is not null) sub["dateStart"] = start;
        if (end is not null) sub["dateEnd"] = end;

        return new S122Feature
        {
            Id = id,
            FeatureType = "MarineProtectedArea",
            GeometryType = GmlGeometryType.Point,
            Points = ImmutableArray.Create((5.0, 5.0)),
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray.Create(new S122ComplexAttribute
            {
                Code = "fixedDateRange",
                SubAttributes = sub.ToImmutable(),
            }),
        };
    }

    private static S122Feature MpaWithoutValidity(string id)
    {
        return new S122Feature
        {
            Id = id,
            FeatureType = "MarineProtectedArea",
            GeometryType = GmlGeometryType.Point,
            Points = ImmutableArray.Create((5.0, 5.0)),
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = ImmutableDictionary<string, string>.Empty,
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

    [Fact]
    public async Task Times_null_includes_every_feature_regardless_of_validity()
    {
        var inWindow = MpaWithFixedRange("in", "2024-01-01", "2024-12-31");
        var outOfWindow = MpaWithFixedRange("out", "2030-01-01", "2030-12-31");
        var noMetadata = MpaWithoutValidity("nometa");

        var tool = new QueryFeaturesTool(BuildCatalog(inWindow, outOfWindow, noMetadata));
        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.TotalCount);
    }

    [Fact]
    public async Task Times_instant_excludes_features_with_disjoint_validity()
    {
        var inWindow = MpaWithFixedRange("in", "2024-01-01", "2024-12-31");
        var outOfWindow = MpaWithFixedRange("out", "2030-01-01", "2030-12-31");

        var tool = new QueryFeaturesTool(BuildCatalog(inWindow, outOfWindow));
        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10)),
            Times: TimeQuery.At(DateTimeOffset.Parse("2024-06-15T12:00:00Z"))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(1, value.TotalCount);
        Assert.Equal("in", value.Features[0].FeatureId);
    }

    [Fact]
    public async Task Times_range_includes_overlapping_validity()
    {
        var ends2024 = MpaWithFixedRange("ends-2024", "2020-01-01", "2024-12-31");
        var starts2025 = MpaWithFixedRange("starts-2025", "2025-01-01", "2025-12-31");

        var tool = new QueryFeaturesTool(BuildCatalog(ends2024, starts2025));
        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10)),
            Times: TimeQuery.Between(
                DateTimeOffset.Parse("2024-12-01T00:00:00Z"),
                DateTimeOffset.Parse("2025-02-01T00:00:00Z"))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.TotalCount);
    }

    [Fact]
    public async Task Times_filter_always_includes_features_without_validity_metadata()
    {
        var noMetadata = MpaWithoutValidity("nometa");
        var outOfWindow = MpaWithFixedRange("out", "2030-01-01", "2030-12-31");

        var tool = new QueryFeaturesTool(BuildCatalog(noMetadata, outOfWindow));
        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10)),
            Times: TimeQuery.At(DateTimeOffset.Parse("2024-06-15T12:00:00Z"))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(1, value.TotalCount);
        Assert.Equal("nometa", value.Features[0].FeatureId);
    }

    [Fact]
    public async Task Open_ended_range_with_only_start_overlaps_later_window()
    {
        var openEnded = MpaWithFixedRange("open", "2020-01-01", null);

        var tool = new QueryFeaturesTool(BuildCatalog(openEnded));
        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10)),
            Times: TimeQuery.At(DateTimeOffset.Parse("2050-01-01T00:00:00Z"))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(1, value.TotalCount);
    }
}
