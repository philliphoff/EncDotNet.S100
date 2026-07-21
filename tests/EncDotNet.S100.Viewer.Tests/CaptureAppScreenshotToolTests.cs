using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Fake <see cref="IAppScreenshotProvider"/> that serves a canned PNG (or
/// null / an exception) so <see cref="CaptureAppScreenshotTool"/> can be
/// exercised without an Avalonia UI thread or a live window.
/// </summary>
internal sealed class FakeAppScreenshotProvider : IAppScreenshotProvider
{
    private readonly byte[]? _png;
    private readonly Exception? _throw;

    public FakeAppScreenshotProvider(byte[]? png = null, Exception? toThrow = null)
    {
        _png = png;
        _throw = toThrow;
    }

    public int Calls { get; private set; }

    public Avalonia.Controls.Control? Target { get; set; }

    public Task<byte[]?> CapturePngAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        if (_throw is not null)
        {
            throw _throw;
        }

        return Task.FromResult(_png);
    }

    /// <summary>Builds a minimal but valid PNG header carrying the given dimensions.</summary>
    public static byte[] MakePng(int width, int height)
    {
        var bytes = new byte[24];
        // 8-byte PNG signature.
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(sig, bytes, 8);
        // IHDR length (13) at 8..11, "IHDR" at 12..15.
        bytes[11] = 13;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);
        return bytes;
    }
}

public class CaptureAppScreenshotToolTests
{
    [Fact]
    public async Task Invoke_returns_png_bytes_and_decoded_dimensions()
    {
        var png = FakeAppScreenshotProvider.MakePng(1600, 1000);
        var provider = new FakeAppScreenshotProvider(png);
        var tool = new CaptureAppScreenshotTool(provider);

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(1, provider.Calls);
        Assert.Equal("png", value!.ImageFormat);
        Assert.Same(png, value.ImageBytes);
        Assert.Equal(1600, value.Width);
        Assert.Equal(1000, value.Height);
    }

    [Fact]
    public async Task Invoke_returns_window_not_ready_when_provider_yields_null()
    {
        var provider = new FakeAppScreenshotProvider(png: null);
        var tool = new CaptureAppScreenshotTool(provider);

        var result = await tool.InvokeAsync();

        Assert.False(result.TryGetValue(out _));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<WindowNotReady>(err);
        Assert.Equal("window_not_ready", err!.Code);
    }

    [Fact]
    public async Task Invoke_returns_window_not_ready_when_provider_yields_empty()
    {
        var provider = new FakeAppScreenshotProvider(png: Array.Empty<byte>());
        var tool = new CaptureAppScreenshotTool(provider);

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<WindowNotReady>(err);
    }

    [Fact]
    public async Task Invoke_wraps_provider_exception_as_window_not_ready()
    {
        var provider = new FakeAppScreenshotProvider(toThrow: new InvalidOperationException("boom"));
        var tool = new CaptureAppScreenshotTool(provider);

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<WindowNotReady>(err);
        Assert.Contains("boom", err!.Message);
    }

    [Theory]
    [InlineData(4)]   // too short to even reach the offsets
    [InlineData(64)]  // long enough to read offsets, but not a PNG
    public async Task Invoke_omits_dimensions_when_bytes_are_not_a_png(int length)
    {
        // A non-PNG buffer with non-zero bytes at the width/height offsets:
        // without signature validation this would yield bogus dimensions.
        var junk = new byte[length];
        for (var i = 0; i < junk.Length; i++)
        {
            junk[i] = 0x7F;
        }

        var provider = new FakeAppScreenshotProvider(png: junk);
        var tool = new CaptureAppScreenshotTool(provider);

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(0, value!.Width);
        Assert.Equal(0, value.Height);
    }

    [Fact]
    public async Task Invoke_honours_cancellation()
    {
        var provider = new FakeAppScreenshotProvider(FakeAppScreenshotProvider.MakePng(10, 10));
        var tool = new CaptureAppScreenshotTool(provider);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.InvokeAsync(cts.Token));
    }

    [Fact]
    public void Constructor_rejects_null_provider()
    {
        Assert.Throws<ArgumentNullException>(() => new CaptureAppScreenshotTool(null!));
    }
}

public class CaptureAppScreenshotMcpAdapterTests
{
    [Fact]
    public void Success_surfaces_image_block_then_metadata()
    {
        var png = FakeAppScreenshotProvider.MakePng(800, 600);
        var ok = ToolResult<CaptureAppScreenshotResult>.Ok(
            new CaptureAppScreenshotResult(800, 600, "png", png));

        var call = CaptureAppScreenshotMcpAdapter.TranslateResult(ok);

        Assert.False(call.IsError);
        Assert.Equal(2, call.Content.Count);
        var image = Assert.IsType<ImageContentBlock>(call.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        var text = Assert.IsType<TextContentBlock>(call.Content[1]);
        Assert.Contains("\"width\":800", text.Text);
        Assert.Contains("\"height\":600", text.Text);
        Assert.Contains("\"imageFormat\":\"png\"", text.Text);
    }

    [Fact]
    public void Failure_surfaces_error_code_only_as_text()
    {
        var err = ToolResult<CaptureAppScreenshotResult>.Err(new WindowNotReady("no window"));

        var call = CaptureAppScreenshotMcpAdapter.TranslateResult(err);

        Assert.True(call.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(call.Content));
        Assert.Contains("window_not_ready", text.Text);
    }
}
