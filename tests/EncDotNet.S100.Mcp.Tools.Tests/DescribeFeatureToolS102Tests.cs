using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class DescribeFeatureToolS102Tests
{
    [Fact]
    public async Task Ok_DescribesCoverageInstance()
    {
        var catalog = new FakeDatasetCatalog();
        var dataset = S102Synth.Dataset(
            originLat: 47.0,
            originLon: -122.0,
            spacingLat: 0.01,
            spacingLon: 0.01,
            numRows: 3,
            numCols: 4,
            depth: 12.5f,
            uncertainty: 0.25f);
        catalog.Add(LoadedDatasetFactory.S102("s102-ds", source: new(dataset)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("s102-ds"), "BathymetryCoverage.01"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("S-102", value.Spec.Name);
        Assert.Equal("BathymetryCoverage", value.FeatureTypeName);
        Assert.Empty(value.References);

        var attrs = value.Attributes;
        Assert.Equal("BathymetryCoverage.01", attrs.GetProperty("instanceId").GetString());
        var grid = attrs.GetProperty("gridMetadata");
        Assert.Equal(47.0, grid.GetProperty("originLatitude").GetDouble());
        Assert.Equal(-122.0, grid.GetProperty("originLongitude").GetDouble());
        Assert.Equal(3, grid.GetProperty("numPointsLatitudinal").GetInt32());
        Assert.Equal(4, grid.GetProperty("numPointsLongitudinal").GetInt32());

        Assert.Equal(4326, attrs.GetProperty("horizontalCRS").GetInt32());
        Assert.Equal(1_000_000f, attrs.GetProperty("noDataValue").GetSingle());

        var depthRange = attrs.GetProperty("depthRange");
        Assert.Equal(12.5f, depthRange.GetProperty("min").GetSingle());
        Assert.Equal(12.5f, depthRange.GetProperty("max").GetSingle());
        Assert.Equal(0, depthRange.GetProperty("nodataCount").GetInt32());

        var fields = attrs.GetProperty("valueFields");
        Assert.Equal(2, fields.GetArrayLength());
        Assert.Equal("depth", fields[0].GetProperty("name").GetString());
        Assert.Equal("uncertainty", fields[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Ok_AcceptsBareBathymetryCoverageId()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S102("ds"));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "BathymetryCoverage"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("BathymetryCoverage", value.FeatureTypeName);
    }

    [Fact]
    public async Task NotFound_UnknownInstanceId()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S102("ds"));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "BathymetryCoverage.99"));

        Assert.True(result.TryGetError(out var err));
        var nf = Assert.IsType<FeatureNotFound>(err);
        Assert.Equal("BathymetryCoverage.99", nf.FeatureId);
    }

    [Fact]
    public async Task NotFound_UnparseableId()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S102("ds"));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "NotARealId"));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<FeatureNotFound>(err);
    }

    [Fact]
    public async Task DepthRange_SkipsFillValues()
    {
        var dataset = new EncDotNet.S100.Datasets.S102.S102Dataset
        {
            HorizontalCRS = 4326,
            Coverages =
            [
                new EncDotNet.S100.Datasets.S102.BathymetryCoverage
                {
                    OriginLatitude = 0, OriginLongitude = 0,
                    SpacingLatitudinal = 1, SpacingLongitudinal = 1,
                    NumPointsLatitudinal = 2, NumPointsLongitudinal = 2,
                    Values =
                    [
                        new(5.0f, 0.1f),
                        new(1_000_000f, 1_000_000f),
                        new(10.0f, 0.2f),
                        new(1_000_000f, 1_000_000f),
                    ],
                },
            ],
        };
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S102("ds", source: new EncDotNet.S100.Datasets.S102.S102CoverageSource(dataset)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "BathymetryCoverage.01"));

        Assert.True(result.TryGetValue(out var value));
        var dr = value.Attributes.GetProperty("depthRange");
        Assert.Equal(5.0f, dr.GetProperty("min").GetSingle());
        Assert.Equal(10.0f, dr.GetProperty("max").GetSingle());
        Assert.Equal(2, dr.GetProperty("nodataCount").GetInt32());
    }

    [Fact]
    public async Task SpecMismatch_PayloadIsS101()
    {
        var catalog = new FakeDatasetCatalog();
        // Inject an S-101 dataset but register it under the S-102 spec so
        // the registry dispatches to S102FeatureDescriber.
        var s101 = LoadedDatasetFactory.S101("ds");
        var mismatched = new LoadedDataset(
            s101.Id,
            LoadedDatasetFactory.S102Spec,
            s101.Bounds,
            null,
            s101.Data);
        catalog.Add(mismatched);
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "BathymetryCoverage.01"));

        Assert.True(result.TryGetError(out var err));
        var unsupported = Assert.IsType<SpecNotSupportedForTool>(err);
        Assert.Equal("S-102", unsupported.Spec.Name);
        Assert.Equal(DescribeFeatureTool.Name, unsupported.Tool);
    }
}
