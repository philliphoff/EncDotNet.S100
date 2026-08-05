using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines.Geometry;

/// <summary>
/// Shared geographic hit-testing over encoding-neutral
/// <see cref="IS100Feature"/> geometry, used by dataset processors to implement
/// <see cref="IDatasetProcessor.HitTestFeatures"/>. Builds on the precise,
/// hole-aware, segment-based <see cref="GeometryDistance.Measure"/>, so a pick
/// matches an area feature by exact point-in-polygon containment and a
/// point/curve feature by nearest-geometry distance.
/// </summary>
public static class FeatureHitTester
{
    /// <summary>
    /// Returns every feature in <paramref name="features"/> whose geometry
    /// contains the point (area features) or lies within
    /// <paramref name="radiusMeters"/> of it (point/curve features), in
    /// enumeration order. Each hit's <see cref="FeatureGeometryHit.Ordinal"/>
    /// is the feature's zero-based position in <paramref name="features"/>, so
    /// it aligns with <see cref="IDatasetProcessor.GetFeatureInfoAt"/> when the
    /// caller enumerates the features in the same order that method indexes.
    /// </summary>
    /// <param name="features">The dataset's features, in ordinal order.</param>
    /// <param name="latitude">Pick latitude in WGS-84 degrees.</param>
    /// <param name="longitude">Pick longitude in WGS-84 degrees.</param>
    /// <param name="radiusMeters">
    /// Search tolerance for point/curve features in metres; area features use
    /// exact containment and ignore it. Negative or non-finite values are
    /// treated as 0.
    /// </param>
    /// <returns>The matching features in enumeration order, or an empty list.</returns>
    public static IReadOnlyList<FeatureGeometryHit> HitTest<TFeature>(
        IEnumerable<TFeature> features,
        double latitude,
        double longitude,
        double radiusMeters)
        where TFeature : IS100Feature
    {
        ArgumentNullException.ThrowIfNull(features);

        var radius = double.IsFinite(radiusMeters) ? Math.Max(0.0, radiusMeters) : 0.0;
        var point = new GeoPoint(latitude, longitude);

        List<FeatureGeometryHit>? hits = null;
        var ordinal = 0;
        foreach (var feature in features)
        {
            if (feature is not null
                && GeometryDistance.Measure(feature, point) is { } distance
                && (distance.Inside || distance.DistanceMeters <= radius))
            {
                (hits ??= []).Add(new FeatureGeometryHit
                {
                    FeatureRef = feature.Id,
                    Ordinal = ordinal,
                    FeatureType = feature.FeatureType,
                    Primitive = distance.Primitive,
                    Inside = distance.Inside,
                    DistanceMeters = distance.Inside ? 0.0 : distance.DistanceMeters,
                });
            }

            ordinal++;
        }

        if (hits is null)
        {
            return System.Array.Empty<FeatureGeometryHit>();
        }

        return hits;
    }
}
