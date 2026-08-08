using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Mutable;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Verifies <see cref="RenderToImageTool"/> resolves size/density and drives the
/// shared <see cref="IImageRenderer"/> (#560).
/// </summary>
public class RenderToImageToolTests
{
    private static readonly byte[] FakePng = { 1, 2, 3, 4 };

    [Fact]
    public async Task Defaults_To1024x768AtDensity1()
    {
        var host = new FakeRenderer(FakePng);
        var tool = new RenderToImageTool(Accessor(host));

        var value = AssertOk(await tool.InvokeAsync(new RenderToImageRequest()));

        Assert.Equal(1024, value.Width);
        Assert.Equal(768, value.Height);
        Assert.Equal(1.0, value.PixelDensity);
        Assert.Equal("png", value.ImageFormat);
        Assert.Equal(FakePng, value.ImageBytes);
        Assert.Equal((1024, 768, 1.0), host.LastCall);
        Assert.Contains("defaulted size", value.Notes);
    }

    [Fact]
    public async Task PassesThroughExplicitDimensions()
    {
        var host = new FakeRenderer(FakePng);

        var value = AssertOk(await new RenderToImageTool(Accessor(host))
            .InvokeAsync(new RenderToImageRequest(Width: 640, Height: 480, PixelDensity: 2.0)));

        Assert.Equal(640, value.Width);
        Assert.Equal(480, value.Height);
        Assert.Equal(2.0, value.PixelDensity);
        Assert.Null(value.Notes);
        Assert.Equal((640, 480, 2.0), host.LastCall);
    }

    [Fact]
    public async Task ClampsOutOfRangeDimensionsAndDensity()
    {
        var host = new FakeRenderer(FakePng);

        var value = AssertOk(await new RenderToImageTool(Accessor(host))
            .InvokeAsync(new RenderToImageRequest(Width: 10, Height: 99999, PixelDensity: 9.0)));

        Assert.Equal(64, value.Width);
        Assert.Equal(4096, value.Height);
        Assert.Equal(3.0, value.PixelDensity);
        Assert.Contains("clamped dimensions", value.Notes);
        Assert.Contains("clamped pixelDensity", value.Notes);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task NonFinitePixelDensity_IsInvalidArgument(double density)
    {
        var host = new FakeRenderer(FakePng);

        var error = Assert.IsType<InvalidArgument>(
            AssertErr(await new RenderToImageTool(Accessor(host))
                .InvokeAsync(new RenderToImageRequest(PixelDensity: density))));
        Assert.Equal(nameof(RenderToImageRequest.PixelDensity), error.Parameter);
        Assert.Null(host.LastCall);
    }

    [Fact]
    public async Task EmptyRender_IsHostNotReady()
    {
        var host = new FakeRenderer(Array.Empty<byte>());

        Assert.IsType<HostNotReady>(
            AssertErr(await new RenderToImageTool(Accessor(host))
                .InvokeAsync(new RenderToImageRequest())));
    }

    [Fact]
    public async Task RendererUnattached_IsHostNotReady()
    {
        var tool = new RenderToImageTool(new NullCapabilityAccessor<IImageRenderer>());

        Assert.IsType<HostNotReady>(
            AssertErr(await tool.InvokeAsync(new RenderToImageRequest())));
    }

    private static ICapabilityAccessor<IImageRenderer> Accessor(IImageRenderer r)
        => new StaticCapabilityAccessor<IImageRenderer>(r);

    private static TValue AssertOk<TValue>(ToolResult<TValue> result)
    {
        Assert.True(result.TryGetValue(out var value), "expected a success result");
        return value!;
    }

    private static ToolError AssertErr<TValue>(ToolResult<TValue> result)
    {
        Assert.True(result.TryGetError(out var error), "expected an error result");
        return error!;
    }

    private sealed class NullCapabilityAccessor<T> : ICapabilityAccessor<T> where T : class
    {
        public T? Current => null;
    }

    private sealed class FakeRenderer(byte[] png) : IImageRenderer
    {
        public (int Width, int Height, double Density)? LastCall { get; private set; }

        public Task<byte[]?> RenderToPngAsync(
            int widthPx, int heightPx, double pixelDensity, CancellationToken cancellationToken = default)
        {
            LastCall = (widthPx, heightPx, pixelDensity);
            return Task.FromResult<byte[]?>(png);
        }
    }
}
