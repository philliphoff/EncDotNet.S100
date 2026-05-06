using System.Collections.Generic;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Provides a flat, in-memory index over every feature exposed by the
/// currently loaded datasets so the search activity panel can offer
/// substring lookup across feature ID, feature type and dataset name
/// without re-walking each processor on every keystroke.
/// </summary>
internal interface IFeatureSearchService
{
    /// <summary>
    /// Searches the snapshot for the supplied query, returning at most
    /// <paramref name="limit"/> hits in match order. An empty / null
    /// query returns an empty list. Match is case-insensitive substring
    /// against the feature ID, the on-the-wire feature type, the
    /// FC-resolved feature type name (when present), the dataset file
    /// name, and the product spec code.
    /// </summary>
    /// <returns>
    /// A tuple of (hits, totalMatched). When <c>totalMatched &gt; hits.Count</c>
    /// the caller can render a "showing N of M" footer.
    /// </returns>
    (IReadOnlyList<FeatureSearchHit> Hits, int TotalMatched) Search(string? query, int limit);
}

/// <summary>
/// One result from <see cref="IFeatureSearchService.Search"/>. Carries
/// the originating processor so callers can resolve full
/// <see cref="FeatureInfo"/> via
/// <see cref="IPickService.OpenFeature"/>.
/// </summary>
internal sealed class FeatureSearchHit
{
    public required IDatasetProcessor Processor { get; init; }

    public required string DatasetFileName { get; init; }

    public required string ProductSpec { get; init; }

    public required string FeatureRef { get; init; }

    /// <summary>
    /// The feature's enumeration position within its processor — passed
    /// to <see cref="IPickService.OpenFeatureAt"/> so duplicate
    /// <c>gml:id</c>s (a real producer bug) still route to the correct
    /// feature.
    /// </summary>
    public int Ordinal { get; init; }

    public required string FeatureType { get; init; }

    public string? FeatureTypeName { get; init; }
}
