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
    [property: Description("Output image width in pixels; null defaults to 1024. Clamped to [64, 4096].")] int? Width = null,
    [property: Description("Output image height in pixels; null defaults to 768. Clamped to [64, 4096].")] int? Height = null,
    [property: Description("Display pixel-density multiplier (1.0 = device-independent pixels; 2.0 = HiDPI). Null defaults to 1.0. Clamped to [0.5, 3.0].")] double? PixelDensity = null);

/// <summary>Result of <see cref="RenderToImageTool"/>.</summary>
[Description("Result of render_to_image: echoed dimensions/density plus PNG bytes; agents receive the image as a separate MCP ImageContentBlock alongside this JSON metadata.")]
internal sealed record RenderToImageResult(
    [property: Description("Image width in pixels actually rendered (post-clamp / default-resolution).")] int Width,
    [property: Description("Image height in pixels actually rendered (post-clamp / default-resolution).")] int Height,
    [property: Description("Pixel-density multiplier actually applied (post-clamp / default-resolution).")] double PixelDensity,
    [property: Description("Image format identifier; always \"png\" in v1.")] string ImageFormat,
    [property: Description("PNG-encoded image bytes; surfaced separately as a MCP ImageContentBlock with mimeType image/png at the wire layer.")] byte[] ImageBytes,
    [property: Description("Optional human-readable note (e.g. \"applied default 1024x768 size\").")] string? Notes);

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

        var (width, widthClamped) = ResolveDimension(request.Width, DefaultWidth);
        var (height, heightClamped) = ResolveDimension(request.Height, DefaultHeight);
        var (density, densityClamped) = ResolveDensity(request.PixelDensity);

        var host = _accessor.Current;
        if (host is null)
        {
            return ToolResult<RenderToImageResult>.Err(
                new MapNotReady("the viewer's map control has not been initialised yet"));
        }

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
            request.PixelDensity is null, densityClamped);

        return ToolResult<RenderToImageResult>.Ok(new RenderToImageResult(
            width, height, density, "png", bytes, notes));
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
        bool densityDefaulted, bool densityClamped)
    {
        if (!widthDefaulted && !widthClamped
            && !heightDefaulted && !heightClamped
            && !densityDefaulted && !densityClamped)
        {
            return null;
        }

        var parts = new System.Collections.Generic.List<string>(3);
        if (widthDefaulted || heightDefaulted)
        {
            var which = (widthDefaulted, heightDefaulted) switch
            {
                (true, true) => "width/height",
                (true, false) => "width",
                _ => "height",
            };
            parts.Add($"defaulted {which}");
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
