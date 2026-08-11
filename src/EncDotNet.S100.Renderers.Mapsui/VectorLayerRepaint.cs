using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Shared routing for a background-publish repaint from the S-100 vector
/// renderers (snapshot / scene / tile) to a layer, so the fallback behaviour
/// lives in one place rather than being duplicated per renderer.
/// </summary>
internal static class VectorLayerRepaint
{
    /// <summary>
    /// Requests a repaint of <paramref name="layer"/>. An
    /// <see cref="InstrumentedMemoryLayer"/> repaints via its
    /// <see cref="InstrumentedMemoryLayer.RequestRepaint"/> (which invokes the
    /// session-stamped <see cref="InstrumentedMemoryLayer.RequestRedraw"/> sink,
    /// or <c>DataHasChanged()</c> when none was wired); any other layer falls
    /// back to <see cref="BaseLayer.DataHasChanged"/>.
    /// </summary>
    public static void Request(ILayer layer)
    {
        if (layer is InstrumentedMemoryLayer instrumented)
        {
            instrumented.RequestRepaint();
            return;
        }

        (layer as BaseLayer)?.DataHasChanged();
    }
}
