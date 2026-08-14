using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Mapsui.Avalonia;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Composes the Viewer's focused map capabilities over the reusable S-100
/// session obtained from <see cref="AvaloniaS100MapExtensions.AddS100"/>.
/// </summary>
/// <remarks>
/// This host sets up S-100 the same way the MapHost sample does — one
/// <see cref="AvaloniaS100MapExtensions.AddS100"/> call attaches the session to
/// the control's map and the <see cref="AvaloniaMapsuiMapAdapter"/> to the
/// control. The host then implements the Viewer's capability contracts by
/// delegating band ownership to <see cref="IS100MapSession.Layers"/>, navigation
/// to <see cref="IS100MapSession.Navigator"/>, and live-control behavior
/// (redraw, snapshots, coordinate conversion) to the adapter. It retains only
/// the Viewer-specific render-subsystem lifecycle on top of that.
/// </remarks>
internal sealed class MapsuiMapHost :
    IMapLayerCollection,
    IMapsuiOverlayLayerHost,
    IMapViewportController,
    IMapCoordinateConverter,
    IImageRenderer,
    IMapInvalidator,
    IDisposable
{
    private readonly IS100MapSession _session;
    private readonly AvaloniaMapsuiMapAdapter _adapter;
    private bool _disposed;

    public MapsuiMapHost(
        CaptureSynchronizedMapControl mapControl,
        DatasetProcessorOwner processorOwner,
        MapsuiDatasetRenderer datasetRenderer,
        IInteroperabilityAuthorityProvider authorityProvider)
    {
        ArgumentNullException.ThrowIfNull(mapControl);
        ArgumentNullException.ThrowIfNull(processorOwner);
        ArgumentNullException.ThrowIfNull(datasetRenderer);
        ArgumentNullException.ThrowIfNull(authorityProvider);

        // The public entry point: attach the S-100 session to the control's map
        // and the Avalonia adapter to the control in one call. The DI-shared
        // processor owner and dataset renderer are injected as borrowed
        // collaborators — the session never disposes them (other Viewer services
        // acquire processor leases on the same owner and render through the same
        // renderer), so their lifetime stays with DI. A prebuilt renderer already
        // carries its own CRS transform factory, so none is supplied here. The
        // adapter's UI-thread redraw is defaulted by AddS100, replacing the
        // former process-global static redraw hooks.
        (_session, _adapter) = mapControl.AddS100(
            new S100MapsuiOptions
            {
                ProcessorOwner = processorOwner,
                DatasetRenderer = datasetRenderer,
                InteroperabilityAuthorityProvider = authorityProvider,
            });
    }

    /// <inheritdoc />
    public MapsuiMapSession DatasetSession => _session.Session;

    public void SetBasemapLayer(ILayer? layer) => _session.Layers.SetBasemapLayer(layer);

    public void AddOverlayLayer(ILayer layer) => _session.Layers.AddOverlayLayer(layer);

    public void RemoveOverlayLayer(ILayer layer) => _session.Layers.RemoveOverlayLayer(layer);

    public void AddToolLayer(ILayer layer) => _session.Layers.AddToolLayer(layer);

    public void RemoveToolLayer(ILayer layer) => _session.Layers.RemoveToolLayer(layer);

    public void RequestRedraw() => _adapter.RequestRedraw();

    public void ZoomToExtent(MRect extent) => _session.Navigator.ZoomToExtent(extent);

    public void ZoomToExtent(MRect extent, long durationMilliseconds) =>
        _session.Navigator.ZoomToExtent(extent, durationMilliseconds);

    public void SetViewportToExtent(MRect mercatorExtent) =>
        _session.Navigator.SetViewportToExtent(mercatorExtent);

    public void SetViewportToCenterAndResolution(
        MPoint mercatorCenter,
        double resolution) =>
        _session.Navigator.SetViewportToCenterAndResolution(mercatorCenter, resolution);

    public void SetRotation(double degrees) => _session.Navigator.SetRotation(degrees);

    public void CenterOn(
        double latitudeWgs84,
        double longitudeWgs84,
        long durationMs = 300) =>
        _session.Navigator.CenterOn(
            new GeoPosition(latitudeWgs84, longitudeWgs84),
            durationMs);

    public GeoPosition? TryGetViewportCenterWgs84() =>
        _session.Navigator.TryGetViewportCenterWgs84();

    public (double Width, double Height)? TryGetViewportSizePx() =>
        _adapter.TryGetViewportSizePx();

    public GeoPosition? TryScreenToWgs84(double xPx, double yPx) =>
        _adapter.TryScreenToWgs84(xPx, yPx);

    public GeoPosition? TryImagePixelToWgs84(
        double xPx,
        double yPx,
        int imageWidthPx,
        int imageHeightPx) =>
        _adapter.TryImagePixelToWgs84(
            xPx,
            yPx,
            imageWidthPx,
            imageHeightPx);

    public Task<byte[]?> RenderToPngAsync(
        int widthPx,
        int heightPx,
        double pixelDensity,
        CancellationToken cancellationToken = default) =>
        _adapter.RenderCurrentViewToPngAsync(
            widthPx,
            heightPx,
            pixelDensity,
            cancellationToken);

    /// <inheritdoc />
    public (int Width, int Height)? PreferredSize => NormalizePreferredSize(TryGetViewportSizePx());

    /// <summary>
    /// Rounds and range-checks a live viewport size into an integer pixel size
    /// usable as <see cref="IImageRenderer.PreferredSize"/>, or
    /// <see langword="null"/> when the layout is degenerate (unset, sub-pixel,
    /// NaN/Inf, or out of <see cref="int"/> range).
    /// </summary>
    /// <remarks>
    /// Round, then range-check before the int cast: a NaN, sub-pixel, or
    /// out-of-int-range value (e.g. from an uninitialised layout) must not reach
    /// the cast, which would otherwise produce an unspecified int. +Inf is caught
    /// by the upper bound and -Inf by the lower. The raw rounded live size is
    /// returned; the render_to_image tool clamps it to the supported
    /// render-dimension range for both the default and the echo.
    /// </remarks>
    internal static (int Width, int Height)? NormalizePreferredSize(
        (double Width, double Height)? viewportSize)
    {
        if (viewportSize is not { } size)
        {
            return null;
        }

        var width = Math.Round(size.Width);
        var height = Math.Round(size.Height);
        if (double.IsNaN(width) || double.IsNaN(height)
            || width < 1 || height < 1
            || width > int.MaxValue || height > int.MaxValue)
        {
            return null;
        }
        return ((int)width, (int)height);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
        _adapter.Dispose();
    }
}
