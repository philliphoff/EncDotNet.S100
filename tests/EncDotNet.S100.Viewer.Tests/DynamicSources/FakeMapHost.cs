using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Services;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

/// <summary>
/// Test-only focused map capabilities that record layer and viewport activity
/// without spinning up Mapsui or Avalonia.
/// </summary>
internal sealed class FakeMapHost :
    IMapLayerCollection,
    IMapViewportController,
    IMapCoordinateConverter
{
    public List<ILayer> OverlayLayers { get; } = new();

    public void AddOverlayLayer(ILayer layer) => OverlayLayers.Add(layer);
    public void RemoveOverlayLayer(ILayer layer) => OverlayLayers.Remove(layer);
    public void SetBasemapLayer(ILayer? layer) { }
    public void AddToolLayer(ILayer layer) { }
    public void RemoveToolLayer(ILayer layer) { }

    public void ZoomToExtent(MRect extent) { }
    public void SetViewportToExtent(MRect mercatorExtent) { }
    public void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution) { }

    public void SetRotation(double degrees) { }

    /// <summary>Records every <see cref="CenterOn"/> call (lat, lon).</summary>
    public List<GeoPosition> CenterOnCalls { get; } = new();

    public void CenterOn(double latitudeWgs84, double longitudeWgs84, long durationMs = 300)
        => CenterOnCalls.Add(new GeoPosition(latitudeWgs84, longitudeWgs84));

    /// <summary>
    /// Viewport centre returned by <see cref="TryGetViewportCenterWgs84"/>.
    /// Tests set this to exercise viewport-relative vessel ordering; the
    /// default (<see langword="null"/>) mimics an unlaid-out map.
    /// </summary>
    public GeoPosition? ViewportCenter { get; set; }

    public GeoPosition? TryGetViewportCenterWgs84() => ViewportCenter;

    /// <summary>
    /// Viewport size returned by <see cref="TryGetViewportSizePx"/>. Default
    /// (<see langword="null"/>) mimics an unlaid-out map.
    /// </summary>
    public (double Width, double Height)? ViewportSizePx { get; set; }

    public (double Width, double Height)? TryGetViewportSizePx() => ViewportSizePx;

    /// <summary>
    /// Projection used by <see cref="TryScreenToWgs84"/>. Default returns
    /// <see langword="null"/> for every pixel, mimicking an unlaid-out map.
    /// </summary>
    public Func<double, double, GeoPosition?> ScreenToWgs84 { get; set; }
        = static (_, _) => null;

    public GeoPosition? TryScreenToWgs84(double xPx, double yPx)
        => ScreenToWgs84(xPx, yPx);

    /// <summary>
    /// Projection used by <see cref="TryImagePixelToWgs84"/>. Default returns
    /// <see langword="null"/> for every pixel, mimicking an unlaid-out map.
    /// Receives the pixel and the capture's logical dimensions.
    /// </summary>
    public Func<double, double, int, int, GeoPosition?> ImagePixelToWgs84 { get; set; }
        = static (_, _, _, _) => null;

    public GeoPosition? TryImagePixelToWgs84(
        double xPx, double yPx, int imageWidthPx, int imageHeightPx)
        => ImagePixelToWgs84(xPx, yPx, imageWidthPx, imageHeightPx);

}
