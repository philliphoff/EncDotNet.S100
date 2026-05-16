using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S111;
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
    public async Task S104_returns_water_level_sample_at_first_time_step_by_default()
    {
        var catalog = new FakeDatasetCatalog();
        var times = new[]
        {
            new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 6, 1, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 6, 1, 2, 0, 0, DateTimeKind.Utc),
        };
        var dataset = S104Synth.Dataset(originLat: 0, originLon: 0, height: 1.5f, trend: 2, times: times);
        catalog.Add(LoadedDatasetFactory.S104(
            "wl-1",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S104CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 0.02, Longitude: 0.02));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<WaterLevelSample>(value.Value);
        Assert.Equal(1.5, sample.WaterLevelHeight, 5);
        Assert.Equal("increasing", sample.Trend);
        Assert.Equal(times[0], sample.SampleTime);
        Assert.Null(sample.RequestedTime);
    }

    [Fact]
    public async Task S104_clamps_to_last_time_step_when_requested_time_is_after_dataset()
    {
        var catalog = new FakeDatasetCatalog();
        var times = new[]
        {
            new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 6, 1, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 6, 1, 2, 0, 0, DateTimeKind.Utc),
        };
        var dataset = S104Synth.Dataset(times: times);
        catalog.Add(LoadedDatasetFactory.S104(
            "wl-clamp-late",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S104CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var requested = new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 0.02, Longitude: 0.02, Time: requested));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<WaterLevelSample>(value.Value);
        Assert.Equal(times[^1], sample.SampleTime);
        Assert.Equal(requested, sample.RequestedTime);
    }

    [Fact]
    public async Task S104_clamps_to_first_time_step_when_requested_time_is_before_dataset()
    {
        var catalog = new FakeDatasetCatalog();
        var times = new[]
        {
            new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 6, 1, 1, 0, 0, DateTimeKind.Utc),
        };
        var dataset = S104Synth.Dataset(times: times);
        catalog.Add(LoadedDatasetFactory.S104(
            "wl-clamp-early",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S104CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var requested = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 0.02, Longitude: 0.02, Time: requested));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<WaterLevelSample>(value.Value);
        Assert.Equal(times[0], sample.SampleTime);
    }

    [Fact]
    public async Task S104_returns_OutOfBounds_when_point_outside_loaded_S104_bounds()
    {
        var catalog = new FakeDatasetCatalog();
        var dataset = S104Synth.Dataset();
        catalog.Add(LoadedDatasetFactory.S104(
            "wl-oob",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S104CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 50, Longitude: 50));

        Assert.True(result.TryGetError(out var error));
        var oob = Assert.IsType<OutOfBounds>(error);
        Assert.Equal("S-104", oob.Spec.Name);
    }

    [Fact]
    public async Task S104_returns_NoDatasetCoversPoint_when_no_S104_loaded()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("v"));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 0, Longitude: 0));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<NoDatasetCoversPoint>(error);
    }

    [Fact]
    public async Task S104_returns_NotSupportedYet_for_non_regular_grid()
    {
        var catalog = new FakeDatasetCatalog();
        var dataset = S104Synth.Dataset(dataCodingFormat: 8);
        catalog.Add(LoadedDatasetFactory.S104(
            "wl-dcf8",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S104CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 0.02, Longitude: 0.02));

        Assert.True(result.TryGetError(out var error));
        var nsy = Assert.IsType<NotSupportedYet>(error);
        Assert.Equal("S-104", nsy.Spec.Name);
        Assert.Equal(SampleCoverageTool.Name, nsy.Tool);
    }

    [Fact]
    public async Task S104_returns_NoDataAtPoint_when_cell_is_fill_value()
    {
        var catalog = new FakeDatasetCatalog();
        var dataset = S104Synth.Dataset(height: S104CoverageSource.FillValue);
        catalog.Add(LoadedDatasetFactory.S104(
            "wl-nodata",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S104CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 0.02, Longitude: 0.02));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<NoDataAtPoint>(error);
    }

    [Fact]
    public async Task S111_returns_surface_current_sample_with_metres_per_second_and_knots()
    {
        var catalog = new FakeDatasetCatalog();
        var dataset = S111Synth.Dataset(speed: 1.0f, direction: 45.0f);
        catalog.Add(LoadedDatasetFactory.S111(
            "sc-1",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S111CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: 0.02, Longitude: 0.02));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<SurfaceCurrentSample>(value.Value);
        Assert.Equal(1.0, sample.SpeedMetresPerSecond, 5);
        // 1.0 m/s × 1.94384… ≈ 1.94384 kn.
        Assert.Equal(1.94384, sample.SpeedKnots, 3);
        Assert.Equal(45.0, sample.DirectionDegreesTrue, 5);
    }

    [Fact]
    public async Task S111_clamps_time_outside_range()
    {
        var catalog = new FakeDatasetCatalog();
        var times = new[]
        {
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
        };
        var dataset = S111Synth.Dataset(times: times);
        catalog.Add(LoadedDatasetFactory.S111(
            "sc-clamp",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S111CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var before = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var beforeResult = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: 0.02, Longitude: 0.02, Time: before));
        Assert.True(beforeResult.TryGetValue(out var bv));
        Assert.Equal(times[0], Assert.IsType<SurfaceCurrentSample>(bv.Value).SampleTime);

        var after = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var afterResult = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: 0.02, Longitude: 0.02, Time: after));
        Assert.True(afterResult.TryGetValue(out var av));
        Assert.Equal(times[^1], Assert.IsType<SurfaceCurrentSample>(av.Value).SampleTime);
    }

    [Fact]
    public async Task S111_returns_NotSupportedYet_for_non_regular_grid()
    {
        var catalog = new FakeDatasetCatalog();
        var dataset = S111Synth.Dataset(dataCodingFormat: 3);
        catalog.Add(LoadedDatasetFactory.S111(
            "sc-dcf3",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S111CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: 0.02, Longitude: 0.02));

        Assert.True(result.TryGetError(out var error));
        var nsy = Assert.IsType<NotSupportedYet>(error);
        Assert.Equal("S-111", nsy.Spec.Name);
    }

    [Fact]
    public async Task S111_returns_OutOfBounds_when_point_outside_loaded_S111_bounds()
    {
        var catalog = new FakeDatasetCatalog();
        var dataset = S111Synth.Dataset();
        catalog.Add(LoadedDatasetFactory.S111(
            "sc-oob",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S111CoverageSource(dataset)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: -50, Longitude: -50));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<OutOfBounds>(error);
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
