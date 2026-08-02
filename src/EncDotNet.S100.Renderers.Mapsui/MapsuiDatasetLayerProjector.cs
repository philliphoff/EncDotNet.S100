namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Projects ordinary bottom-to-top dataset snapshots into the final layer
/// sequence placed in a Mapsui map.
/// </summary>
/// <remarks>
/// The default <see cref="MapsuiMapSession"/> projection preserves ordinary
/// dataset order and excludes inactive datasets. Hosts may temporarily supply a
/// richer compositor, such as the Viewer's S-98 projector, without transferring
/// layer ownership out of the session.
/// </remarks>
/// <param name="datasets">
/// Managed dataset snapshots in ordinary bottom-to-top paint order.
/// </param>
/// <returns>The final bottom-to-top projected layer sequence.</returns>
public delegate IReadOnlyList<MapsuiProjectedDatasetLayer> MapsuiDatasetLayerProjector(
    IReadOnlyList<MapsuiMapDatasetSnapshot> datasets);
