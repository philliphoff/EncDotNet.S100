using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>
/// Converts image-space pixels to geographic coordinates under the session's
/// current viewport. Backs the pixel-addressing part of the <c>pick_features</c>
/// tool.
/// </summary>
/// <remarks>
/// Geographic identify already exists read-only (<c>find_at</c> /
/// <c>identify_features</c> resolve features at a lon/lat via the catalog). The
/// only thing a pixel pick adds is turning a pixel in a rendered image into that
/// lon/lat, which requires knowing the current viewport and the image size. So
/// <c>pick_features</c> is this converter plus the existing geographic query:
/// convert here, then reuse the read-only path.
/// </remarks>
public interface IPixelCoordinateConverter
{
    /// <summary>
    /// Maps a pixel in an image of the given size to a geographic position under
    /// the current viewport.
    /// </summary>
    /// <param name="xPx">Pixel X, measured from the left edge.</param>
    /// <param name="yPx">Pixel Y, measured from the top edge.</param>
    /// <param name="imageWidthPx">Width of the image the pixel is addressed in.</param>
    /// <param name="imageHeightPx">Height of the image the pixel is addressed in.</param>
    /// <returns>
    /// The geographic position, or <see langword="null"/> when no viewport is
    /// set or the pixel falls outside the addressable area.
    /// </returns>
    GeoPosition? TryImagePixelToGeographic(
        double xPx,
        double yPx,
        int imageWidthPx,
        int imageHeightPx);
}
