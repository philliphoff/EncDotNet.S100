namespace EncDotNet.S100.Pipelines.Vector.Spatial;

/// <summary>
/// Optional refinement of <see cref="IVectorSource"/> for sources that
/// maintain a persistent spatial index over their features.
/// </summary>
/// <remarks>
/// <para>
/// Additive by design: existing <see cref="IVectorSource"/>
/// implementations (S-124, S-125, S-127, S-131, S-201, S-411, S-421)
/// keep working unchanged. Sources that opt in expose the same
/// spatial index used internally by their <see cref="IVectorSource.GetFeatures(BoundingBox?)"/>
/// override, so external callers (identify, MCP query tools) can bypass
/// the linear scan without knowing about the concrete source type.
/// </para>
/// <para>
/// Discovery via <see cref="VectorSourceExtensions.TryGetIndex(IVectorSource)"/>
/// keeps consumers from depending on the interface directly.
/// </para>
/// </remarks>
public interface IVectorSourceWithIndex : IVectorSource
{
    /// <summary>
    /// The persistent spatial index over this source's features. May
    /// be built lazily on first access; implementations must return
    /// the same instance for every call once built.
    /// </summary>
    IVectorSpatialIndex Index { get; }
}
