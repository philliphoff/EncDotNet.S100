using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Viewport-extent culling for the TiledScene ("B") custom layer renderers.
/// </summary>
/// <remarks>
/// <para>
/// Mapsui's render loop invokes a layer's custom renderer for <i>every</i>
/// enabled, resolution-visible layer each frame — it never extent-culls a
/// custom-renderer layer (only the default per-feature path filters by extent,
/// inside <c>GetFeatures</c>). With an exchange set of many S-101 cells loaded
/// together that means the renderer's <c>Render</c> runs once per cell per
/// frame, including cells whose data lies entirely outside the viewport. For
/// the tiled arm each such off-view call still schedules (empty) tile workers,
/// reconciles GPU residency, composites, and re-draws its live symbol overlay —
/// and every off-view worker publish fires a full-map repaint — so multi-cell
/// pan/zoom becomes laggier than the snapshot arm, which only blits one cached
/// image per layer.
/// </para>
/// <para>
/// This helper lets each B renderer skip a layer whose data extent does not
/// intersect the current viewport (grown by an over-render margin), so off-view
/// cells contribute zero render-thread and worker work.
/// </para>
/// </remarks>
internal static class LayerExtentCulling
{
    /// <summary>
    /// Pure axis-aligned box-intersection test in EPSG:3857 metres. The layer
    /// box is treated as enlarged by <paramref name="marginWorld"/> on every
    /// edge so data whose geometry sits just outside the viewport — but whose
    /// symbols / over-render reach in — is not culled.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the (margin-grown) layer box overlaps the
    /// viewport box.
    /// </returns>
    internal static bool Intersects(
        double layerMinX, double layerMinY, double layerMaxX, double layerMaxY,
        double vpMinX, double vpMinY, double vpMaxX, double vpMaxY,
        double marginWorld)
    {
        return layerMaxX + marginWorld >= vpMinX
            && layerMinX - marginWorld <= vpMaxX
            && layerMaxY + marginWorld >= vpMinY
            && layerMinY - marginWorld <= vpMaxY;
    }

    /// <summary>
    /// Decides whether <paramref name="layer"/> may contribute to the current
    /// frame and therefore warrants the renderer's per-frame work. A layer with
    /// no known extent (<see cref="ILayer.Extent"/> <see langword="null"/>, e.g.
    /// a geometry-less container) is never culled. Both the layer extent and the
    /// viewport are in EPSG:3857 (the viewer's map CRS), so no projection is
    /// needed.
    /// </summary>
    /// <param name="layer">The Mapsui layer being rendered.</param>
    /// <param name="viewport">The live viewport.</param>
    /// <param name="resolution">The viewport resolution (metres / DIP).</param>
    /// <param name="marginPx">
    /// Over-render halo, in screen pixels, added around the viewport before the
    /// intersection test (converted to world metres via
    /// <paramref name="resolution"/>).
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the layer should be rendered this frame;
    /// <see langword="false"/> when it can be skipped entirely.
    /// </returns>
    internal static bool ShouldRender(ILayer layer, Viewport viewport, double resolution, double marginPx)
    {
        var extent = layer.Extent;
        if (extent is null)
        {
            return true;
        }

        var vp = viewport.ToExtent();
        if (vp is null)
        {
            return true;
        }

        var marginWorld = marginPx * resolution;
        return Intersects(
            extent.MinX, extent.MinY, extent.MaxX, extent.MaxY,
            vp.MinX, vp.MinY, vp.MaxX, vp.MaxY,
            marginWorld);
    }
}
