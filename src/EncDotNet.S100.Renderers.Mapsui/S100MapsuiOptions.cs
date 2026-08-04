using EncDotNet.S100.Datasets.Pipelines.Interoperability;

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
public sealed class S100MapsuiOptions
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
}
