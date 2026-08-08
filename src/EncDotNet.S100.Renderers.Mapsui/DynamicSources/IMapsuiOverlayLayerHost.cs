using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// A minimal overlay-band target: adds and removes layers in the overlay tier
/// above the dataset layers. Implemented by <see cref="MapsuiLayerBands"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="S100DynamicSourceHost"/> depends only on this interface so it can
/// attach dynamic-source layers over either the reusable session's
/// <see cref="MapsuiLayerBands"/> or a UI host's own layer-band adapter, without
/// a dependency on any particular host type.
/// </para>
/// <para>
/// Implementations mutate the underlying map directly and do <b>not</b> marshal
/// to a UI thread; the caller marshals (see
/// <see cref="S100DynamicSourceHost"/>'s marshal callback).
/// </para>
/// </remarks>
public interface IMapsuiOverlayLayerHost
{
    /// <summary>Adds a layer to the overlay band above all dataset layers.</summary>
    /// <param name="layer">The overlay layer to add.</param>
    void AddOverlayLayer(ILayer layer);

    /// <summary>Removes a layer from the overlay band.</summary>
    /// <param name="layer">The overlay layer to remove.</param>
    void RemoveOverlayLayer(ILayer layer);
}
