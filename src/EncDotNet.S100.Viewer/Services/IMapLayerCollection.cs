using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Manages the basemap, dataset, overlay, and tool layer bands owned by the
/// viewer.
/// </summary>
/// <remarks>
/// Implementations delegate band ownership and ordering to
/// <see cref="EncDotNet.S100.Renderers.Mapsui.MapsuiLayerBands"/>.
/// </remarks>
internal interface IMapLayerCollection
{
    /// <summary>
    /// Gets the reusable dataset-layer session when the host provides one.
    /// </summary>
    /// <remarks>
    /// The session owns and drives the dataset band itself (via
    /// <see cref="IS100MapSession.AddDatasetAsync"/> and friends), so this
    /// collection exposes only the host-fillable basemap, overlay, and tool
    /// bands — never the dataset band.
    /// </remarks>
    MapsuiMapSession? DatasetSession => null;

    /// <summary>Sets or clears the single basemap layer.</summary>
    void SetBasemapLayer(ILayer? layer);

    /// <summary>Adds a layer to the overlay band.</summary>
    void AddOverlayLayer(ILayer layer);

    /// <summary>Removes a layer from the overlay band.</summary>
    void RemoveOverlayLayer(ILayer layer);

    /// <summary>Adds a layer to the tool band.</summary>
    void AddToolLayer(ILayer layer);

    /// <summary>Removes a layer from the tool band.</summary>
    void RemoveToolLayer(ILayer layer);
}
