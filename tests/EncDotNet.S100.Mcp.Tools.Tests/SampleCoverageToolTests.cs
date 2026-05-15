using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class SampleCoverageToolTests
{
    [Fact]
    public async Task Returns_depth_sample_inside_bounds()
    {
        var catalog = new FakeDatasetCatalog();
        var dataset = S102Synth.Dataset(originLat: 0, originLon: 0, spacingLat: 0.01, spacingLon: 0.01,
            numRows: 4, numCols: 4, depth: 25.0f, uncertainty: 0.5f);
        catalog.Add(LoadedDatasetFactory.S102(
            "s102-1",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S102CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S102Spec, Latitude: 0.02, Longitude: 0.02));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(new DatasetId("s102-1"), value.DatasetId);
        var depth = Assert.IsType<DepthSample>(value.Value);
        Assert.Equal(25.0, depth.DepthMeters);
        Assert.Equal(0.5, depth.UncertaintyMeters);
    }

    [Fact]
    public async Task Returns_NoDatasetCoversPoint_when_point_outside_bounds()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S102("s102-1", bounds: LoadedDatasetFactory.Box(0, 0, 1, 1)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S102Spec, Latitude: 50, Longitude: 50));

        Assert.True(result.TryGetError(out var error));
        var miss = Assert.IsType<NoDatasetCoversPoint>(error);
        Assert.Equal(50, miss.Latitude);
        Assert.Equal(50, miss.Longitude);
    }

    [Fact]
    public async Task Returns_NoDatasetCoversPoint_when_no_S102_loaded()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("vector-only"));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S102Spec, Latitude: 0, Longitude: 0));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<NoDatasetCoversPoint>(error);
    }

    [Fact]
    public async Task Returns_SpecNotSupported_for_S104()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 0, Longitude: 0));

        Assert.True(result.TryGetError(out var error));
        var unsupported = Assert.IsType<SpecNotSupportedForTool>(error);
        Assert.Equal("S-104", unsupported.Spec.Name);
        Assert.Equal(SampleCoverageTool.Name, unsupported.Tool);
    }

    [Fact]
    public async Task Returns_DatasetClosedDuringQuery_when_handle_disposed()
    {
        var catalog = new FakeDatasetCatalog();
        var dataset = S102Synth.Dataset();
        var source = new DisposableS102CoverageSource(dataset);
        catalog.Add(LoadedDatasetFactory.S102("ds", bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04), source: source));
        var tool = new SampleCoverageTool(catalog);
        source.Dispose();

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S102Spec, Latitude: 0.02, Longitude: 0.02));

        Assert.True(result.TryGetError(out var error));
        var closed = Assert.IsType<DatasetClosedDuringQuery>(error);
        Assert.Equal(new DatasetId("ds"), closed.Id);
    }

    [Fact]
    public async Task Nearest_cell_clamps_to_grid_when_point_on_boundary()
    {
        var catalog = new FakeDatasetCatalog();
        // Cell-centred grid where the bounds include the grid origin corner exactly.
        var dataset = S102Synth.Dataset(originLat: 10, originLon: 20, depth: 7.0f, uncertainty: 0.0f);
        catalog.Add(LoadedDatasetFactory.S102(
            "edge",
            bounds: LoadedDatasetFactory.Box(10, 20, 10.04, 20.04),
            source: new S102CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S102Spec, Latitude: 10.0, Longitude: 20.0));

        Assert.True(result.TryGetValue(out var value));
        var depth = Assert.IsType<DepthSample>(value.Value);
        Assert.Equal(7.0, depth.DepthMeters);
    }
}
