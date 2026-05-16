using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class DescribeFeatureToolS111Tests
{
    [Fact]
    public async Task Dcf2_Ok_DescribesInstance()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111("ds"));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "SurfaceCurrent.01"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("SurfaceCurrent", value.FeatureTypeName);
        Assert.Equal("S-111", value.Spec.Name);
        var attrs = value.Attributes;
        Assert.Equal(2, attrs.GetProperty("dataCodingFormat").GetInt32());
        var ts = attrs.GetProperty("timeSteps");
        Assert.Equal(3, ts.GetProperty("count").GetInt32());
        Assert.Equal(3600.0, ts.GetProperty("intervalSeconds").GetDouble());
    }

    [Fact]
    public async Task Dcf2_Ok_DescribesGroup()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111("ds"));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "SurfaceCurrent.01.Group_001"));

        Assert.True(result.TryGetValue(out var value));
        var attrs = value.Attributes;
        Assert.Equal(1, attrs.GetProperty("groupIndex").GetInt32());
        Assert.Equal("Group_001", attrs.GetProperty("groupId").GetString());
        var sr = attrs.GetProperty("speedRange");
        Assert.Equal(0.5f, sr.GetProperty("min").GetSingle());
        var dr = attrs.GetProperty("directionRange");
        Assert.Equal(90.0f, dr.GetProperty("min").GetSingle());
    }

    [Fact]
    public async Task Dcf2_NotFound_UnknownGroupIndex()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111("ds"));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "SurfaceCurrent.01.Group_099"));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<FeatureNotFound>(err);
    }

    [Fact]
    public async Task Dcf8_Ok_DescribesInstanceWithStationCount()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111Stations("ds", S111Synth.StationSeries(stationCount: 4)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "SurfaceCurrent.01"));

        Assert.True(result.TryGetValue(out var value));
        var attrs = value.Attributes;
        Assert.Equal(8, attrs.GetProperty("dataCodingFormat").GetInt32());
        Assert.Equal(4, attrs.GetProperty("stations").GetProperty("count").GetInt32());
        Assert.Equal(6, attrs.GetProperty("typeOfCurrentData").GetInt32());
        Assert.Equal(1.5f, attrs.GetProperty("surfaceCurrentDepth").GetSingle());
    }

    [Fact]
    public async Task Dcf8_Ok_DescribesStationByGroupIndex()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111Stations("ds", S111Synth.StationSeries(stationCount: 3)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "SurfaceCurrent.01.Group_001"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("SurfaceCurrentStation", value.FeatureTypeName);
        Assert.Equal("CUR_001", value.Attributes.GetProperty("stationId").GetString());
    }

    [Fact]
    public async Task Dcf8_Ok_DescribesStationByIdentifier()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111Stations("ds", S111Synth.StationSeries(stationCount: 3)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "CUR_002"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("SurfaceCurrentStation", value.FeatureTypeName);
        var attrs = value.Attributes;
        Assert.Equal("CUR_002", attrs.GetProperty("stationId").GetString());
        var tr = attrs.GetProperty("timeRange");
        Assert.Equal(4, tr.GetProperty("count").GetInt32());
        Assert.Equal(3600.0, tr.GetProperty("intervalSeconds").GetDouble());
    }

    [Fact]
    public async Task Dcf8_NotFound_UnknownStationIdentifier()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111Stations("ds", S111Synth.StationSeries()));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "BAD_ID"));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<FeatureNotFound>(err);
    }

    [Fact]
    public async Task SpecMismatch_PayloadIsS101()
    {
        var catalog = new FakeDatasetCatalog();
        var s101 = LoadedDatasetFactory.S101("ds");
        catalog.Add(new LoadedDataset(
            s101.Id,
            LoadedDatasetFactory.S111Spec,
            s101.Bounds,
            null,
            s101.Data));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "SurfaceCurrent.01"));

        Assert.True(result.TryGetError(out var err));
        var unsupported = Assert.IsType<SpecNotSupportedForTool>(err);
        Assert.Equal("S-111", unsupported.Spec.Name);
    }
}
