using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Result of <see cref="CaptureAppScreenshotTool"/>.</summary>
[Description("Result of capture_app_screenshot: image dimensions plus PNG bytes; agents receive the image as a separate MCP ImageContentBlock alongside this JSON metadata.")]
internal sealed record CaptureAppScreenshotResult(
    [property: Description("Captured image width in pixels (decoded from the PNG header), or 0 when unknown.")] int Width,
    [property: Description("Captured image height in pixels (decoded from the PNG header), or 0 when unknown.")] int Height,
    [property: Description("Image format identifier; always \"png\" in v1.")] string ImageFormat,
    [property: Description("PNG-encoded image bytes; surfaced separately as a MCP ImageContentBlock with mimeType image/png at the wire layer.")] byte[] ImageBytes);

/// <summary>
/// Captures a PNG snapshot of the whole viewer application window —
/// the chart <em>plus</em> the surrounding chrome (activity docks,
/// panels, timeline, status bar) — so MCP agents can visually verify
/// non-rendering UX changes (e.g. that <see cref="SetPanelTool"/>
/// actually opened a panel). This complements
/// <see cref="RenderToImageTool"/>, which captures only the map surface.
/// </summary>
/// <remarks>
/// <para>
/// The capture is delegated to <see cref="IAppScreenshotProvider"/>,
/// which renders the live <c>MainWindow</c> on the UI thread. When the
/// window has not been attached yet (or has no on-screen size) the tool
/// returns a <see cref="WindowNotReady"/> error rather than throwing.
/// </para>
/// <para>
/// Read-only and side-effect free: the window is not mutated. Like
/// <see cref="RenderToImageTool"/> this is a viewer-injected tool — the
/// catalog-only MCP surface has no window dependency, so a headless MCP
/// host would need to supply its own equivalent.
/// </para>
/// </remarks>
internal sealed class CaptureAppScreenshotTool
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "capture_app_screenshot";

    private readonly IAppScreenshotProvider _provider;

    /// <summary>Creates a new <see cref="CaptureAppScreenshotTool"/>.</summary>
    public CaptureAppScreenshotTool(IAppScreenshotProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <summary>Executes the tool.</summary>
    public async Task<ToolResult<CaptureAppScreenshotResult>> InvokeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[]? bytes;
        try
        {
            bytes = await _provider.CapturePngAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult<CaptureAppScreenshotResult>.Err(
                new WindowNotReady($"capture failed: {ex.GetType().Name}: {ex.Message}"));
        }

        if (bytes is null || bytes.Length == 0)
        {
            return ToolResult<CaptureAppScreenshotResult>.Err(
                new WindowNotReady("the application window is not attached or has no on-screen size yet"));
        }

        var (width, height) = TryReadPngSize(bytes);
        return ToolResult<CaptureAppScreenshotResult>.Ok(
            new CaptureAppScreenshotResult(width, height, "png", bytes));
    }

    /// <summary>
    /// Reads the pixel dimensions from a PNG's IHDR chunk without decoding
    /// the image. Returns (0, 0) when the bytes are not a recognisable PNG.
    /// </summary>
    private static (int Width, int Height) TryReadPngSize(byte[] bytes)
    {
        // 8-byte signature, 4-byte length, 4-byte "IHDR", then width/height
        // (each a big-endian uint32) at offsets 16 and 20.
        const int WidthOffset = 16;
        const int HeightOffset = 20;
        if (bytes.Length < HeightOffset + 4)
        {
            return (0, 0);
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(WidthOffset, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(HeightOffset, 4));
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
        {
            return (0, 0);
        }

        return ((int)width, (int)height);
    }
}
