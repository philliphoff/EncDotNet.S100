using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class DescribeFeatureToolS104Tests
{
    [Fact]
    public async Task Dcf2_Ok_DescribesInstance()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104("ds"));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "WaterLevel.01"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("WaterLevel", value.FeatureTypeName);
        Assert.Equal("S-104", value.Spec.Name);
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
        catalog.Add(LoadedDatasetFactory.S104("ds"));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "WaterLevel.01.Group_002"));

        Assert.True(result.TryGetValue(out var value));
        var attrs = value.Attributes;
        Assert.Equal(2, attrs.GetProperty("groupIndex").GetInt32());
        Assert.Equal("Group_002", attrs.GetProperty("groupId").GetString());
        Assert.True(attrs.TryGetProperty("timePoint", out _));
        var hr = attrs.GetProperty("heightRange");
        Assert.True(hr.GetProperty("min").GetSingle() > 0);
        var trends = attrs.GetProperty("trendCounts");
        Assert.Equal(16, trends.GetProperty("steady").GetInt32());
    }

    [Fact]
    public async Task Dcf2_NotFound_UnknownGroupIndex()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104("ds"));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "WaterLevel.01.Group_099"));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<FeatureNotFound>(err);
    }

    [Fact]
    public async Task Dcf8_Ok_DescribesInstanceWithStationCount()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104Stations("ds", S104Synth.StationSeries(stationCount: 3)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "WaterLevel.01"));

        Assert.True(result.TryGetValue(out var value));
        var attrs = value.Attributes;
        Assert.Equal(8, attrs.GetProperty("dataCodingFormat").GetInt32());
        Assert.Equal(3, attrs.GetProperty("stations").GetProperty("count").GetInt32());
        Assert.Equal(0.1, attrs.GetProperty("waterLevelTrendThreshold").GetDouble());
    }

    [Fact]
    public async Task Dcf8_Ok_DescribesStationByGroupIndex()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104Stations("ds", S104Synth.StationSeries(stationCount: 3)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "WaterLevel.01.Group_002"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("WaterLevelStation", value.FeatureTypeName);
        var attrs = value.Attributes;
        Assert.Equal("STN_002", attrs.GetProperty("stationId").GetString());
        var pos = attrs.GetProperty("position");
        Assert.Equal(47.1, pos.GetProperty("latitude").GetDouble(), 6);
    }

    [Fact]
    public async Task Dcf8_Ok_DescribesStationByIdentifier()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104Stations("ds", S104Synth.StationSeries(stationCount: 3)));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "STN_003"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("WaterLevelStation", value.FeatureTypeName);
        Assert.Equal("STN_003", value.Attributes.GetProperty("stationId").GetString());
    }

    [Fact]
    public async Task Dcf8_NotFound_UnknownStationIdentifier()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104Stations("ds", S104Synth.StationSeries()));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "NOT_A_STATION"));

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
            LoadedDatasetFactory.S104Spec,
            s101.Bounds,
            null,
            s101.Data));
        var tool = new DescribeFeatureTool(catalog);

        var result = await tool.InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("ds"), "WaterLevel.01"));

        Assert.True(result.TryGetError(out var err));
        var unsupported = Assert.IsType<SpecNotSupportedForTool>(err);
        Assert.Equal("S-104", unsupported.Spec.Name);
    }
}
