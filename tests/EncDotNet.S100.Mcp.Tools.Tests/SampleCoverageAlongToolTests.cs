using System.Collections.Immutable;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class SampleCoverageAlongToolTests
{
    [Fact]
    public async Task Samples_every_vertex_inside_bounds_returns_depth_at_each()
    {
        var catalog = new FakeDatasetCatalog();
        var dataset = S102Synth.Dataset(originLat: 0, originLon: 0, spacingLat: 0.01, spacingLon: 0.01,
            numRows: 4, numCols: 4, depth: 25.0f, uncertainty: 0.5f);
        catalog.Add(LoadedDatasetFactory.S102(
            "s102-1",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S102CoverageSource(dataset)));
        var tool = new SampleCoverageAlongTool(catalog);

        var polyline = new GeoPolyline(ImmutableArray.Create(
            new GeoPoint(0.01, 0.01),
            new GeoPoint(0.02, 0.02),
            new GeoPoint(0.03, 0.03)));

        var result = await tool.InvokeAsync(new SampleCoverageAlongRequest(
            LoadedDatasetFactory.S102Spec, polyline));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.Samples.Length);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(i, value.Samples[i].VertexIndex);
            Assert.NotNull(value.Samples[i].Result);
            var depth = Assert.IsType<DepthSample>(value.Samples[i].Result!.Value);
            Assert.Equal(25.0, depth.DepthMeters);
        }
    }

    [Fact]
    public async Task Vertices_outside_bounds_yield_null_results_in_order()
    {
        var catalog = new FakeDatasetCatalog();
        var dataset = S102Synth.Dataset(originLat: 0, originLon: 0, spacingLat: 0.01, spacingLon: 0.01,
            numRows: 4, numCols: 4, depth: 10.0f, uncertainty: 0.1f);
        catalog.Add(LoadedDatasetFactory.S102(
            "s102-1",
            bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
            source: new S102CoverageSource(dataset)));
        var tool = new SampleCoverageAlongTool(catalog);

        var polyline = new GeoPolyline(ImmutableArray.Create(
            new GeoPoint(0.02, 0.02),       // inside
            new GeoPoint(50, 50),           // outside — NoDatasetCoversPoint
            new GeoPoint(0.03, 0.03)));     // inside

        var result = await tool.InvokeAsync(new SampleCoverageAlongRequest(
            LoadedDatasetFactory.S102Spec, polyline));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.Samples.Length);
        Assert.NotNull(value.Samples[0].Result);
        Assert.Null(value.Samples[1].Result);
        Assert.NotNull(value.Samples[2].Result);
    }

    [Fact]
    public async Task Unsupported_spec_propagates_as_request_level_error()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new SampleCoverageAlongTool(catalog);

        var polyline = new GeoPolyline(ImmutableArray.Create(
            new GeoPoint(0, 0),
            new GeoPoint(1, 1)));

        var result = await tool.InvokeAsync(new SampleCoverageAlongRequest(
            new SpecRef("S-101", new SpecVersion(1, 0, 0)),
            polyline));

        Assert.False(result.TryGetValue(out _));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<SpecNotSupportedForTool>(err);
    }

    [Fact]
    public async Task Empty_polyline_returns_geometry_invalid()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new SampleCoverageAlongTool(catalog);

        var polyline = new GeoPolyline(ImmutableArray<GeoPoint>.Empty);

        var result = await tool.InvokeAsync(new SampleCoverageAlongRequest(
            LoadedDatasetFactory.S102Spec, polyline));

        Assert.False(result.TryGetValue(out _));
        Assert.True(result.TryGetError(out var err));
        Assert.True(err is GeometryInvalid or InvalidArgument);
    }
}
