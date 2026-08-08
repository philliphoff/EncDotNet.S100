using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Captures Mapsui-specific S-100 rendering configuration for a renderer or
/// map session.
/// </summary>
/// <remarks>
/// Defaults are copied from the environment-backed
/// <see cref="RenderingOptimizations"/> store when an instance is created.
/// Hosts can override individual values without mutating process-global state.
/// Additional renderer settings will move onto this type incrementally as the
/// reusable Mapsui session API evolves.
/// </remarks>
public sealed record S100MapsuiOptions
{
    /// <summary>
    /// Gets the base-plane chart render subsystem.
    /// </summary>
    public RenderSubsystemKind RenderSubsystem { get; init; } =
        RenderingOptimizations.RenderSubsystem;

    /// <summary>
    /// Gets the vector-scene rendering mode used by the
    /// <see cref="RenderSubsystemKind.TiledScene"/> subsystem.
    /// </summary>
    public VectorSceneMode SceneMode { get; init; } =
        RenderingOptimizations.SceneMode;

    /// <summary>
    /// Gets the runtime S-98 cross-product ordering and suppression authority
    /// provider. When <see langword="null"/>,
    /// <see cref="S100MapExtensions.AddS100"/> uses a default runtime authority.
    /// </summary>
    public IInteroperabilityAuthorityProvider? InteroperabilityAuthorityProvider { get; init; }

    /// <summary>
    /// Gets the process-wide pattern-fill priority-clip cache shared by the
    /// dataset renderer. When <see langword="null"/> an in-memory single-slot
    /// cache is used for the session's lifetime.
    /// </summary>
    public IPatternClipCache? PatternClipCache { get; init; }

    /// <summary>
    /// Gets the dataset processor factory used to build processors when loading
    /// datasets from a path (<see cref="IS100DatasetLoader.LoadAsync"/>). The
    /// host chooses which products and portrayal catalogues the factory supports
    /// — e.g. the built-in <c>DatasetPipelineFactory</c> from
    /// <c>EncDotNet.S100.Datasets.Pipelines</c>, or one registering only a subset
    /// of products. When <see langword="null"/>, path-based loading throws; the
    /// caller can still add pre-built processors via
    /// <see cref="IS100MapSession.AddDatasetAsync"/>.
    /// </summary>
    public IDatasetProcessorFactory? DatasetPipelineFactory { get; init; }

    /// <summary>
    /// Gets the marshal used by the session's dynamic-source host to run overlay
    /// mutations on the map thread. Dynamic sources publish changes from
    /// arbitrary threads, so a UI host supplies a dispatcher-backed marshal
    /// (e.g. one that posts to the UI thread). When <see langword="null"/>, the
    /// host runs mutations inline (synchronously) on the caller's thread, which
    /// suits headless or single-threaded hosts.
    /// </summary>
    public Action<Action>? DynamicSourceMarshal { get; init; }

    /// <summary>
    /// Gets the resolver the session's dynamic-source host uses to find an
    /// <see cref="IDynamicFeatureRenderer"/> for a source's
    /// <see cref="EncDotNet.S100.DynamicSources.DynamicSourceMetadata.RendererKey"/>.
    /// Returns <see langword="null"/> for an unknown key to fall back to the
    /// default renderer. When the whole delegate is <see langword="null"/>, every
    /// source uses the default renderer. A DI host typically passes a resolver
    /// over its keyed services.
    /// </summary>
    public Func<string?, IDynamicFeatureRenderer?>? DynamicFeatureRendererResolver { get; init; }

    /// <summary>
    /// Gets the minimum interval between full rebuilds of a single dynamic
    /// source's overlay layer (a coalescing throttle for high-frequency
    /// sources). When <see langword="null"/>, the host's default (250 ms) is
    /// used.
    /// </summary>
    public TimeSpan? DynamicSourceCoalesceWindow { get; init; }
}
