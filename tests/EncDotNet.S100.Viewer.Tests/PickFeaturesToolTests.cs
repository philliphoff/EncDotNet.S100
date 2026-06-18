using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.DynamicSources;
using Mapsui;
using Mapsui.Layers;
using ModelContextProtocol.Protocol;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class PickFeaturesToolTests
{
    private sealed class FakeAccessor : IMapHostAccessor
    {
        public IMapHost? Current { get; set; }
    }

    private sealed class FakeCatalog : IDatasetCatalog
    {
        public ImmutableArray<LoadedDataset> Datasets { get; set; } = ImmutableArray<LoadedDataset>.Empty;
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
        host.ScreenToWgs84 = (x, y) => (47.6, -122.3);

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
        host.ScreenToWgs84 = (_, _) => (1, 2);

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
        host.ScreenToWgs84 = (_, _) => (1, 2);

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
            "geo", 47.6, -122.3, ImmutableArray<IdentifyMatch>.Empty, 0, false);

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
