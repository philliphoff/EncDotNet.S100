using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using Mapsui;
using Mapsui.Layers;
using ModelContextProtocol.Protocol;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class SetViewportToolTests
{
    private sealed record ExtentCall(double MinX, double MinY, double MaxX, double MaxY);
    private sealed record CenterCall(double X, double Y, double Resolution);

    private sealed class RecordingMapHost : IMapHost
    {
        public List<ExtentCall> ExtentCalls { get; } = new();
        public List<CenterCall> CenterCalls { get; } = new();

        public void AddLayer(ILayer layer) { }
        public void RemoveLayer(ILayer layer) { }
        public void AddOverlayLayer(ILayer layer) { }
        public void RemoveOverlayLayer(ILayer layer) { }
        public void ReorderDatasetLayers(IReadOnlyList<ILayer> orderedDatasetLayers) { }
        public void ZoomToExtent(MRect extent) { }

        public void SetViewportToExtent(MRect mercatorExtent)
            => ExtentCalls.Add(new ExtentCall(
                mercatorExtent.MinX, mercatorExtent.MinY,
                mercatorExtent.MaxX, mercatorExtent.MaxY));

        public void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution)
            => CenterCalls.Add(new CenterCall(mercatorCenter.X, mercatorCenter.Y, resolution));

        public Task<byte[]?> RenderCurrentViewToPngAsync(int widthPx, int heightPx, double pixelDensity, CancellationToken ct = default)
            => Task.FromResult<byte[]?>(null);
    }

    private sealed class FakeAccessor : IMapHostAccessor
    {
        public IMapHost? Current { get; set; }
    }

    private static (SetViewportTool tool, RecordingMapHost host) Make(IMapHost? hostOverride = null)
    {
        var host = (hostOverride as RecordingMapHost) ?? new RecordingMapHost();
        var accessor = new FakeAccessor { Current = hostOverride is null ? host : hostOverride };
        return (new SetViewportTool(accessor), host);
    }

    [Fact]
    public async Task Bbox_form_projects_and_calls_extent()
    {
        var (tool, host) = Make();

        var result = await tool.InvokeAsync(new SetViewportRequest(
            South: 50.40, West: -3.66, North: 50.50, East: -3.50));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal("bbox", ok!.Mode);
        Assert.Equal(50.40, ok.South, 6);
        Assert.Equal(-3.66, ok.West, 6);
        Assert.Equal(50.50, ok.North, 6);
        Assert.Equal(-3.50, ok.East, 6);
        Assert.Single(host.ExtentCalls);
        Assert.Empty(host.CenterCalls);
        var call = host.ExtentCalls[0];
        // Coarse sanity: bbox roughly around south-west UK in Mercator.
        Assert.True(call.MinX < call.MaxX);
        Assert.True(call.MinY < call.MaxY);
    }

    [Fact]
    public async Task Center_zoom_form_projects_and_calls_center()
    {
        var (tool, host) = Make();

        var result = await tool.InvokeAsync(new SetViewportRequest(
            CenterLat: 50.45, CenterLon: -3.58, Zoom: 12));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal("center", ok!.Mode);
        // Echoed bbox brackets the centre at z=12 (1024×768 reference frame ≈ 0.4° × 0.2° at this latitude).
        Assert.InRange(ok.South, 50.0, 50.45);
        Assert.InRange(ok.North, 50.45, 50.9);
        Assert.InRange(ok.West, -4.5, -3.58);
        Assert.InRange(ok.East, -3.58, -2.5);
        Assert.Empty(host.ExtentCalls);
        Assert.Single(host.CenterCalls);
        // Resolution at zoom 12: 156543.0339... / 4096 ≈ 38.22 m/px.
        Assert.Equal(SetViewportTool.ResolutionAtZoomZero / 4096.0, host.CenterCalls[0].Resolution, 6);
    }

    [Fact]
    public async Task Empty_request_is_rejected()
    {
        var (tool, _) = Make();
        var result = await tool.InvokeAsync(new SetViewportRequest());
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Mixing_bbox_and_center_is_rejected()
    {
        var (tool, _) = Make();
        var result = await tool.InvokeAsync(new SetViewportRequest(
            South: 50, West: -4, North: 51, East: -3,
            CenterLat: 50.5, CenterLon: -3.5, Zoom: 12));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Partial_bbox_is_rejected()
    {
        var (tool, _) = Make();
        var result = await tool.InvokeAsync(new SetViewportRequest(South: 50, West: -4));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Partial_center_zoom_is_rejected()
    {
        var (tool, _) = Make();
        var result = await tool.InvokeAsync(new SetViewportRequest(CenterLat: 50, CenterLon: -3));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Theory]
    [InlineData(-91.0, -3.66, 50.50, -3.50)] // south < -90
    [InlineData(50.40, -181.0, 50.50, -3.50)] // west < -180
    [InlineData(50.40, -3.66, 91.0, -3.50)] // north > 90
    [InlineData(50.40, -3.66, 50.50, 181.0)] // east > 180
    public async Task Out_of_range_bbox_edge_is_rejected(
        double south, double west, double north, double east)
    {
        var (tool, _) = Make();
        var result = await tool.InvokeAsync(new SetViewportRequest(south, west, north, east));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Theory]
    [InlineData(50.50, -3.50, 50.40, -3.66)] // south >= north
    [InlineData(50.40, -3.50, 50.50, -3.66)] // west >= east (no antimeridian)
    public async Task Inverted_bbox_is_rejected_with_geometry_invalid(
        double south, double west, double north, double east)
    {
        var (tool, _) = Make();
        var result = await tool.InvokeAsync(new SetViewportRequest(south, west, north, east));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<GeometryInvalid>(err);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(24.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task Out_of_range_zoom_is_rejected(double zoom)
    {
        var (tool, _) = Make();
        var result = await tool.InvokeAsync(new SetViewportRequest(
            CenterLat: 50, CenterLon: -3, Zoom: zoom));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Map_not_ready_when_accessor_returns_null()
    {
        var accessor = new FakeAccessor { Current = null };
        var tool = new SetViewportTool(accessor);

        var result = await tool.InvokeAsync(new SetViewportRequest(
            South: 50, West: -4, North: 51, East: -3));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<MapNotReady>(err);
    }

    [Fact]
    public void Adapter_translates_success_to_text_block()
    {
        var ok = ToolResult<SetViewportResult>.Ok(new SetViewportResult("bbox", 50, -4, 51, -3));
        var call = SetViewportMcpAdapter.TranslateResult(ok);
        Assert.False(call.IsError);
        var single = Assert.Single(call.Content);
        var text = Assert.IsType<TextContentBlock>(single);
        Assert.Contains("\"mode\":\"bbox\"", text.Text);
        Assert.Contains("\"south\":50", text.Text);
    }

    [Fact]
    public void Adapter_translates_error_to_error_payload()
    {
        var err = ToolResult<SetViewportResult>.Err(new InvalidArgument("zoom", "out of range"));
        var call = SetViewportMcpAdapter.TranslateResult(err);
        Assert.True(call.IsError);
        var single = Assert.Single(call.Content);
        var text = Assert.IsType<TextContentBlock>(single);
        Assert.Contains("\"code\":\"invalid_argument\"", text.Text);
    }
}
