using System.Buffers.Binary;
using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Result of <see cref="CaptureAppScreenshotTool"/>.</summary>
[Description("Result of capture_app_screenshot: image dimensions plus PNG bytes; agents receive the image as a separate MCP ImageContentBlock alongside this JSON metadata.")]
internal sealed record CaptureAppScreenshotResult(
    [property: Description("Captured image width in pixels (decoded from the PNG header), or 0 when unknown.")] int Width,
    [property: Description("Captured image height in pixels (decoded from the PNG header), or 0 when unknown.")] int Height,
    [property: Description("Image format identifier; always \"png\" in v1.")] string ImageFormat,
    [property: Description("PNG-encoded image bytes; surfaced separately as a MCP ImageContentBlock with mimeType image/png at the wire layer.")] byte[] ImageBytes,
    [property: Description("Id of the display combination the map shows (the View pill), as list_display_combinations reports it; null for Custom.")] string? DisplayCombinationId = null,
    [property: Description("True when a setting the display combination covers has changed since it was applied ('Pilotage (modified)').")] bool DisplayCombinationModified = false);

/// <summary>
/// Captures a PNG snapshot of the whole viewer application window —
/// the chart <em>plus</em> the surrounding chrome (activity docks,
/// panels, timeline, status bar) — so MCP agents can visually verify
/// non-rendering UX changes (e.g. that <see cref="SetPanelTool"/>
/// actually opened a panel). This complements the <c>render_to_image</c>
/// tool, which captures only the map surface.
/// </summary>
/// <remarks>
/// <para>
/// The capture is delegated to <see cref="IAppScreenshotProvider"/>,
/// which renders the live <c>MainWindow</c> on the UI thread. When the
/// window has not been attached yet (or has no on-screen size) the tool
/// returns a <see cref="WindowNotReady"/> error rather than throwing.
/// </para>
/// <para>
/// Read-only and side-effect free: the window is not mutated. This is a
/// viewer-injected tool — the shared MCP surface has no window dependency,
/// so a headless MCP host would need to supply its own equivalent.
/// </para>
/// </remarks>
internal sealed class CaptureAppScreenshotTool
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "capture_app_screenshot";

    /// <summary>Smallest accepted <c>scale</c>; lower values are clamped up.</summary>
    public const double MinScale = 0.5;

    /// <summary>Largest accepted <c>scale</c>; higher values are clamped down.</summary>
    public const double MaxScale = 3.0;

    /// <summary>The <c>panel</c> value that crops to the map.</summary>
    public const string MapPanel = "map";

    private readonly IAppScreenshotProvider _provider;
    private readonly IViewerUiControllerAccessor? _panels;
    private readonly Func<(string? Id, bool Modified)>? _displayCombination;

    /// <summary>Creates a new <see cref="CaptureAppScreenshotTool"/>.</summary>
    /// <param name="provider">Renders the window, or one region of it.</param>
    /// <param name="panels">Resolves a panel id to its dock for the <c>panel</c> crop; null accepts only 'map'.</param>
    /// <param name="displayCombination">The active display combination's id and modified marker, recorded with each capture; null records none.</param>
    public CaptureAppScreenshotTool(
        IAppScreenshotProvider provider,
        IViewerUiControllerAccessor? panels = null,
        Func<(string? Id, bool Modified)>? displayCombination = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _panels = panels;
        _displayCombination = displayCombination;
    }

    /// <summary>Executes the tool for the whole window.</summary>
    /// <param name="scale">
    /// Device pixels per logical pixel (2 = Retina-sharp). Null captures at 1;
    /// other values are clamped to [<see cref="MinScale"/>, <see cref="MaxScale"/>].
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public Task<ToolResult<CaptureAppScreenshotResult>> InvokeAsync(
        double? scale = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(scale, panel: null, cancellationToken);

    /// <summary>Executes the tool.</summary>
    /// <param name="scale">
    /// Device pixels per logical pixel (2 = Retina-sharp). Null captures at 1;
    /// other values are clamped to [<see cref="MinScale"/>, <see cref="MaxScale"/>].
    /// </param>
    /// <param name="panel">
    /// Crops to one panel: a panel id from <c>list_panels</c> (its dock, while it
    /// is showing) or <see cref="MapPanel"/>; null captures the whole window.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public async Task<ToolResult<CaptureAppScreenshotResult>> InvokeAsync(
        double? scale,
        string? panel,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var region = AppScreenshotRegion.Window;
        if (!string.IsNullOrWhiteSpace(panel))
        {
            var resolved = await ResolveRegionAsync(panel.Trim(), cancellationToken).ConfigureAwait(false);
            if (!resolved.TryGetValue(out region))
            {
                resolved.TryGetError(out var error);
                return ToolResult<CaptureAppScreenshotResult>.Err(error!);
            }
        }

        var effectiveScale = scale is { } s && double.IsFinite(s) ? Math.Clamp(s, MinScale, MaxScale) : 1.0;
        byte[]? bytes;
        try
        {
            bytes = region == AppScreenshotRegion.Window
                ? await _provider.CapturePngAsync(effectiveScale, cancellationToken).ConfigureAwait(false)
                : await _provider.CapturePngAsync(effectiveScale, region, cancellationToken).ConfigureAwait(false);
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

        if ((bytes is null || bytes.Length == 0) && region != AppScreenshotRegion.Window && region != AppScreenshotRegion.Map)
        {
            return ToolResult<CaptureAppScreenshotResult>.Err(new PanelNotShowing(panel!.Trim()));
        }

        if (bytes is null || bytes.Length == 0)
        {
            return ToolResult<CaptureAppScreenshotResult>.Err(
                new WindowNotReady("the application window is not attached or has no on-screen size yet"));
        }

        var (width, height) = TryReadPngSize(bytes);
        var (combinationId, modified) = _displayCombination?.Invoke() ?? (null, false);
        return ToolResult<CaptureAppScreenshotResult>.Ok(
            new CaptureAppScreenshotResult(width, height, "png", bytes, combinationId, modified));
    }

    /// <summary>Maps a <c>panel</c> value to the region that shows it.</summary>
    private async Task<ToolResult<AppScreenshotRegion>> ResolveRegionAsync(string panel, CancellationToken ct)
    {
        if (string.Equals(panel, MapPanel, StringComparison.OrdinalIgnoreCase))
            return ToolResult<AppScreenshotRegion>.Ok(AppScreenshotRegion.Map);
        if (_panels?.Current is not { } controller)
            return ToolResult<AppScreenshotRegion>.Err(new UiNotReady("the viewer's UI controller has not been initialised yet"));

        var panels = await controller.GetPanelsAsync(ct).ConfigureAwait(false);
        var index = panels.ToList().FindIndex(p => string.Equals(p.Id, panel, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return ToolResult<AppScreenshotRegion>.Err(new PanelNotFound(panel));
        var state = panels[index];
        if (!state.Showing)
            return ToolResult<AppScreenshotRegion>.Err(new PanelNotShowing(state.Id));
        return state.Dock switch
        {
            "Left" => ToolResult<AppScreenshotRegion>.Ok(AppScreenshotRegion.LeftDock),
            "Right" => ToolResult<AppScreenshotRegion>.Ok(AppScreenshotRegion.RightDock),
            "Bottom" => ToolResult<AppScreenshotRegion>.Ok(AppScreenshotRegion.BottomDock),
            _ => ToolResult<AppScreenshotRegion>.Err(new PanelNotShowing(state.Id)),
        };
    }

    /// <summary>
    /// Reads the pixel dimensions from a PNG's IHDR chunk without decoding
    /// the image. Validates the 8-byte PNG signature and the IHDR chunk
    /// marker first, returning (0, 0) when the bytes are not a
    /// recognisable PNG.
    /// </summary>
    private static (int Width, int Height) TryReadPngSize(byte[] bytes)
    {
        // 8-byte signature, 4-byte length, 4-byte "IHDR", then width/height
        // (each a big-endian uint32) at offsets 16 and 20.
        const int widthOffset = 16;
        const int heightOffset = 20;
        if (bytes.Length < heightOffset + 4)
        {
            return (0, 0);
        }

        // Reject anything that is not a real PNG so an arbitrary byte
        // buffer can't yield plausible-looking dimensions from fixed
        // offsets. Signature per the PNG spec (RFC 2083 §3.1).
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (!bytes.AsSpan(0, 8).SequenceEqual(signature))
        {
            return (0, 0);
        }

        // The first chunk must be IHDR (offsets 12..16 = 'I','H','D','R').
        ReadOnlySpan<byte> ihdr = [(byte)'I', (byte)'H', (byte)'D', (byte)'R'];
        if (!bytes.AsSpan(12, 4).SequenceEqual(ihdr))
        {
            return (0, 0);
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(widthOffset, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(heightOffset, 4));
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
        {
            return (0, 0);
        }

        return ((int)width, (int)height);
    }
}
