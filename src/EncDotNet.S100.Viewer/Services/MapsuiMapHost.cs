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
/// Composes the Viewer's focused map capabilities over reusable Mapsui and
/// Avalonia adapters.
/// </summary>
/// <remarks>
/// Layer ownership remains delegated to <see cref="MapsuiLayerBands"/>,
/// navigation to <see cref="MapsuiMapNavigator"/>, and live-control behavior
/// to <see cref="AvaloniaMapsuiMapAdapter"/>. This host retains only Viewer
/// capability contracts and render-subsystem lifecycle.
/// </remarks>
internal sealed class MapsuiMapHost :
    IMapLayerCollection,
    IMapsuiOverlayLayerHost,
    IMapViewportController,
    IMapCoordinateConverter,
    IMapSnapshotRenderer,
    IMapInvalidator,
    IDisposable
{
    private readonly AvaloniaMapsuiMapAdapter _avaloniaAdapter;
    private readonly MapsuiLayerBands _layerBands;
    private readonly MapsuiMapNavigator _mapNavigator;
    private bool _disposed;

    public MapsuiMapHost(
        Map map,
        AvaloniaMapsuiMapAdapter avaloniaAdapter,
        DatasetProcessorOwner processorOwner,
        MapsuiDatasetRenderer datasetRenderer,
        IInteroperabilityAuthorityProvider authorityProvider)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(avaloniaAdapter);
        ArgumentNullException.ThrowIfNull(processorOwner);
        ArgumentNullException.ThrowIfNull(datasetRenderer);
        ArgumentNullException.ThrowIfNull(authorityProvider);
        _avaloniaAdapter = avaloniaAdapter;
        _layerBands = new MapsuiLayerBands(map);
        DatasetSession = new MapsuiMapSession(
            _layerBands,
            processorOwner,
            datasetRenderer,
            authorityProvider);
        _mapNavigator = new MapsuiMapNavigator(map);
        RenderSubsystem = ChartRenderSubsystemFactory.CreateActive();
        RenderSubsystem.Activate();
    }

    /// <inheritdoc />
    public IChartRenderSubsystem RenderSubsystem { get; }

    public MapsuiMapSession DatasetSession { get; }

    public void AddDatasetLayer(ILayer layer) => _layerBands.AddDatasetLayer(layer);

    public void RemoveDatasetLayer(ILayer layer) => _layerBands.RemoveDatasetLayer(layer);

    public void ReplaceDatasetLayers(IReadOnlyList<ILayer> orderedDatasetLayers) =>
        _layerBands.ReplaceDatasetLayers(orderedDatasetLayers);

    public void SetBasemapLayer(ILayer? layer) => _layerBands.SetBasemapLayer(layer);

    public void AddOverlayLayer(ILayer layer) => _layerBands.AddOverlayLayer(layer);

    public void RemoveOverlayLayer(ILayer layer) => _layerBands.RemoveOverlayLayer(layer);

    public void AddToolLayer(ILayer layer) => _layerBands.AddToolLayer(layer);

    public void RemoveToolLayer(ILayer layer) => _layerBands.RemoveToolLayer(layer);

    public void RequestRedraw() => _avaloniaAdapter.RequestRedraw();

    public void ZoomToExtent(MRect extent) => _mapNavigator.ZoomToExtent(extent);

    public void ZoomToExtent(MRect extent, long durationMilliseconds) =>
        _mapNavigator.ZoomToExtent(extent, durationMilliseconds);

    public void SetViewportToExtent(MRect mercatorExtent) =>
        _mapNavigator.SetViewportToExtent(mercatorExtent);

    public void SetViewportToCenterAndResolution(
        MPoint mercatorCenter,
        double resolution) =>
        _mapNavigator.SetViewportToCenterAndResolution(mercatorCenter, resolution);

    public void SetRotation(double degrees) => _mapNavigator.SetRotation(degrees);

    public void CenterOn(
        double latitudeWgs84,
        double longitudeWgs84,
        long durationMs = 300) =>
        _mapNavigator.CenterOn(
            new GeoPosition(latitudeWgs84, longitudeWgs84),
            durationMs);

    public GeoPosition? TryGetViewportCenterWgs84() =>
        _mapNavigator.TryGetViewportCenterWgs84();

    public (double Width, double Height)? TryGetViewportSizePx() =>
        _avaloniaAdapter.TryGetViewportSizePx();

    public GeoPosition? TryScreenToWgs84(double xPx, double yPx) =>
        _avaloniaAdapter.TryScreenToWgs84(xPx, yPx);

    public GeoPosition? TryImagePixelToWgs84(
        double xPx,
        double yPx,
        int imageWidthPx,
        int imageHeightPx) =>
        _avaloniaAdapter.TryImagePixelToWgs84(
            xPx,
            yPx,
            imageWidthPx,
            imageHeightPx);

    public Task<byte[]?> RenderCurrentViewToPngAsync(
        int widthPx,
        int heightPx,
        double pixelDensity,
        CancellationToken cancellationToken = default) =>
        _avaloniaAdapter.RenderCurrentViewToPngAsync(
            widthPx,
            heightPx,
            pixelDensity,
            cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DatasetSession.Dispose();
        RenderSubsystem.Deactivate();
        _avaloniaAdapter.Dispose();
    }
}
