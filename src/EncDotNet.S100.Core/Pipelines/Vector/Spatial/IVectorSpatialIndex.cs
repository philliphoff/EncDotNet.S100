namespace EncDotNet.S100.Pipelines.Vector.Spatial;

/// <summary>
/// A spatial index built once over an <see cref="IVectorSource"/>'s
/// features and queried by extent. Implementations are expected to
/// answer <see cref="Query(BoundingBox)"/> in time sub-linear in
/// <see cref="Count"/> for typical dataset densities.
/// </summary>
/// <remarks>
/// <para>
/// The index is queried by axis-aligned <see cref="BoundingBox"/>
/// overlap on each feature's geometry MBR. That matches the semantics
/// of the pre-existing linear <c>IntersectsExtent</c> scan in
/// <see cref="EncDotNet.S100.Datasets.S101.S101VectorSource"/> for
/// point features, and — because the feature MBR is the axis-aligned
/// hull of the vertex list — includes any feature whose geometry
/// crosses the query edge, matching real-world "features intersecting
/// this viewport / pick-box" semantics.
/// </para>
/// </remarks>
public interface IVectorSpatialIndex
{
    /// <summary>
    /// Builds an index over <paramref name="features"/>. The concrete
    /// implementation is a
    /// <see cref="Spatial.StrRTree">STR-packed R-tree</see> (private) —
    /// callers depend only on this interface.
    /// </summary>
    /// <param name="features">Features to index.</param>
    /// <param name="productTag">
    /// Optional value for the <c>s100.product</c> tag attached to
    /// build/query telemetry emitted through
    /// <see cref="EncDotNet.S100.Diagnostics.PipelineMetrics"/>.
    /// </param>
    public static IVectorSpatialIndex Build(
        IReadOnlyList<Feature> features, string? productTag = null)
        => Spatial.StrRTree.Build(features, productTag);

    /// <summary>Number of indexed features.</summary>
    int Count { get; }

    /// <summary>Union MBR of all indexed feature geometries; <see langword="null"/> if empty.</summary>
    BoundingBox? Extent { get; }

    /// <summary>
    /// Returns every indexed feature whose geometry MBR overlaps
    /// <paramref name="extent"/>. Edge-touching MBRs are included
    /// (closed-interval intersection).
    /// </summary>
    IReadOnlyList<Feature> Query(BoundingBox extent);

    /// <summary>Enumerates every indexed feature in insertion order.</summary>
    IReadOnlyList<Feature> All();
}
