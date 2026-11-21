using EncDotNet.S100.Rendering.Engine;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// The Mapsui adapter over the host-neutral chart engine
/// (<c>EncDotNet.S100.Rendering.Engine</c>, map rendering slice&#160;7). It
/// registers the engine's tile and single-surface compositors as Mapsui custom
/// layer renderers, turns Mapsui's viewport and layer into the engine's
/// <see cref="ViewState"/> and <see cref="LayerFrame"/>, and routes the
/// engine's repaint requests back to the layer. The layer object itself is the
/// engine's owner token, so a scene bound with
/// <see cref="S100VectorTileRenderer.BindScene(object, Rendering.Scene.VectorScene)"/>
/// for a layer is the scene this adapter draws for it.
/// </summary>
public static class MapsuiChartLayers
{
    // Installed as soon as the adapter is used, not only by Register, so a
    // layer drawn through RenderTiles or RenderScene always gets its repaints,
    // as it did when the renderer called VectorLayerRepaint directly.
    static MapsuiChartLayers() => LayerRepaint.SetHandler(RequestRepaint);

    /// <summary>
    /// Registers the engine's compositors with Mapsui under
    /// <see cref="S100VectorTileRenderer.RendererName"/> and
    /// <see cref="S100VectorSceneRenderer.RendererName"/>, and installs the
    /// repaint handler. Idempotent; <see cref="S100MapsuiRendering.Register"/>
    /// calls it.
    /// </summary>
    internal static void Register()
    {
        LayerRepaint.SetHandler(RequestRepaint);
        MapRenderer.RegisterLayerRenderer(S100VectorSceneRenderer.RendererName, RenderScene);
        MapRenderer.RegisterLayerRenderer(S100VectorTileRenderer.RendererName, RenderTiles);
    }

    /// <summary>
    /// The Mapsui custom layer renderer for tiled layers: draws
    /// <paramref name="layer"/> through <see cref="S100VectorTileRenderer.Render"/>.
    /// </summary>
    /// <param name="canvas">The canvas Mapsui renders the frame into.</param>
    /// <param name="viewport">Mapsui's viewport for the frame.</param>
    /// <param name="layer">The layer to draw; its scene was bound with the layer as owner.</param>
    /// <param name="renderService">Mapsui's render service (unused).</param>
    public static void RenderTiles(SKCanvas canvas, Viewport viewport, ILayer layer, RenderService renderService)
    {
        ArgumentNullException.ThrowIfNull(layer);
        S100VectorTileRenderer.Render(canvas, ToViewState(viewport), ToFrame(layer));
    }

    /// <summary>
    /// The Mapsui custom layer renderer for single-surface layers: draws
    /// <paramref name="layer"/> through <see cref="S100VectorSceneRenderer.Render"/>.
    /// </summary>
    /// <param name="canvas">The canvas Mapsui renders the frame into.</param>
    /// <param name="viewport">Mapsui's viewport for the frame.</param>
    /// <param name="layer">The layer to draw; its scene was bound with the layer as owner.</param>
    /// <param name="renderService">Mapsui's render service (unused).</param>
    public static void RenderScene(SKCanvas canvas, Viewport viewport, ILayer layer, RenderService renderService)
    {
        ArgumentNullException.ThrowIfNull(layer);
        S100VectorSceneRenderer.Render(canvas, ToViewState(viewport), ToFrame(layer));
    }

    /// <summary>
    /// Reports whether the tiled layers among <paramref name="layers"/> have
    /// settled; see <see cref="S100VectorTileRenderer.EvaluateSettle"/>.
    /// </summary>
    /// <param name="layers">The map's layers, for example a snapshot of <c>Map.Layers</c>.</param>
    /// <returns>The settle state.</returns>
    public static TileSettleState EvaluateSettle(IEnumerable<ILayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        return S100VectorTileRenderer.EvaluateSettle(layers
            .Where(static l => l is not null)
            .Select(static l => new SettleLayer(l, l.Enabled, l.MinVisible, l.MaxVisible)));
    }

    private static void RequestRepaint(object owner)
    {
        if (owner is ILayer layer)
        {
            VectorLayerRepaint.Request(layer);
        }
    }

    /// <summary>Converts a Mapsui viewport to the engine's view.</summary>
    /// <param name="viewport">The Mapsui viewport.</param>
    /// <returns>The same view as a <see cref="ViewState"/>.</returns>
    public static ViewState ToViewState(Viewport viewport) =>
        new(viewport.CenterX, viewport.CenterY, viewport.Resolution, viewport.Rotation, viewport.Width, viewport.Height);

    /// <summary>Describes a Mapsui layer to the engine, with the layer as its owner token.</summary>
    /// <param name="layer">The layer.</param>
    /// <returns>The layer's frame description.</returns>
    public static LayerFrame ToFrame(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var extent = layer.Extent;
        return new LayerFrame(
            layer,
            layer.Name,
            layer.Opacity,
            extent is null ? null : new WorldRect(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY));
    }
}
