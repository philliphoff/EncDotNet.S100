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
    /// Replaces the host's dataset-layer slice with the supplied
    /// sequence, preserving the relative position of the basemap
    /// (below) and any tool overlays (above). The supplied list is
    /// <b>authoritative</b>: any previously-tracked dataset layer not
    /// present in the new sequence is removed from the map, and any
    /// layer in the new sequence that the host has not seen before
    /// (e.g. a rule-filtered MemoryLayer replica) is inserted and
    /// tracked. Callers therefore use this method both to reorder and
    /// to hide/replace dataset layers — including swapping out inactive
    /// datasets when the user toggles their Active flag.
    /// </summary>
    void ReorderDatasetLayers(System.Collections.Generic.IReadOnlyList<ILayer> orderedDatasetLayers);

    /// <summary>
    /// Pans/zooms the navigator to the supplied extent (no-op when the
    /// map's navigator is unavailable).
    /// </summary>
    void ZoomToExtent(MRect extent);

    /// <summary>
    /// Captures the current map view as a PNG byte array.
    /// </summary>
    /// <param name="widthPx">Output image width in pixels (caller-clamped).</param>
    /// <param name="heightPx">Output image height in pixels (caller-clamped).</param>
    /// <param name="pixelDensity">Display pixel density multiplier (1.0 = device-independent pixels).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// PNG-encoded bytes of the current map state at the requested
    /// size, or <see langword="null"/> when the underlying map has not
    /// been initialised yet. The snapshot mirrors the user's current
    /// viewport, palette, time step, and loaded datasets exactly —
    /// nothing in the live map is mutated by this call.
    /// </returns>
    /// <remarks>
    /// Implementations must be safe to call from any thread; they
    /// marshal to the UI thread as needed.
    /// </remarks>
    System.Threading.Tasks.Task<byte[]?> RenderCurrentViewToPngAsync(
        int widthPx,
        int heightPx,
        double pixelDensity,
        System.Threading.CancellationToken cancellationToken = default);
}
