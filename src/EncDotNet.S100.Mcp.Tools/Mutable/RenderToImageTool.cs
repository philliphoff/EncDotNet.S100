using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>Request payload for <see cref="RenderToImageTool"/>.</summary>
public sealed record RenderToImageRequest(
    [property: Description("Output image width in pixels; null defaults to 1024. Clamped to [64, 4096].")] int? Width = null,
    [property: Description("Output image height in pixels; null defaults to 768. Clamped to [64, 4096].")] int? Height = null,
    [property: Description("Display pixel-density multiplier (1.0 = device-independent pixels; 2.0 = HiDPI). Null defaults to 1.0. Clamped to [0.5, 3.0].")] double? PixelDensity = null);

/// <summary>Result payload for <see cref="RenderToImageTool"/>.</summary>
public sealed record RenderToImageResult(
    [property: Description("Image width in pixels actually rendered (post-clamp / default).")] int Width,
    [property: Description("Image height in pixels actually rendered (post-clamp / default).")] int Height,
    [property: Description("Pixel-density multiplier actually applied (post-clamp / default).")] double PixelDensity,
    [property: Description("Image format identifier; always \"png\" in v1.")] string ImageFormat,
    [property: Description("PNG-encoded image bytes; surfaced separately as an MCP ImageContentBlock with mimeType image/png at the wire layer.")] byte[] ImageBytes,
    [property: Description("Optional human-readable note (e.g. \"defaulted size to 1024x768\").")] string? Notes);

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
                nameof(request.PixelDensity), $"value {d} is not a finite number"));
        }

        var renderer = _renderer.Current;
        if (renderer is null)
        {
            return ToolResult<RenderToImageResult>.Err(
                new HostNotReady("the image renderer is not attached yet"));
        }

        var (width, widthClamped) = ResolveDimension(request.Width, DefaultWidth);
        var (height, heightClamped) = ResolveDimension(request.Height, DefaultHeight);
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
            width, height);

        return ToolResult<RenderToImageResult>.Ok(new RenderToImageResult(
            width, height, density, "png", bytes, notes));
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
        int width, int height)
    {
        if (!widthDefaulted && !widthClamped
            && !heightDefaulted && !heightClamped
            && !densityDefaulted && !densityClamped)
        {
            return null;
        }

        var parts = new List<string>(3);
        if (widthDefaulted || heightDefaulted)
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
