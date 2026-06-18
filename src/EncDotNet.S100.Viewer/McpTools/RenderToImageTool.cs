using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Request payload for <see cref="RenderToImageTool"/>.
/// </summary>
[Description("Request for render_to_image: optional output dimensions and pixel-density multiplier; the snapshot otherwise mirrors the viewer's current map state exactly.")]
internal sealed record RenderToImageRequest(
    [property: Description("Output image width in pixels; null defaults to the live viewport width (or 1024 when the viewport is not yet laid out). Clamped to [64, 4096].")] int? Width = null,
    [property: Description("Output image height in pixels; null defaults to the live viewport height (or 768 when the viewport is not yet laid out). Clamped to [64, 4096].")] int? Height = null,
    [property: Description("Display pixel-density multiplier (1.0 = device-independent pixels; 2.0 = HiDPI). Null defaults to 1.0. Clamped to [0.5, 3.0].")] double? PixelDensity = null);

/// <summary>Result of <see cref="RenderToImageTool"/>.</summary>
[Description("Result of render_to_image: echoed dimensions/density plus PNG bytes; agents receive the image as a separate MCP ImageContentBlock alongside this JSON metadata.")]
internal sealed record RenderToImageResult(
    [property: Description("Image width in pixels actually rendered (post-clamp / default-resolution).")] int Width,
    [property: Description("Image height in pixels actually rendered (post-clamp / default-resolution).")] int Height,
    [property: Description("Pixel-density multiplier actually applied (post-clamp / default-resolution).")] double PixelDensity,
    [property: Description("Image format identifier; always \"png\" in v1.")] string ImageFormat,
    [property: Description("PNG-encoded image bytes; surfaced separately as a MCP ImageContentBlock with mimeType image/png at the wire layer.")] byte[] ImageBytes,
    [property: Description("Optional human-readable note (e.g. \"applied default 1024x768 size\").")] string? Notes,
    [property: Description("Live on-screen viewport width in device-independent pixels at capture time, or null when the viewport is not yet laid out. Pass this and viewportHeight back as width/height to capture at the live aspect ratio, and as the imageWidth/imageHeight inputs to pick_features when picking pixels off this capture.")] int? ViewportWidth = null,
    [property: Description("Live on-screen viewport height in device-independent pixels at capture time, or null when the viewport is not yet laid out.")] int? ViewportHeight = null);

/// <summary>
/// Captures the viewer's current map view as a PNG image so that MCP
/// agents can visually inspect what the user sees. Primary use case:
/// diagnosis of rendering issues (palette banding, NoData voids,
/// augmented-geometry artefacts, missing features, etc.).
/// </summary>
/// <remarks>
/// <para>
/// The tool snapshots the live Mapsui map managed by the viewer's
/// <see cref="IMapHost"/>: current viewport, palette, time step, and
/// loaded datasets are reflected exactly. Nothing in the live map is
/// mutated; the snapshot uses a clone Map that shares the layer
/// collection but owns its own navigator.
/// </para>
/// <para>
/// This tool is viewer-injected — the catalog-only MCP tool surface
/// in <c>EncDotNet.S100.Mcp.Tools</c> deliberately has no rendering
/// dependency. A future headless MCP host would need to provide its
/// own equivalent.
/// </para>
/// <para>
/// When the caller omits both <c>width</c> and <c>height</c> the
/// capture is sized to the live on-screen viewport (when laid out) so
/// the PNG matches what the user sees pixel-for-pixel, rather than the
/// fixed 1024x768 default which would letterbox under
/// <c>MBoxFit.Fit</c> against a differently shaped viewport. The live
/// viewport size is always echoed back as <c>viewportWidth</c>/
/// <c>viewportHeight</c> so agents can request a matching aspect ratio
/// or feed those dimensions to <c>pick_features</c> when picking pixels
/// off the capture.
/// </para>
/// </remarks>
internal sealed class RenderToImageTool
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "render_to_image";

    internal const int DefaultWidth = 1024;
    internal const int DefaultHeight = 768;
    internal const double DefaultPixelDensity = 1.0;

    internal const int MinDimension = 64;
    internal const int MaxDimension = 4096;
    internal const double MinPixelDensity = 0.5;
    internal const double MaxPixelDensity = 3.0;

    private readonly IMapHostAccessor _accessor;

    /// <summary>Creates a new <see cref="RenderToImageTool"/>.</summary>
    public RenderToImageTool(IMapHostAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <summary>Executes the tool.</summary>
    public async Task<ToolResult<RenderToImageResult>> InvokeAsync(
        RenderToImageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.PixelDensity is { } d && (double.IsNaN(d) || double.IsInfinity(d)))
        {
            return ToolResult<RenderToImageResult>.Err(
                new InvalidArgument(nameof(request.PixelDensity),
                    $"value {d} is not a finite number"));
        }

        var host = _accessor.Current;
        if (host is null)
        {
            return ToolResult<RenderToImageResult>.Err(
                new MapNotReady("the viewer's map control has not been initialised yet"));
        }

        var (viewportWidth, viewportHeight) = ResolveViewportSize(host);

        // When the caller specifies neither dimension we capture at the
        // live viewport size (if known) so the snapshot matches what the
        // user sees pixel-for-pixel — avoiding the MBoxFit.Fit letterboxing
        // that a fixed 1024x768 default produces against a differently
        // shaped viewport. A partial request (only one dimension) keeps the
        // static fallback for the omitted side to avoid an arbitrary aspect.
        var useLiveDefaults = request.Width is null && request.Height is null
            && viewportWidth is not null && viewportHeight is not null;

        var (width, widthClamped) = ResolveDimension(
            request.Width, useLiveDefaults ? viewportWidth!.Value : DefaultWidth);
        var (height, heightClamped) = ResolveDimension(
            request.Height, useLiveDefaults ? viewportHeight!.Value : DefaultHeight);
        var (density, densityClamped) = ResolveDensity(request.PixelDensity);

        byte[]? bytes;
        try
        {
            bytes = await host.RenderCurrentViewToPngAsync(width, height, density, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult<RenderToImageResult>.Err(
                new MapNotReady($"render failed: {ex.GetType().Name}: {ex.Message}"));
        }

        if (bytes is null || bytes.Length == 0)
        {
            return ToolResult<RenderToImageResult>.Err(
                new MapNotReady("no current viewport (map has no size yet)"));
        }

        var notes = BuildNotes(
            request.Width is null, widthClamped,
            request.Height is null, heightClamped,
            request.PixelDensity is null, densityClamped,
            useLiveDefaults, width, height);

        return ToolResult<RenderToImageResult>.Ok(new RenderToImageResult(
            width, height, density, "png", bytes, notes, viewportWidth, viewportHeight));
    }

    private static (int? Width, int? Height) ResolveViewportSize(IMapHost host)
    {
        if (host.TryGetViewportSizePx() is not { } size) return (null, null);
        if (size.Width < 1 || size.Height < 1
            || double.IsNaN(size.Width) || double.IsNaN(size.Height)
            || double.IsInfinity(size.Width) || double.IsInfinity(size.Height))
        {
            return (null, null);
        }

        var w = Math.Clamp((int)Math.Round(size.Width), MinDimension, MaxDimension);
        var h = Math.Clamp((int)Math.Round(size.Height), MinDimension, MaxDimension);
        return (w, h);
    }

    private static (int Value, bool Clamped) ResolveDimension(int? requested, int @default)
    {
        if (requested is null) return (@default, false);
        var raw = requested.Value;
        var clamped = Math.Clamp(raw, MinDimension, MaxDimension);
        return (clamped, clamped != raw);
    }

    private static (double Value, bool Clamped) ResolveDensity(double? requested)
    {
        if (requested is null) return (DefaultPixelDensity, false);
        var raw = requested.Value;
        var clamped = Math.Clamp(raw, MinPixelDensity, MaxPixelDensity);
        return (clamped, clamped != raw);
    }

    private static string? BuildNotes(
        bool widthDefaulted, bool widthClamped,
        bool heightDefaulted, bool heightClamped,
        bool densityDefaulted, bool densityClamped,
        bool usedLiveDefaults, int width, int height)
    {
        if (!widthDefaulted && !widthClamped
            && !heightDefaulted && !heightClamped
            && !densityDefaulted && !densityClamped)
        {
            return null;
        }

        var parts = new System.Collections.Generic.List<string>(3);
        if (usedLiveDefaults)
        {
            parts.Add($"defaulted to live viewport size {width}x{height}");
        }
        else if (widthDefaulted || heightDefaulted)
        {
            var which = (widthDefaulted, heightDefaulted) switch
            {
                (true, true) => "width/height",
                (true, false) => "width",
                _ => "height",
            };
            parts.Add($"defaulted {which} to {DefaultWidth}x{DefaultHeight} (live viewport size unavailable)");
        }
        if (widthClamped || heightClamped)
        {
            parts.Add("clamped dimensions to [64, 4096]");
        }
        if (densityDefaulted) parts.Add("defaulted pixelDensity to 1.0");
        else if (densityClamped) parts.Add("clamped pixelDensity to [0.5, 3.0]");

        return string.Join("; ", parts);
    }
}
