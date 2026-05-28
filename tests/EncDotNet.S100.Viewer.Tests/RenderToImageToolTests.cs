using System;
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

public class RenderToImageToolTests
{
    private static readonly byte[] PngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] StubPng = MakeStubPng();

    private static byte[] MakeStubPng()
    {
        // Caller doesn't care that this isn't a real PNG body; only the
        // signature is checked.
        var b = new byte[16];
        Array.Copy(PngSignature, b, PngSignature.Length);
        return b;
    }

    private sealed class FakeMapHost : IMapHost
    {
        public int ObservedWidth, ObservedHeight;
        public double ObservedDensity;
        public byte[]? Result = StubPng;
        public Exception? Throw;

        public void AddLayer(ILayer layer) { }
        public void RemoveLayer(ILayer layer) { }
        public void AddOverlayLayer(ILayer layer) { }
        public void RemoveOverlayLayer(ILayer layer) { }
        public void ReorderDatasetLayers(System.Collections.Generic.IReadOnlyList<ILayer> orderedDatasetLayers) { }
        public void ZoomToExtent(MRect extent) { }

        public Task<byte[]?> RenderCurrentViewToPngAsync(int widthPx, int heightPx, double pixelDensity, CancellationToken ct = default)
        {
            ObservedWidth = widthPx;
            ObservedHeight = heightPx;
            ObservedDensity = pixelDensity;
            if (Throw is not null) throw Throw;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeAccessor : IMapHostAccessor
    {
        public IMapHost? Current { get; set; }
    }

    [Fact]
    public async Task Applies_defaults_when_request_is_blank()
    {
        var host = new FakeMapHost();
        var accessor = new FakeAccessor { Current = host };
        var tool = new RenderToImageTool(accessor);

        var result = await tool.InvokeAsync(new RenderToImageRequest());

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal(1024, ok!.Width);
        Assert.Equal(768, ok.Height);
        Assert.Equal(1.0, ok.PixelDensity);
        Assert.Equal("png", ok.ImageFormat);
        Assert.Same(StubPng, ok.ImageBytes);
        Assert.Equal(1024, host.ObservedWidth);
        Assert.Equal(768, host.ObservedHeight);
        Assert.Equal(1.0, host.ObservedDensity);
        Assert.NotNull(ok.Notes); // defaulted dimensions/density
    }

    [Theory]
    [InlineData(32, 64)]
    [InlineData(8192, 4096)]
    [InlineData(64, 64)]
    [InlineData(4096, 4096)]
    [InlineData(1000, 1000)]
    public async Task Clamps_dimensions(int requested, int expected)
    {
        var host = new FakeMapHost();
        var tool = new RenderToImageTool(new FakeAccessor { Current = host });
        var r = await tool.InvokeAsync(new RenderToImageRequest(Width: requested, Height: requested, PixelDensity: 1.0));
        Assert.True(r.TryGetValue(out var ok));
        Assert.Equal(expected, ok!.Width);
        Assert.Equal(expected, ok.Height);
    }

    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(5.0, 3.0)]
    [InlineData(1.5, 1.5)]
    public async Task Clamps_pixel_density(double requested, double expected)
    {
        var host = new FakeMapHost();
        var tool = new RenderToImageTool(new FakeAccessor { Current = host });
        var r = await tool.InvokeAsync(new RenderToImageRequest(PixelDensity: requested));
        Assert.True(r.TryGetValue(out var ok));
        Assert.Equal(expected, ok!.PixelDensity);
    }

    [Fact]
    public async Task NaN_density_returns_invalid_argument()
    {
        var tool = new RenderToImageTool(new FakeAccessor { Current = new FakeMapHost() });
        var r = await tool.InvokeAsync(new RenderToImageRequest(PixelDensity: double.NaN));
        Assert.True(r.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Missing_host_returns_map_not_ready()
    {
        var tool = new RenderToImageTool(new FakeAccessor { Current = null });
        var r = await tool.InvokeAsync(new RenderToImageRequest());
        Assert.True(r.TryGetError(out var err));
        Assert.IsType<MapNotReady>(err);
    }

    [Fact]
    public async Task Null_bytes_returns_map_not_ready()
    {
        var host = new FakeMapHost { Result = null };
        var tool = new RenderToImageTool(new FakeAccessor { Current = host });
        var r = await tool.InvokeAsync(new RenderToImageRequest());
        Assert.True(r.TryGetError(out var err));
        Assert.IsType<MapNotReady>(err);
    }

    [Fact]
    public async Task Host_exception_returns_map_not_ready()
    {
        var host = new FakeMapHost { Throw = new InvalidOperationException("no map") };
        var tool = new RenderToImageTool(new FakeAccessor { Current = host });
        var r = await tool.InvokeAsync(new RenderToImageRequest());
        Assert.True(r.TryGetError(out var err));
        Assert.IsType<MapNotReady>(err);
    }

    [Fact]
    public void Adapter_produces_image_content_block_first()
    {
        var host = new FakeMapHost();
        var tool = new RenderToImageTool(new FakeAccessor { Current = host });
        var mcpTool = RenderToImageMcpAdapter.Create(tool);
        Assert.Equal("render_to_image", mcpTool.ProtocolTool.Name);
    }

    [Fact]
    public void Adapter_success_emits_image_then_text()
    {
        var ok = ToolResult<RenderToImageResult>.Ok(new RenderToImageResult(
            Width: 256, Height: 256, PixelDensity: 1.0,
            ImageFormat: "png", ImageBytes: StubPng, Notes: null));
        var call = RenderToImageMcpAdapter.TranslateResult(ok);
        Assert.False(call.IsError);
        Assert.Collection(call.Content,
            c => Assert.IsType<ImageContentBlock>(c),
            c => Assert.IsType<TextContentBlock>(c));
        var img = (ImageContentBlock)call.Content[0];
        Assert.Equal("image/png", img.MimeType);
    }

    [Fact]
    public void Adapter_error_emits_structured_text()
    {
        var err = ToolResult<RenderToImageResult>.Err(new MapNotReady("no map"));
        var call = RenderToImageMcpAdapter.TranslateResult(err);
        Assert.True(call.IsError);
        var single = Assert.Single(call.Content);
        var text = Assert.IsType<TextContentBlock>(single);
        Assert.Contains("map_not_ready", text.Text);
    }
}
