using System.Runtime.CompilerServices;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// The vector scenes a <see cref="MapsuiDatasetRenderer"/> has built, per
/// processor, keyed by everything that shapes them. When two maps render one
/// processor with the same portrayal, the second map's layer binds the first
/// map's scene instead of building its own. They then share the scene, its
/// planes and indexes, and their chart tiles' keys.
/// </summary>
/// <remarks>
/// <para>
/// Scenes are held weakly. A scene stays cached while a layer is bound to it
/// (the engine's tile state holds it), and drops out once no map shows it.
/// </para>
/// <para>
/// Callers hold the processor's render gate, so one processor's entries are
/// never read and written at the same time.
/// </para>
/// </remarks>
internal sealed class SharedSceneCache
{
    private readonly ConditionalWeakTable<IDatasetProcessor, Dictionary<SharedSceneKey, WeakReference<VectorScene>>> _byProcessor = new();

    /// <summary>
    /// The key for the sub-layer <paramref name="layerKey"/> drawn by <paramref name="renderer"/>,
    /// or <see langword="null"/> when its scene can't be shared: it draws
    /// single-surface, or no content scope identifies the dataset's content.
    /// </summary>
    /// <param name="renderer">The renderer configured for the sub-layer.</param>
    /// <param name="layerKey">The sub-layer's key.</param>
    /// <param name="diskCacheKey">
    /// The sub-layer's tile disk namespace, from
    /// <see cref="MapsuiDisplayListRenderer.DiskCacheKey"/>.
    /// </param>
    /// <returns>The key, or <see langword="null"/>.</returns>
    public static SharedSceneKey? KeyFor(
        MapsuiDisplayListRenderer renderer,
        string layerKey,
        (string? ProductLayerSet, string? StyleStateHash) diskCacheKey)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(layerKey);

        if (!renderer.DrawsTiled)
            return null;

        // The style-state hash covers the resolved instructions (so the
        // display state, time, hidden features and viewing groups that chose
        // them), the palette and the symbol and text scales. The content scope
        // covers the dataset bytes and the catalogue content the symbol, fill
        // and line-style providers resolve from, as it does for the tile disk
        // cache. (The providers themselves are new delegates on every
        // portrayal, so they can't be compared.) The rest of the key is what
        // the scene builder reads besides those.
        var (productLayerSet, styleStateHash) = diskCacheKey;
        if (productLayerSet is null || styleStateHash is null)
            return null;

        return new SharedSceneKey(
            layerKey,
            productLayerSet,
            styleStateHash,
            renderer.OutOfBandMinDisplayScale,
            renderer.PreferLiveDrawing);
    }

    /// <summary>Finds a live scene built for <paramref name="key"/>.</summary>
    /// <param name="processor">The processor the scene portrays.</param>
    /// <param name="key">The scene's key.</param>
    /// <param name="scene">The scene, when found.</param>
    /// <returns><see langword="true"/> when a scene was found.</returns>
    public bool TryGet(IDatasetProcessor processor, SharedSceneKey key, out VectorScene scene)
    {
        ArgumentNullException.ThrowIfNull(processor);

        if (_byProcessor.TryGetValue(processor, out var scenes)
            && scenes.TryGetValue(key, out var weak)
            && weak.TryGetTarget(out var found))
        {
            scene = found;
            return true;
        }

        scene = null!;
        return false;
    }

    /// <summary>Records <paramref name="scene"/> as the scene for <paramref name="key"/>.</summary>
    /// <param name="processor">The processor the scene portrays.</param>
    /// <param name="key">The scene's key.</param>
    /// <param name="scene">The scene.</param>
    public void Put(IDatasetProcessor processor, SharedSceneKey key, VectorScene scene)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(scene);

        var scenes = _byProcessor.GetValue(processor, static _ => []);

        // Drop entries whose scene no map shows any more, so a processor
        // rendered in many style states doesn't keep a key for each.
        List<SharedSceneKey>? dead = null;
        foreach (var (existing, weak) in scenes)
        {
            if (!weak.TryGetTarget(out _))
                (dead ??= []).Add(existing);
        }

        if (dead is not null)
        {
            foreach (var existing in dead)
                scenes.Remove(existing);
        }

        scenes[key] = new WeakReference<VectorScene>(scene);
    }

    /// <summary>The number of keys held for <paramref name="processor"/>, live or not. For tests.</summary>
    internal int CountFor(IDatasetProcessor processor) =>
        _byProcessor.TryGetValue(processor, out var scenes) ? scenes.Count : 0;
}

/// <summary>
/// Everything that shapes a sub-layer's vector scene. Equal keys for one
/// processor build equal scenes.
/// </summary>
/// <param name="LayerKey">The sub-layer's key.</param>
/// <param name="ProductLayerSet">The disk namespace's product and content scope.</param>
/// <param name="StyleStateHash">The hash of the instructions, palette and scales.</param>
/// <param name="OutOfBandMinDisplayScale">The out-of-scale-band cap on the scene.</param>
/// <param name="PreferLiveDrawing">Whether the scene prefers live drawing.</param>
internal readonly record struct SharedSceneKey(
    string LayerKey,
    string ProductLayerSet,
    string StyleStateHash,
    int? OutOfBandMinDisplayScale,
    bool PreferLiveDrawing);
