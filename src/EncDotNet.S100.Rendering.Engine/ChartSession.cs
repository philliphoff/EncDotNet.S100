using EncDotNet.S100.Rendering.Engine.Prediction;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// The engine's root object for one map (map rendering slice&#160;8): it owns
/// the map's <see cref="ChartSessionOptions"/> and publishes chart stacks to
/// the chart layers that draw them. Each chart layer draws one chart tile per
/// slot (see <see cref="ChartTileKey"/>) over every layer of its stack.
/// </summary>
/// <remarks>
/// <para>
/// A host creates one session per map. The process-wide resources (the
/// work scheduler, the tile memory pool, the warm disk cache and the GPU
/// budget) stay shared across sessions; the session's options decide its own
/// tile behaviour: prediction, cross-band pre-warm, metatiles, the disk cache
/// and GPU residency.
/// </para>
/// <para>
/// A chart layer is identified by an owner token, as the host's map layer
/// was in slice&#160;7. Draw it with
/// <see cref="S100VectorTileRenderer.Render(SkiaSharp.SKCanvas, ViewState, LayerFrame)"/>.
/// </para>
/// </remarks>
public sealed class ChartSession
{
    private readonly object _sync = new();
    private readonly Dictionary<object, ChartStack> _stacks = new(ReferenceEqualityComparer.Instance);

    /// <summary>Creates a session.</summary>
    /// <param name="options">
    /// The session's options. A host with one map may pass the process-wide
    /// <see cref="ChartEngine.Options"/>, so its settings UI keeps one source
    /// of truth.
    /// </param>
    /// <param name="planner">
    /// The map's demand planner (map rendering slice&#160;9): its predicted
    /// views add tile demand, and its arrival forecast batches stack versions.
    /// A new planner when null.
    /// </param>
    public ChartSession(ChartSessionOptions options, DemandPlanner? planner = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        Planner = planner ?? new DemandPlanner();
    }

    /// <summary>The session's options.</summary>
    public ChartSessionOptions Options { get; }

    /// <summary>The map's demand planner.</summary>
    public DemandPlanner Planner { get; }

    /// <summary>
    /// Publishes <paramref name="stack"/> to the chart layer
    /// <paramref name="owner"/>. Slots the change doesn't touch keep their
    /// tiles; a changed slot shows its old tile until its new one lands, so a
    /// slot changes from one complete image to the next.
    /// </summary>
    /// <param name="owner">The chart layer's owner token.</param>
    /// <param name="stack">The stack, bottom layer first.</param>
    public void Publish(object owner, ChartStack stack)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(stack);
        lock (_sync)
        {
            if (_stacks.TryGetValue(owner, out var current) && current.Version > stack.Version)
            {
                return;
            }

            _stacks[owner] = stack;
        }

        S100VectorTileRenderer.BindStack(owner, stack, Options, Planner);
    }

    /// <summary>Forgets <paramref name="owner"/>: it is no longer on the map.</summary>
    /// <param name="owner">The chart layer's owner token.</param>
    public void Remove(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_sync)
        {
            _stacks.Remove(owner);
        }
    }

    /// <summary>The stack last published to <paramref name="owner"/>, or null.</summary>
    /// <param name="owner">The chart layer's owner token.</param>
    /// <returns>The stack, or <see langword="null"/>.</returns>
    public ChartStack? GetStack(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_sync)
        {
            return _stacks.GetValueOrDefault(owner);
        }
    }

    /// <summary>
    /// The world extent (EPSG:3857) of the base plane of the stack drawn by
    /// <paramref name="owner"/>, or null before a stack with content is bound.
    /// </summary>
    /// <param name="owner">The chart layer's owner token.</param>
    /// <returns>The extent, or <see langword="null"/>.</returns>
    public static WorldRect? GetExtent(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return S100VectorTileRenderer.ChartExtent(owner);
    }
}
