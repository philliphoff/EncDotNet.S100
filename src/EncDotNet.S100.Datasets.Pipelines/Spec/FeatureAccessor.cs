using System.Runtime.CompilerServices;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S412;
using EncDotNet.S100.Datasets.S412.DataModel;
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
/// implement <see cref="IS100Feature"/>), and CAP weather warnings expose
/// each area as a surface feature. A single accessor — rather than
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
        => GetFeatures(dataset, extent, time: null);

    /// <summary>
    /// Returns the features intersecting <paramref name="extent"/> that a
    /// render at <paramref name="time"/> draws, or every drawable feature
    /// when <paramref name="time"/> is <see langword="null"/> (as a render
    /// with no time step draws them). Same null-semantics as the other
    /// overloads.
    /// </summary>
    /// <param name="dataset">The dataset.</param>
    /// <param name="extent">The extent, or <see langword="null"/> for the whole dataset.</param>
    /// <param name="time">
    /// The view time (UTC), or <see langword="null"/> for none. Only the
    /// products whose drawing depends on the time narrow their features:
    /// S-412 warnings in force then, CAP areas in force then, the surface
    /// analysis valid then and the tropical cyclone's position then. Other
    /// products, S-124 among them, return the same features whatever the
    /// time: a render draws every S-124 part unless the host hides some
    /// (see <see cref="Query.IMapDrawState.HiddenFeatures"/>).
    /// </param>
    public static IEnumerable<IS100Feature>? GetFeatures(LoadedDataset dataset, BoundingBox? extent, DateTimeOffset? time)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var timeStep = time?.UtcDateTime;
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
            // S-412 warnings: those in force at the time, or every warning
            // with no time.
            S412DatasetData s412 => FilterByExtent(S412DrawnAt(s412.Model, time), extent),
            S421DatasetData s421 => FilterByExtent(s421.Model.Features, extent),
            // CAP warnings: the areas in force at the time, or, with no time,
            // every area not superseded or cancelled.
            CapDatasetData cap => FilterByExtent(cap.Warnings.DrawnAt(time).Select(a => new CapAreaFeature(a)), extent),
            // Surface analyses: the fronts and centres of the analysis drawn
            // at the time (with no time, the bulletin's first).
            SurfaceAnalysisDatasetData analysis => FilterByExtent(SurfaceAnalysisDatasetProcessor.DrawnFeatures(analysis.Bulletin, timeStep), extent),
            // Tropical cyclones: every drawn part of the storm, with its
            // position at the time (#1296).
            TropicalCycloneDatasetData tc => FilterByExtent(TropicalCycloneDatasetProcessor.DrawnFeatures(tc.Advisory, timeStep), extent),
            // AML features keep their S-57 classes; they're never S-101 (#1372).
            AmlDatasetData { Cell: { } aml } => FilterByExtent(aml.Features, extent),
            _ => null,
        };
    }

    /// <summary>
    /// The ids of the features of <paramref name="dataset"/> a render that
    /// hides <paramref name="hidden"/> (<see cref="Query.IMapDrawState.HiddenFeatures"/>)
    /// neither draws nor picks: for S-124 the hidden parts with the areas and
    /// texts only they reference (all of the dataset when every part is
    /// hidden), else <paramref name="hidden"/> itself.
    /// </summary>
    /// <param name="dataset">The dataset.</param>
    /// <param name="hidden">The features the host hides, or <see langword="null"/> for none.</param>
    /// <returns>The ids, or <see langword="null"/> when nothing is left out.</returns>
    public static IReadOnlySet<string>? UndrawnFeatures(LoadedDataset dataset, IReadOnlySet<string>? hidden)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (hidden is not { Count: > 0 })
        {
            return null;
        }

        return dataset.Data is S124DatasetData s124
            ? S124DatasetProcessor.UndrawnFeaturesOf(s124.Model, hidden)
            : hidden;
    }

    // The typed warnings of each S-412 dataset, built once: the drawn set is
    // asked for on every identify and pick.
    private static readonly ConditionalWeakTable<S412Dataset, S412MarineWeatherWarnings> S412Warnings = new();

    private static IEnumerable<S412Feature> S412DrawnAt(S412Dataset dataset, DateTimeOffset? time)
    {
        if (time is null)
        {
            return dataset.Features;
        }

        var warnings = S412Warnings.GetValue(dataset, d => S412MarineWeatherWarnings.From(d, out _));
        var drawn = warnings.DrawnAt(time).Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        return dataset.Features.Where(f => drawn.Contains(f.Id));
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
