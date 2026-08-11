using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>Request payload for <see cref="RenderToImageTool"/>.</summary>
public sealed record RenderToImageRequest(
    [property: Description("Output image width in pixels; clamped to [64, 4096]. When both width and height are omitted, defaults to the renderer's live viewport width if it has one, otherwise 1024. Independently, whenever the renderer has a live viewport its width is echoed as viewportWidth — even when explicit dimensions are supplied.")] int? Width = null,
    [property: Description("Output image height in pixels; clamped to [64, 4096]. When both width and height are omitted, defaults to the renderer's live viewport height if it has one, otherwise 768. Independently, whenever the renderer has a live viewport its height is echoed as viewportHeight — even when explicit dimensions are supplied.")] int? Height = null,
    [property: Description("Display pixel-density multiplier (1.0 = device-independent pixels; 2.0 = HiDPI). Null defaults to 1.0. Clamped to [0.5, 3.0].")] double? PixelDensity = null);

/// <summary>Result payload for <see cref="RenderToImageTool"/>.</summary>
public sealed record RenderToImageResult(
    [property: Description("Image width in pixels actually rendered (post-clamp / default).")] int Width,
    [property: Description("Image height in pixels actually rendered (post-clamp / default).")] int Height,
    [property: Description("Pixel-density multiplier resolved for the request (post-clamp / default). A host may not apply it — the headless CLI render ignores density and encodes at the literal width/height.")] double PixelDensity,
    [property: Description("Image format identifier; always \"png\" in v1.")] string ImageFormat,
    [property: Description("PNG-encoded image bytes; surfaced separately as an MCP ImageContentBlock with mimeType image/png at the wire layer.")] byte[] ImageBytes,
    [property: Description("Optional human-readable note (e.g. \"defaulted size to 1024x768\").")] string? Notes,
    [property: Description("The renderer's live viewport width in pixels at render time, or null when it has none (e.g. a headless host). Pass this and viewportHeight back as width/height to capture at the live aspect ratio. For a pixel pick, feed pick_features the rendered image's width/height (the width/height fields), not these — the two match only when the capture defaulted to the live size.")] int? ViewportWidth = null,
    [property: Description("The renderer's live viewport height in pixels at render time, or null when it has none. See viewportWidth for how it relates to the image dimensions.")] int? ViewportHeight = null);

/// <summary>
/// Mutating-session tool that renders the current session state — loaded
/// datasets, presentation, time, and viewport — to a PNG. Renderer-neutral: it
/// drives the shared <see cref="IImageRenderer"/>, so the desktop viewer backs
/// it with a live-map snapshot and the headless CLI backs it with its Skia
/// pipeline. The render is side-effect free.
/// </summary>
public sealed class RenderToImageTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "render_to_image";

    internal const int DefaultWidth = 1024;
    internal const int DefaultHeight = 768;
    internal const double DefaultPixelDensity = 1.0;

    internal const int MinDimension = 64;
    internal const int MaxDimension = 4096;
    internal const double MinPixelDensity = 0.5;
    internal const double MaxPixelDensity = 3.0;

    private readonly ICapabilityAccessor<IImageRenderer> _renderer;

    /// <summary>Creates the tool bound to an image-renderer accessor.</summary>
    public RenderToImageTool(ICapabilityAccessor<IImageRenderer> renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    /// <summary>Renders the current view. Returns the PNG bytes plus echoed dimensions.</summary>
    public async Task<ToolResult<RenderToImageResult>> InvokeAsync(
        RenderToImageRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (request.PixelDensity is { } d && (double.IsNaN(d) || double.IsInfinity(d)))
        {
            return ToolResult<RenderToImageResult>.Err(new InvalidArgument(
                "pixelDensity", $"value {d} is not a finite number"));
        }

        var renderer = _renderer.Current;
        if (renderer is null)
        {
            return ToolResult<RenderToImageResult>.Err(
                new HostNotReady("the image renderer is not attached yet"));
        }

        var (viewportWidth, viewportHeight) = ResolvePreferred(renderer.PreferredSize);

        // When the caller specifies neither dimension and the renderer has a live
        // viewport, capture at that size so the snapshot matches what the user
        // sees pixel-for-pixel — avoiding the letterboxing a fixed default would
        // produce against a differently shaped viewport. A partial request (only
        // one dimension) keeps the static fallback for the omitted side.
        var useLiveDefaults = request.Width is null && request.Height is null
            && viewportWidth is not null && viewportHeight is not null;

        var (width, widthClamped) = ResolveDimension(
            request.Width, useLiveDefaults ? viewportWidth!.Value : DefaultWidth);
        var (height, heightClamped) = ResolveDimension(
            request.Height, useLiveDefaults ? viewportHeight!.Value : DefaultHeight);
        var (density, densityClamped) = ResolveDensity(request.PixelDensity);

        var bytes = await renderer.RenderToPngAsync(width, height, density, ct).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return ToolResult<RenderToImageResult>.Err(new HostNotReady(
                "the renderer produced no image (no datasets loaded, or an empty viewport)"));
        }

        var notes = BuildNotes(
            request.Width is null, widthClamped,
            request.Height is null, heightClamped,
            request.PixelDensity is null, densityClamped,
            useLiveDefaults, width, height);

        return ToolResult<RenderToImageResult>.Ok(new RenderToImageResult(
            width, height, density, "png", bytes, notes, viewportWidth, viewportHeight));
    }

    /// <summary>
    /// Clamps the renderer's preferred size to the render-dimension range, or
    /// returns <c>(null, null)</c> when it has none or reports a degenerate size.
    /// </summary>
    private static (int? Width, int? Height) ResolvePreferred((int Width, int Height)? preferred)
    {
        if (preferred is not { } size) return (null, null);
        if (size.Width < 1 || size.Height < 1) return (null, null);
        return (
            Math.Clamp(size.Width, MinDimension, MaxDimension),
            Math.Clamp(size.Height, MinDimension, MaxDimension));
    }

    private static (int Value, bool Clamped) ResolveDimension(int? requested, int @default)
    {
        if (requested is null) return (@default, false);
        var clamped = Math.Clamp(requested.Value, MinDimension, MaxDimension);
        return (clamped, clamped != requested.Value);
    }

    private static (double Value, bool Clamped) ResolveDensity(double? requested)
    {
        if (requested is null) return (DefaultPixelDensity, false);
        var clamped = Math.Clamp(requested.Value, MinPixelDensity, MaxPixelDensity);
        return (clamped, clamped != requested.Value);
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

        var parts = new List<string>(3);
        if (usedLiveDefaults)
        {
            parts.Add($"defaulted to live viewport size {width}x{height}");
        }
        else if (widthDefaulted || heightDefaulted)
        {
            parts.Add($"defaulted size to {(widthDefaulted ? DefaultWidth : width)}x{(heightDefaulted ? DefaultHeight : height)}");
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
