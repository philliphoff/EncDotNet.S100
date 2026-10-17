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

    public double? LastScale { get; private set; }

    public Avalonia.Controls.Control? Target { get; set; }

    public Task<byte[]?> CapturePngAsync(CancellationToken cancellationToken = default) =>
        CapturePngAsync(1.0, cancellationToken);

    public AppScreenshotRegion? LastRegion { get; private set; }

    /// <summary>Regions the fake reports as not showing (it returns null for them).</summary>
    public HashSet<AppScreenshotRegion> Hidden { get; } = [];

    public Task<byte[]?> CapturePngAsync(double scale, CancellationToken cancellationToken = default) =>
        CapturePngAsync(scale, AppScreenshotRegion.Window, cancellationToken);

    public Task<byte[]?> CapturePngAsync(double scale, AppScreenshotRegion region, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastScale = scale;
        LastRegion = region;
        if (_throw is not null)
        {
            throw _throw;
        }

        return Task.FromResult(Hidden.Contains(region) ? null : _png);
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

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(1, provider.Calls);
        Assert.Equal("png", value!.ImageFormat);
        Assert.Same(png, value.ImageBytes);
        Assert.Equal(1600, value.Width);
        Assert.Equal(1000, value.Height);
    }

    [Theory]
    [InlineData(null, 1.0)]
    [InlineData(2.0, 2.0)]
    [InlineData(0.1, CaptureAppScreenshotTool.MinScale)]
    [InlineData(10.0, CaptureAppScreenshotTool.MaxScale)]
    [InlineData(double.NaN, 1.0)]
    public async Task Invoke_passes_the_clamped_scale_to_the_provider(double? scale, double expected)
    {
        var provider = new FakeAppScreenshotProvider(FakeAppScreenshotProvider.MakePng(2200, 1400));
        var tool = new CaptureAppScreenshotTool(provider);

        var result = await tool.InvokeAsync(scale, TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out _));
        Assert.Equal(expected, provider.LastScale);
    }

    [Fact]
    public async Task Invoke_returns_window_not_ready_when_provider_yields_null()
    {
        var provider = new FakeAppScreenshotProvider(png: null);
        var tool = new CaptureAppScreenshotTool(provider);

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

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

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<WindowNotReady>(err);
    }

    [Fact]
    public async Task Invoke_wraps_provider_exception_as_window_not_ready()
    {
        var provider = new FakeAppScreenshotProvider(toThrow: new InvalidOperationException("boom"));
        var tool = new CaptureAppScreenshotTool(provider);

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

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

        var result = await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.InvokeAsync(cancellationToken: cts.Token));
    }

    private static FakeUiControllerAccessor Panels(params ViewerPanelState[] panels) =>
        new() { Current = new FakeViewerUiController(panels) };

    private static ViewerPanelState Panel(string id, string dock, bool showing) =>
        new(id, id, dock, Available: true, Selected: showing, DockOpen: showing, Showing: showing);

    [Theory]
    [InlineData("Datasets", "LeftDock")]
    [InlineData("pickreport", "RightDock")]
    [InlineData("Timeline", "BottomDock")]
    [InlineData("MAP", "Map")]
    public async Task Panel_crops_to_the_dock_showing_it(string panel, string region)
    {
        var expected = Enum.Parse<AppScreenshotRegion>(region);
        var provider = new FakeAppScreenshotProvider(FakeAppScreenshotProvider.MakePng(320, 700));
        var tool = new CaptureAppScreenshotTool(provider, Panels(
            Panel("Datasets", "Left", showing: true),
            Panel("PickReport", "Right", showing: true),
            Panel("Timeline", "Bottom", showing: true)));

        var result = await tool.InvokeAsync(2, panel, TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(320, value!.Width);
        Assert.Equal(expected, provider.LastRegion);
        Assert.Equal(2, provider.LastScale);
    }

    [Fact]
    public async Task Panel_reports_unknown_and_hidden_panels()
    {
        var provider = new FakeAppScreenshotProvider(FakeAppScreenshotProvider.MakePng(10, 10));
        var tool = new CaptureAppScreenshotTool(provider, Panels(Panel("Library", "Left", showing: false)));

        Assert.True((await tool.InvokeAsync(null, "Nope", TestContext.Current.CancellationToken)).TryGetError(out var unknown));
        Assert.Equal("panel_not_found", unknown!.Code);
        Assert.True((await tool.InvokeAsync(null, "Library", TestContext.Current.CancellationToken)).TryGetError(out var hidden));
        Assert.Equal("panel_not_showing", hidden!.Code);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Panel_reports_a_dock_the_window_does_not_show_as_not_showing()
    {
        var provider = new FakeAppScreenshotProvider(FakeAppScreenshotProvider.MakePng(10, 10));
        provider.Hidden.Add(AppScreenshotRegion.RightDock);
        var tool = new CaptureAppScreenshotTool(provider, Panels(Panel("PickReport", "Right", showing: true)));

        Assert.True((await tool.InvokeAsync(null, "PickReport", TestContext.Current.CancellationToken)).TryGetError(out var err));
        Assert.Equal("panel_not_showing", err!.Code);
    }

    [Fact]
    public async Task Map_needs_no_panel_controller_but_other_panels_do()
    {
        var provider = new FakeAppScreenshotProvider(FakeAppScreenshotProvider.MakePng(10, 10));
        var tool = new CaptureAppScreenshotTool(provider);

        Assert.True((await tool.InvokeAsync(null, "map", TestContext.Current.CancellationToken)).TryGetValue(out _));
        Assert.True((await tool.InvokeAsync(null, "Datasets", TestContext.Current.CancellationToken)).TryGetError(out var err));
        Assert.Equal("ui_not_ready", err!.Code);
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
