using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// The host-facing layer bands of an <see cref="IS100MapSession"/>: the basemap
/// below the datasets, the overlay band above them, and the topmost tool band.
/// A host attaches its own decoration layers — basemaps, pick highlights, extent
/// indicators, measure tools — through this surface so they keep their z-order
/// relative to the session's dataset layers as datasets are added and removed.
/// </summary>
/// <remarks>
/// <para>
/// This surface deliberately omits the <b>dataset</b> band. The session owns and
/// drives dataset layers through <see cref="IS100MapSession.AddDatasetAsync"/>,
/// <see cref="IS100MapSession.RemoveDataset"/>, and
/// <see cref="IS100MapSession.SetOrder"/>; a host that mutated the dataset band
/// directly would corrupt the session's bookkeeping.
/// </para>
/// <para>
/// Implementations mutate the attached <see cref="Mapsui.Map"/> directly and do
/// <b>not</b> marshal to a UI thread; a UI host calls them on the map-owning
/// thread, matching the session's threading contract.
/// </para>
/// </remarks>
public interface IS100MapLayerHost
{
    /// <summary>
    /// Sets the single basemap layer below all dataset layers, or removes it
    /// when <paramref name="layer"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="layer">The new basemap layer, or <see langword="null"/>.</param>
    void SetBasemapLayer(ILayer? layer);

    /// <summary>Adds a layer to the overlay band above all dataset layers.</summary>
    /// <param name="layer">The overlay layer to add.</param>
    void AddOverlayLayer(ILayer layer);

    /// <summary>Removes a layer from the overlay band.</summary>
    /// <param name="layer">The overlay layer to remove.</param>
    void RemoveOverlayLayer(ILayer layer);

    /// <summary>Adds a layer to the topmost tool band.</summary>
    /// <param name="layer">The tool layer to add.</param>
    void AddToolLayer(ILayer layer);

    /// <summary>Removes a layer from the tool band.</summary>
    /// <param name="layer">The tool layer to remove.</param>
    void RemoveToolLayer(ILayer layer);
}
