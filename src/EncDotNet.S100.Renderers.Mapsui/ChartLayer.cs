using EncDotNet.S100.Rendering.Engine;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// The one Mapsui layer that draws a session's tiled chart content (map
/// rendering slice&#160;8): every cell's base plane goes into one chart tile
/// per slot, drawn in stack order with finer-cell coverage clipping applied,
/// and every cell's symbols and text are drawn live on top. It replaces the
/// per-cell, per-sub-layer Mapsui layers on the map.
/// </summary>
/// <remarks>
/// <para>
/// The layer is an owner token for the engine: its
/// <see cref="ChartSession"/> publishes the <see cref="ChartStack"/> it draws,
/// and the engine's tile compositor draws it through
/// <see cref="MapsuiChartLayers.RenderTiles"/>. A cell's visibility, opacity,
/// scale window and S-98 suppression are settings of its stack layers, not
/// properties of this layer.
/// </para>
/// <para>
/// The layer has no Mapsui features: picks go through the engine and the
/// datasets' own spatial indexes (<see cref="IS100MapQuery"/>).
/// </para>
/// </remarks>
public sealed class ChartLayer : BaseLayer
{
    /// <summary>Creates a chart layer.</summary>
    public ChartLayer()
    {
        Name = "S-100 chart";
        Style = null;
        CustomLayerRendererName = S100VectorTileRenderer.RendererName;
    }

    /// <summary>
    /// The stack this layer draws, in the order published (bottom first), or
    /// an empty stack before the first publish.
    /// </summary>
    public ChartStack Stack { get; internal set; } = ChartStack.Empty;

    /// <summary>
    /// The redraw action a session stamps on the layer, invoked (possibly from
    /// a worker) when a chart tile lands. Falls back to
    /// <see cref="BaseLayer.DataHasChanged"/> when unset.
    /// </summary>
    public Action? RequestRedraw { get; set; }

    /// <inheritdoc />
    public override MRect? Extent =>
        ChartSession.GetExtent(this) is { } e ? new MRect(e.MinX, e.MinY, e.MaxX, e.MaxY) : null;

    /// <inheritdoc />
    public override IEnumerable<IFeature> GetFeatures(MRect extent, double resolution) => [];

    /// <summary>Asks the host to repaint the map.</summary>
    internal void RequestRepaint()
    {
        if (RequestRedraw is { } redraw)
        {
            redraw();
            return;
        }

        DataHasChanged();
    }
}
