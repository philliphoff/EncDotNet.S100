using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Pipelines;
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
    /// Gets the CRS transform factory used to build the default dataset renderer
    /// (the coverage and arrow renderers project a native grid CRS to
    /// EPSG:3857). Required unless a prebuilt <see cref="DatasetRenderer"/> is
    /// supplied, since that renderer already carries its own. The reusable
    /// assembly ships no CRS implementation, so a host that does not inject a
    /// renderer supplies one here (e.g. <c>ProjNetCrsTransformFactory</c>).
    /// </summary>
    public ICrsTransformFactory? CrsTransformFactory { get; init; }

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
    /// Gets the processor owner from which the session acquires render leases.
    /// When <see langword="null"/>, <see cref="S100MapExtensions.AddS100"/>
    /// creates one and disposes it with the session. When supplied, the session
    /// treats it as borrowed and never disposes it — the caller owns its
    /// lifetime. A DI host passes a shared owner so other services (e.g. a
    /// dataset-loading coordinator) acquire and register processors on the same
    /// owner as the map session.
    /// </summary>
    public DatasetProcessorOwner? ProcessorOwner { get; init; }

    /// <summary>
    /// Gets the prebuilt processor-to-Mapsui dataset renderer. When
    /// <see langword="null"/>, <see cref="S100MapExtensions.AddS100"/> builds one
    /// from <see cref="CrsTransformFactory"/> and <see cref="PatternClipCache"/>.
    /// When supplied, <see cref="CrsTransformFactory"/> is unused (the renderer
    /// already carries its own). The renderer is not disposable, so ownership
    /// carries no disposal obligation; a DI host passes a shared renderer.
    /// </summary>
    public MapsuiDatasetRenderer? DatasetRenderer { get; init; }

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
    /// Gets the marshal used to run the session's redraw on the host's UI thread.
    /// The S-100 vector renderers rasterise cached / scene / tile output on
    /// background threads; when a settled image publishes, the session invalidates
    /// the attached map (<c>Map.RefreshGraphics()</c>) to bring it on screen. A UI
    /// host whose control must be invalidated on its dispatcher thread supplies a
    /// marshal (e.g. one that posts to the UI thread); when <see langword="null"/>
    /// the redraw runs inline on the publishing thread, which suits headless or
    /// single-threaded hosts.
    /// </summary>
    public Action<Action>? RedrawMarshal { get; init; }

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
