using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Converts between live or captured map pixel coordinates and WGS-84.
/// </summary>
internal interface IMapCoordinateConverter
{
    /// <summary>Returns the laid-out live viewport size in pixels.</summary>
    (double Width, double Height)? TryGetViewportSizePx();

    /// <summary>Converts a live viewport pixel to WGS-84.</summary>
    GeoPosition? TryScreenToWgs84(double xPx, double yPx);

    /// <summary>Converts a snapshot pixel to WGS-84.</summary>
    GeoPosition? TryImagePixelToWgs84(
        double xPx,
        double yPx,
        int imageWidthPx,
        int imageHeightPx);
}
