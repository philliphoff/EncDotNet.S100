using System.Collections.Generic;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Tiling.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Experimental wrapper around <see cref="RasterizingTileLayer"/> that
/// keeps picking working when the source vector layer is rasterised to
/// a tile cache. The base class draws cached PNG tiles for performance,
/// but stops returning the underlying vector features from
/// <see cref="MemoryLayer.GetFeatures"/>; Mapsui's <c>MapInfo</c> hit
/// detection therefore can't find the features the user clicks on.
/// This subclass forwards <c>GetFeatures</c> to <see cref="SourceLayer"/>
/// so picks resolve to the original <c>IFeature</c> instances (carrying
/// the <c>FeatureRefKey</c> tag the viewer's <c>PickService</c> uses).
/// </summary>
/// <remarks>
/// <para>
/// Per-frame rendering is unaffected — Mapsui's tile-render path uses
/// <c>RasterizingTileSource</c>, not <c>GetFeatures</c>. Only hit
/// detection (one-shot per click) calls into the source's full
/// extent-filter loop.
/// </para>
/// <para>
/// This is a prototype gated by <c>ViewerSettings.EnableVectorRasterization</c>
/// so we can A/B compare frame rate, visual quality, and pick latency
/// against the un-wrapped path.
/// </para>
/// </remarks>
public sealed class S100RasterizingTileLayer : RasterizingTileLayer
{
    /// <summary>
    /// Wraps <paramref name="sourceLayer"/> in a tile-cached rasteriser.
    /// All other <see cref="RasterizingTileLayer"/> defaults are
    /// preserved so the prototype isolates the rasterisation effect.
    /// </summary>
    public S100RasterizingTileLayer(ILayer sourceLayer)
        : base(sourceLayer)
    {
    }

    /// <inheritdoc />
    public override IEnumerable<IFeature> GetFeatures(MRect? rect, double resolution)
        => rect is null
            ? System.Array.Empty<IFeature>()
            : SourceLayer.GetFeatures(rect, resolution);
}
