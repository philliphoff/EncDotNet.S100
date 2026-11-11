namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// How the engine asks its host to draw a layer again. The engine identifies a
/// layer only by an opaque owner token (the object the host passed to
/// <see cref="S100VectorTileRenderer.BindScene(object, Rendering.Scene.VectorScene)"/>
/// or a render call); it never calls into it. When tiles for an owner finish
/// rasterising, or a frame finds its view out of date, the engine passes that
/// token to the handler a host installs here, and the host repaints whatever
/// the token stands for.
/// </summary>
public static class LayerRepaint
{
    private static volatile Action<object>? _handler;

    /// <summary>
    /// Installs the host's repaint callback, replacing any earlier one. The
    /// callback can be invoked from any thread, including the engine's tile
    /// workers, so it must be thread-safe and cheap. A host typically installs
    /// it once at startup.
    /// </summary>
    /// <param name="handler">Invoked with the owner token of the layer to repaint.</param>
    public static void SetHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <summary>Asks the host to repaint <paramref name="owner"/>. Does nothing before a handler is installed.</summary>
    internal static void Request(object owner) => _handler?.Invoke(owner);
}

/// <summary>
/// What a host tells the engine about one layer when it asks the engine to
/// draw it for a frame.
/// </summary>
/// <param name="Owner">
/// The opaque identity of the layer: the same object the host bound the
/// layer's scene with. The engine keys its per-layer state on it, weakly, and
/// hands it back through <see cref="LayerRepaint"/>.
/// </param>
/// <param name="Name">A display name for traces, or <see langword="null"/>.</param>
/// <param name="Opacity">Layer opacity, 0 to 1; below 1 the layer is drawn through one alpha layer.</param>
/// <param name="Extent">The layer's data extent in EPSG:3857, or <see langword="null"/> when unknown (never culled).</param>
public readonly record struct LayerFrame(object Owner, string? Name, double Opacity, WorldRect? Extent);
