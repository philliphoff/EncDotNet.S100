using EncDotNet.S100.DynamicSources;
using Mapsui;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// Renders a <see cref="DynamicFeature"/> as zero or more Mapsui
/// <see cref="IFeature"/> instances ready to drop into a
/// <see cref="global::Mapsui.Layers.MemoryLayer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Renderers are <b>pure functions</b> of a feature snapshot — they
/// must not subscribe to sources, must not retain mutable state
/// between calls, and may be invoked on any thread. The viewer-side
/// overlay host serialises calls onto the UI thread when mutating
/// the backing <c>MemoryLayer</c>.
/// </para>
/// <para>
/// Coordinates from <see cref="DynamicFeature.Coordinates"/> arrive
/// in WGS-84 lat/lon (latitude first, per the static vector
/// pipeline convention). Renderers project to SphericalMercator
/// (EPSG:3857) themselves; the overlay host does not project.
/// </para>
/// </remarks>
public interface IDynamicFeatureRenderer
{
    /// <summary>
    /// Returns <see langword="true"/> if this renderer is willing to
    /// produce output for <paramref name="feature"/>. Used by
    /// <see cref="CompositeDynamicFeatureRenderer"/> for ordered
    /// fallthrough.
    /// </summary>
    bool CanRender(DynamicFeature feature);

    /// <summary>
    /// Produces zero or more Mapsui features for
    /// <paramref name="feature"/>. May return an empty sequence
    /// when the feature is unsupported or degenerate.
    /// </summary>
    IEnumerable<IFeature> Render(DynamicFeature feature);
}
