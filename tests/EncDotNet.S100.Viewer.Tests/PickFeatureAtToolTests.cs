using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using Mapsui;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class PickFeatureAtToolTests
{
    private sealed class FakeMapHost : IMapHost
    {
        public (double Latitude, double Longitude)? ScreenResult { get; set; }
        public (double X, double Y, int W, int H)? LastScreenCall { get; private set; }

        public void AddLayer(ILayer layer) { }
        public void RemoveLayer(ILayer layer) { }
        public void AddOverlayLayer(ILayer layer) { }
        public void RemoveOverlayLayer(ILayer layer) { }
        public void ReorderDatasetLayers(IReadOnlyList<ILayer> orderedDatasetLayers) { }
        public void ZoomToExtent(MRect extent) { }
        public void SetViewportToExtent(MRect mercatorExtent) { }
        public void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution) { }
        public void CenterOn(double latitudeWgs84, double longitudeWgs84, long durationMs = 300) { }
        public (double Latitude, double Longitude)? TryGetViewportCenterWgs84() => null;

        public (double Latitude, double Longitude)? TryScreenToWgs84(double xPixels, double yPixels, int widthPx, int heightPx)
        {
            LastScreenCall = (xPixels, yPixels, widthPx, heightPx);
            return ScreenResult;
        }

        public Task<byte[]?> RenderCurrentViewToPngAsync(int widthPx, int heightPx, double pixelDensity, CancellationToken ct = default)
            => Task.FromResult<byte[]?>(null);
    }

    private sealed class FakeAccessor : IMapHostAccessor
    {
        public IMapHost? Current { get; set; }
    }

    private sealed class EmptyCatalog : IDatasetCatalog
    {
        public ImmutableArray<LoadedDataset> Datasets => ImmutableArray<LoadedDataset>.Empty;
        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }

    private static PickFeatureAtTool Make(IMapHost? host)
        => new(new FakeAccessor { Current = host }, new EmptyCatalog());

    [Fact]
    public async Task Map_not_ready_when_accessor_has_no_host()
    {
        var tool = Make(host: null);

        var result = await tool.InvokeAsync(new PickFeatureAtRequest(10, 10));

        Assert.False(result.TryGetValue(out _));
        Assert.True(result.TryGetError(out var err));
        Assert.Equal("map_not_ready", err!.Code);
    }

    [Fact]
    public async Task Pixel_outside_image_width_is_rejected()
    {
        var tool = Make(new FakeMapHost { ScreenResult = (50, -4) });

        var result = await tool.InvokeAsync(new PickFeatureAtRequest(X: 5000, Y: 10, Width: 1024, Height: 768));

        Assert.True(result.TryGetError(out var err));
        Assert.Equal("invalid_argument", err!.Code);
    }

    [Fact]
    public async Task Non_finite_pixel_is_rejected()
    {
        var tool = Make(new FakeMapHost { ScreenResult = (50, -4) });

        var result = await tool.InvokeAsync(new PickFeatureAtRequest(X: double.NaN, Y: 10));

        Assert.True(result.TryGetError(out var err));
        Assert.Equal("invalid_argument", err!.Code);
    }

    [Fact]
    public async Task Conversion_failure_surfaces_map_not_ready()
    {
        var tool = Make(new FakeMapHost { ScreenResult = null });

        var result = await tool.InvokeAsync(new PickFeatureAtRequest(100, 100));

        Assert.True(result.TryGetError(out var err));
        Assert.Equal("map_not_ready", err!.Code);
    }

    [Fact]
    public async Task Defaults_image_size_to_1024x768()
    {
        var host = new FakeMapHost { ScreenResult = (50.5, -3.5) };
        var tool = Make(host);

        var result = await tool.InvokeAsync(new PickFeatureAtRequest(X: 512, Y: 384));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(1024, value!.Width);
        Assert.Equal(768, value.Height);
        Assert.Equal((512.0, 384.0, 1024, 768), host.LastScreenCall);
    }

    [Fact]
    public async Task Returns_world_point_and_empty_results_over_empty_catalog()
    {
        var tool = Make(new FakeMapHost { ScreenResult = (48.25, -4.75) });

        var result = await tool.InvokeAsync(new PickFeatureAtRequest(X: 200, Y: 300));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(48.25, value!.WorldLatitude, 6);
        Assert.Equal(-4.75, value.WorldLongitude, 6);
        Assert.Empty(value.Features);
        Assert.Empty(value.Datasets);
        Assert.False(value.FeaturesTruncated);
    }
}
