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
}
