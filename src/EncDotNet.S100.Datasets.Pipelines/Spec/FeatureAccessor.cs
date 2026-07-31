using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines.Spec;

/// <summary>
/// Extracts a queryable feature collection from a <see cref="LoadedDataset"/>
/// as <see cref="IS100Feature"/> instances. Every GML-encoded S-100 spec
/// implemented in this codebase models its features as
/// <see cref="IS100Feature"/> directly; the ISO 8211-encoded S-101 exposes
/// the same shape through its pipeline <see cref="Feature"/> records (which
/// implement <see cref="IS100Feature"/>). A single accessor — rather than
/// per-spec strategies — therefore powers generic feature-query tools.
/// </summary>
/// <remarks>
/// Returns <c>null</c> for catalog entries that carry no queryable vector
/// features (the coverage products S-102 / S-104 / S-111). Callers should
/// treat <c>null</c> as "this spec does not contribute to feature queries".
/// </remarks>
public static class FeatureAccessor
{
    /// <summary>
    /// Returns the queryable features for the supplied dataset, or
    /// <c>null</c> if the dataset's payload is not a vector feature
    /// collection.
    /// </summary>
    /// <remarks>
    /// For S-101 the features are resolved lazily on enumeration via
    /// <see cref="S101VectorSource"/>, which materialises feature geometry
    /// from the dataset's spatial records as pipeline <see cref="Feature"/>
    /// instances that already satisfy <see cref="IS100Feature"/>.
    /// </remarks>
    public static IEnumerable<IS100Feature>? GetFeatures(LoadedDataset dataset)
        => GetFeatures(dataset, extent: null);

    /// <summary>
    /// Returns the features intersecting <paramref name="extent"/>, or
    /// every feature when <paramref name="extent"/> is
    /// <see langword="null"/>. Same null-semantics as the parameterless
    /// overload: returns <c>null</c> for datasets whose payload is not
    /// a vector feature collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the underlying source implements
    /// <see cref="EncDotNet.S100.Pipelines.Vector.Spatial.IVectorSourceWithIndex"/>
    /// (currently just S-101), an extent query is answered by the
    /// source's spatial index — sub-linear in dataset size. GML-encoded
    /// products fall back to iterating their model's
    /// <c>Features</c> collection and filtering by
    /// <see cref="EncDotNet.S100.Datasets.Pipelines.Spec.FeatureGeometryQuery.TryGetBoundingBox"/>
    /// against <paramref name="extent"/>; adding a per-product index is
    /// a follow-up scoped separately from issue #490.
    /// </para>
    /// </remarks>
    public static IEnumerable<IS100Feature>? GetFeatures(LoadedDataset dataset, BoundingBox? extent)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return dataset.Data switch
        {
            S101DatasetData s101 => S101Features(s101.Dataset, extent),
            S122DatasetData s122 => FilterByExtent(s122.Model.Features, extent),
            S124DatasetData s124 => FilterByExtent(s124.Model.Features, extent),
            S125DatasetData s125 => FilterByExtent(s125.Model.Features, extent),
            S127DatasetData s127 => FilterByExtent(s127.Model.Features, extent),
            S128DatasetData s128 => FilterByExtent(s128.Model.Features, extent),
            S129DatasetData s129 => FilterByExtent(s129.Model.Features, extent),
            S131DatasetData s131 => FilterByExtent(s131.Model.Features, extent),
            S201DatasetData s201 => FilterByExtent(s201.Model.Features, extent),
            S411DatasetData s411 => FilterByExtent(s411.Model.Features, extent),
            S421DatasetData s421 => FilterByExtent(s421.Model.Features, extent),
            _ => null,
        };
    }

    private static IEnumerable<Feature> S101Features(S101Dataset dataset, BoundingBox? extent) =>
        new S101VectorSource(dataset).GetFeatures(extent);

    private static IEnumerable<T> FilterByExtent<T>(
        IEnumerable<T> features, BoundingBox? extent)
        where T : IS100Feature
    {
        if (extent is null)
        {
            return features;
        }

        return FilterByExtentIterator(features, extent);
    }

    private static IEnumerable<T> FilterByExtentIterator<T>(
        IEnumerable<T> features, BoundingBox extent)
        where T : IS100Feature
    {
        foreach (var feature in features)
        {
            var bbox = FeatureGeometryQuery.TryGetBoundingBox(feature);
            // Container-style features (e.g. S-131 Authority) may have
            // no geometry; keep them in the result set so downstream
            // ranking code can still ignore or include them explicitly.
            if (bbox is null || Intersects(bbox, extent))
            {
                yield return feature;
            }
        }
    }

    private static bool Intersects(BoundingBox a, BoundingBox b) =>
        a.NorthLatitude >= b.SouthLatitude
        && a.SouthLatitude <= b.NorthLatitude
        && a.EastLongitude >= b.WestLongitude
        && a.WestLongitude <= b.EastLongitude;
}
