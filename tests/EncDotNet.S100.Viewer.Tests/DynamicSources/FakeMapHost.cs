using EncDotNet.S100.Viewer.Services;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

/// <summary>
/// Test-only <see cref="IMapHost"/> that records overlay-layer
/// additions/removals without spinning up Mapsui or Avalonia.
/// </summary>
internal sealed class FakeMapHost : IMapHost
{
    public List<ILayer> DatasetLayers { get; } = new();
    public List<ILayer> OverlayLayers { get; } = new();

    public void AddLayer(ILayer layer) => DatasetLayers.Add(layer);
    public void RemoveLayer(ILayer layer) => DatasetLayers.Remove(layer);

    public void ReorderDatasetLayers(IReadOnlyList<ILayer> orderedDatasetLayers)
    {
        DatasetLayers.Clear();
        DatasetLayers.AddRange(orderedDatasetLayers);
    }

    public void AddOverlayLayer(ILayer layer) => OverlayLayers.Add(layer);
    public void RemoveOverlayLayer(ILayer layer) => OverlayLayers.Remove(layer);

    public void ZoomToExtent(MRect extent) { }
    public void SetViewportToExtent(MRect mercatorExtent) { }
    public void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution) { }

    /// <summary>Records every <see cref="CenterOn"/> call (lat, lon).</summary>
    public List<(double Latitude, double Longitude)> CenterOnCalls { get; } = new();

    public void CenterOn(double latitudeWgs84, double longitudeWgs84, long durationMs = 300)
        => CenterOnCalls.Add((latitudeWgs84, longitudeWgs84));

    /// <summary>
    /// Viewport centre returned by <see cref="TryGetViewportCenterWgs84"/>.
    /// Tests set this to exercise viewport-relative vessel ordering; the
    /// default (<see langword="null"/>) mimics an unlaid-out map.
    /// </summary>
    public (double Latitude, double Longitude)? ViewportCenter { get; set; }

    public (double Latitude, double Longitude)? TryGetViewportCenterWgs84() => ViewportCenter;

    public Task<byte[]?> RenderCurrentViewToPngAsync(
        int widthPx, int heightPx, double pixelDensity, CancellationToken cancellationToken = default)
        => Task.FromResult<byte[]?>(null);
}
