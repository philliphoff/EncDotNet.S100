using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Minimal map mutation surface used by services that need to manage
/// dataset layers without taking a hard dependency on
/// <see cref="Mapsui.UI.Avalonia.MapControl"/>. Implemented by
/// <see cref="MapsuiMapHost"/> over a live <c>MapControl</c>; tests can
/// supply a fake.
/// </summary>
/// <remarks>
/// The host carves the Mapsui layer collection into three implicit
/// tiers. The viewer's basemap is added to the live <c>Map.Layers</c>
/// before the host sees any traffic; map-tool overlays are appended to
/// the same collection on demand. Layers added via <see cref="AddLayer"/>
/// are treated as <em>dataset</em> layers and inserted strictly above
/// the basemap and below any overlays. <see cref="ReorderDatasetLayers"/>
/// preserves that invariant — overlays never move when datasets reorder.
/// </remarks>
internal interface IMapHost
{
    /// <summary>
    /// Adds a dataset layer to the map, above the basemap and below
    /// any tool overlays.
    /// </summary>
    void AddLayer(ILayer layer);

    /// <summary>Removes a layer from the map.</summary>
    void RemoveLayer(ILayer layer);

    /// <summary>
    /// Replaces the paint order of the host's dataset layers with the
    /// supplied sequence, preserving the relative position of the
    /// basemap (below) and any overlays (above). Layers not present in
    /// the host's dataset set are ignored.
    /// </summary>
    void ReorderDatasetLayers(System.Collections.Generic.IReadOnlyList<ILayer> orderedDatasetLayers);

    /// <summary>
    /// Pans/zooms the navigator to the supplied extent (no-op when the
    /// map's navigator is unavailable).
    /// </summary>
    void ZoomToExtent(MRect extent);
}
