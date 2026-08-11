using EncDotNet.S100.Mcp.Tools.Mutable;

namespace EncDotNet.S100.Viewer.Services.McpCapabilities;

/// <summary>
/// Adapts the viewer's <see cref="IMapSnapshotRenderer"/> (and, for the live
/// viewport size, its <see cref="IMapCoordinateConverter"/>) to the shared
/// <see cref="IImageRenderer"/> that backs the <c>render_to_image</c> tool.
/// </summary>
/// <remarks>
/// The render call maps straight through — the viewer snapshots its live Mapsui
/// map. <see cref="PreferredSize"/> reports the live on-screen viewport size so
/// an unsized <c>render_to_image</c> captures what the user sees pixel-for-pixel
/// (rather than letterboxing the fixed default) and echoes those dimensions back
/// for aspect-matching and pixel picks. The coordinate converter is optional; a
/// null one (or a viewport not yet laid out) yields a null preferred size, and
/// the tool falls back to its fixed default.
/// </remarks>
/// <param name="snapshot">The viewer's live-map snapshot renderer.</param>
/// <param name="coordinates">
/// The viewer's coordinate converter, used only to read the live viewport size;
/// may be <see langword="null"/>.
/// </param>
internal sealed class ViewerImageRenderer(
    IMapSnapshotRenderer snapshot,
    IMapCoordinateConverter? coordinates)
    : IImageRenderer
{
    private readonly IMapSnapshotRenderer _snapshot = snapshot
        ?? throw new ArgumentNullException(nameof(snapshot));

    private readonly IMapCoordinateConverter? _coordinates = coordinates;

    /// <inheritdoc />
    public Task<byte[]?> RenderToPngAsync(
        int widthPx, int heightPx, double pixelDensity, CancellationToken cancellationToken = default)
        => _snapshot.RenderCurrentViewToPngAsync(widthPx, heightPx, pixelDensity, cancellationToken);

    /// <inheritdoc />
    public (int Width, int Height)? PreferredSize
    {
        get
        {
            if (_coordinates?.TryGetViewportSizePx() is not { } size)
            {
                return null;
            }
            // Round, then range-check before the int cast: a NaN, sub-pixel, or
            // out-of-int-range value (e.g. from an uninitialised layout) must not
            // reach the cast, which would otherwise produce an unspecified int.
            // +Inf is caught by the upper bound and -Inf by the lower.
            var width = Math.Round(size.Width);
            var height = Math.Round(size.Height);
            if (double.IsNaN(width) || double.IsNaN(height)
                || width < 1 || height < 1
                || width > int.MaxValue || height > int.MaxValue)
            {
                return null;
            }
            // Return the raw rounded live size; RenderToImageTool clamps it to the
            // supported render-dimension range for both the default and the echo.
            return ((int)width, (int)height);
        }
    }
}
