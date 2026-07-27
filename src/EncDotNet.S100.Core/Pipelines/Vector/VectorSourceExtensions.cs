using EncDotNet.S100.Pipelines.Vector.Spatial;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Extension methods on <see cref="IVectorSource"/> that let callers
/// benefit from a persistent spatial index without depending on
/// <see cref="IVectorSourceWithIndex"/> at their call sites.
/// </summary>
public static class VectorSourceExtensions
{
    /// <summary>
    /// Returns the source's spatial index when it exposes one, or
    /// <see langword="null"/> otherwise. Sources implementing
    /// <see cref="IVectorSourceWithIndex"/> are the current opt-in
    /// path; every other <see cref="IVectorSource"/> returns
    /// <see langword="null"/> and callers should fall back to the
    /// linear <see cref="IVectorSource.GetFeatures(BoundingBox?)"/> scan.
    /// </summary>
    public static IVectorSpatialIndex? TryGetIndex(this IVectorSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source is IVectorSourceWithIndex withIndex ? withIndex.Index : null;
    }

    /// <summary>
    /// Convenience: query the source's features by extent, using its
    /// spatial index when available and falling back to
    /// <see cref="IVectorSource.GetFeatures(BoundingBox?)"/> otherwise.
    /// </summary>
    /// <remarks>
    /// Sources that already route <c>GetFeatures(extent)</c> through
    /// the index (like <see cref="EncDotNet.S100.Datasets.S101.S101VectorSource"/>)
    /// see no behaviour change from calling this — it's provided so
    /// callers holding only an <see cref="IVectorSource"/> reference
    /// don't need to know whether the source is index-backed.
    /// </remarks>
    public static IReadOnlyList<Feature> QueryByExtent(
        this IVectorSource source, BoundingBox extent)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(extent);

        var index = source.TryGetIndex();
        return index is not null ? index.Query(extent) : source.GetFeatures(extent);
    }
}
