using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A single vector feature found at a geographic point by
/// <see cref="IDatasetProcessor.HitTestFeatures"/> — the reusable,
/// renderer-neutral unit of a geographic pick. Carries just enough to rank the
/// hit and to resolve the full <see cref="FeatureInfo"/> from the same
/// processor via <see cref="IDatasetProcessor.GetFeatureInfoAt"/>
/// (collision-free) or <see cref="IDatasetProcessor.GetFeatureInfo"/>.
/// </summary>
public sealed record FeatureGeometryHit
{
    /// <summary>The feature reference string (dataset-specific id).</summary>
    public required string FeatureRef { get; init; }

    /// <summary>
    /// The feature's position within the processor's feature enumeration — the
    /// same index accepted by <see cref="IDatasetProcessor.GetFeatureInfoAt"/>
    /// and reported by <see cref="FeatureSummary.Ordinal"/>. Prefer it over
    /// <see cref="FeatureRef"/> when resolving, so a producer that reuses a
    /// <c>gml:id</c> across features still routes to the hit feature.
    /// </summary>
    public required int Ordinal { get; init; }

    /// <summary>
    /// The feature type code (the GML element local name, or the S-101
    /// acronym) — matches the owning processor's
    /// <see cref="FeatureInfo.FeatureType"/> / <see cref="FeatureSummary.FeatureType"/>.
    /// </summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive the hit was measured against.</summary>
    public required S100GeometryType Primitive { get; init; }

    /// <summary>
    /// <see langword="true"/> when the pick lies inside an area feature's
    /// exterior ring (and outside any interior-ring hole);
    /// <see langword="false"/> for a point or curve feature matched within the
    /// search radius.
    /// </summary>
    public required bool Inside { get; init; }

    /// <summary>
    /// Approximate distance from the pick to the feature in metres — 0 when
    /// <see cref="Inside"/>, otherwise the nearest-geometry distance that fell
    /// within the search radius.
    /// </summary>
    public required double DistanceMeters { get; init; }
}
