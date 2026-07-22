using System.Text.Json;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.DynamicSources;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Viewer.Tests;

public class PickFeaturesToolTests
{
    private sealed class FakeAccessor : IMapHostAccessor
    {
        public IMapHost? Current { get; set; }
    }

    private sealed class FakeCatalog : IDatasetCatalog
    {
        public IReadOnlyList<LoadedDataset> Datasets { get; set; } = [];
        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;
        public void Raise() => Changed?.Invoke(this, null!);
    }

    private static (PickFeaturesTool tool, FakeMapHost host, FakeAccessor accessor) Make(
        IMapHost? hostOverride = null)
    {
        var host = hostOverride as FakeMapHost ?? new FakeMapHost();
        var accessor = new FakeAccessor { Current = hostOverride is null ? host : hostOverride };
        var tool = new PickFeaturesTool(accessor, new FakeCatalog());
        return (tool, host, accessor);
    }

    [Fact]
    public async Task Pixel_form_projects_through_host_and_echoes_resolved_point()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = (800, 600);
        host.ScreenToWgs84 = (x, y) => new GeoPosition(47.6, -122.3);

        var result = await tool.InvokeAsync(new PickFeaturesRequest(X: 400, Y: 300));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("pixel", value!.Source);
        Assert.Equal(47.6, value.Latitude, 6);
        Assert.Equal(-122.3, value.Longitude, 6);
        Assert.Empty(value.Features);
    }

    [Fact]
    public async Task Geo_form_passes_through_without_a_host()
    {
        var (tool, _, accessor) = Make();
        accessor.Current = null;

        var result = await tool.InvokeAsync(new PickFeaturesRequest(Latitude: 47.6, Longitude: -122.3));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("geo", value!.Source);
        Assert.Equal(47.6, value.Latitude, 6);
        Assert.Equal(-122.3, value.Longitude, 6);
    }

    [Fact]
    public async Task Both_forms_supplied_is_rejected()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = (800, 600);
        host.ScreenToWgs84 = (_, _) => new GeoPosition(1, 2);

        var result = await tool.InvokeAsync(new PickFeaturesRequest(X: 1, Y: 2, Latitude: 3, Longitude: 4));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public async Task Neither_form_supplied_is_rejected()
    {
        var (tool, _, _) = Make();

        var result = await tool.InvokeAsync(new PickFeaturesRequest());

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public async Task Pixel_missing_one_axis_is_rejected()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = (800, 600);

        var result = await tool.InvokeAsync(new PickFeaturesRequest(X: 400));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public async Task Non_finite_pixel_is_rejected()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = (800, 600);

        var result = await tool.InvokeAsync(new PickFeaturesRequest(X: double.NaN, Y: 10));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public async Task Pixel_outside_viewport_is_rejected()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = (800, 600);
        host.ScreenToWgs84 = (_, _) => new GeoPosition(1, 2);

        var result = await tool.InvokeAsync(new PickFeaturesRequest(X: 900, Y: 300));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public async Task Pixel_form_with_no_host_is_map_not_ready()
    {
        var (tool, _, accessor) = Make();
        accessor.Current = null;

        var result = await tool.InvokeAsync(new PickFeaturesRequest(X: 10, Y: 10));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<MapNotReady>(error);
    }

    [Fact]
    public async Task Pixel_form_with_unlaid_out_viewport_is_map_not_ready()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = null;

        var result = await tool.InvokeAsync(new PickFeaturesRequest(X: 10, Y: 10));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<MapNotReady>(error);
    }

    [Fact]
    public async Task Pixel_that_does_not_project_is_rejected()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = (800, 600);
        host.ScreenToWgs84 = (_, _) => null;

        var result = await tool.InvokeAsync(new PickFeaturesRequest(X: 10, Y: 10));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public async Task Image_form_resolves_through_image_pixel_projection()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = (800, 600);
        // Live ScreenToWgs84 would give a different answer; the image-fit
        // path must use the image projection instead.
        host.ScreenToWgs84 = (_, _) => new GeoPosition(10, 20);
        host.ImagePixelToWgs84 = (x, y, w, h) =>
        {
            Assert.Equal(512, x);
            Assert.Equal(384, y);
            Assert.Equal(1024, w);
            Assert.Equal(768, h);
            return new GeoPosition(47.6, -122.3);
        };

        var result = await tool.InvokeAsync(
            new PickFeaturesRequest(X: 512, Y: 384, ImageWidth: 1024, ImageHeight: 768));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal("pixel", value!.Source);
        Assert.Equal(47.6, value.Latitude, 6);
        Assert.Equal(-122.3, value.Longitude, 6);
    }

    [Fact]
    public async Task Image_form_requires_both_dimensions()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = (800, 600);

        var result = await tool.InvokeAsync(
            new PickFeaturesRequest(X: 100, Y: 100, ImageWidth: 1024));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public async Task Image_form_rejects_pixel_outside_image_bounds()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = (800, 600);
        host.ImagePixelToWgs84 = (_, _, _, _) => new GeoPosition(1, 2);

        var result = await tool.InvokeAsync(
            new PickFeaturesRequest(X: 2000, Y: 100, ImageWidth: 1024, ImageHeight: 768));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public async Task Image_form_with_unlaid_out_viewport_is_map_not_ready()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = null;
        host.ImagePixelToWgs84 = (_, _, _, _) => new GeoPosition(1, 2);

        var result = await tool.InvokeAsync(
            new PickFeaturesRequest(X: 100, Y: 100, ImageWidth: 1024, ImageHeight: 768));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<MapNotReady>(error);
    }

    [Fact]
    public async Task Image_form_rejects_non_positive_dimensions()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = (800, 600);

        var result = await tool.InvokeAsync(
            new PickFeaturesRequest(X: 100, Y: 100, ImageWidth: 0, ImageHeight: 768));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public async Task Image_form_pixel_that_does_not_project_is_rejected()
    {
        var (tool, host, _) = Make();
        host.ViewportSizePx = (800, 600);
        host.ImagePixelToWgs84 = (_, _, _, _) => null;

        var result = await tool.InvokeAsync(
            new PickFeaturesRequest(X: 100, Y: 100, ImageWidth: 1024, ImageHeight: 768));

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public void Adapter_creates_tool_with_expected_name()
    {
        var (tool, _, _) = Make();

        var mcpTool = PickFeaturesMcpAdapter.Create(tool);

        Assert.Equal("pick_features", mcpTool.ProtocolTool.Name);
    }

    [Fact]
    public void Adapter_translates_success_to_non_error_content()
    {
        var value = new PickFeaturesResult(
            "geo", 47.6, -122.3, [], 0, false);

        var translated = PickFeaturesMcpAdapter.TranslateResult(ToolResult<PickFeaturesResult>.Ok(value));

        Assert.False(translated.IsError);
        var text = Assert.IsType<TextContentBlock>(translated.Content.Single());
        using var doc = JsonDocument.Parse(text.Text);
        Assert.Equal("geo", doc.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public void Adapter_translates_failure_to_error_content()
    {
        var translated = PickFeaturesMcpAdapter.TranslateResult(
            ToolResult<PickFeaturesResult>.Err(new InvalidArgument("request", "boom")));

        Assert.True(translated.IsError);
        var text = Assert.IsType<TextContentBlock>(translated.Content.Single());
        using var doc = JsonDocument.Parse(text.Text);
        Assert.Equal("invalid_argument", doc.RootElement.GetProperty("code").GetString());
    }
}
