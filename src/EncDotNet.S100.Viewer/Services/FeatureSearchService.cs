using System;
using System.Collections.Generic;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IFeatureSearchService"/> implementation. Builds
/// a snapshot of <c>(processor, FeatureSummary)</c> pairs lazily from
/// <see cref="IDatasetLoaderService.Processors"/> /
/// <see cref="IDatasetLoaderService.EntryLayers"/>, and rebuilds it
/// whenever the loader signals a dataset load or unload.
/// </summary>
/// <remarks>
/// v1 indexes feature ID, feature type code, FC-resolved type name,
/// dataset file name, and product spec. Decoded attribute values are
/// out of scope; if real workloads need them, the snapshot can be
/// extended without changing the public contract.
/// </remarks>
internal sealed class FeatureSearchService : IFeatureSearchService
{
    private readonly IDatasetLoaderService _loader;
    private List<FeatureSearchHit>? _snapshot;
    private int _snapshotProcessorHash;

    public FeatureSearchService(IDatasetLoaderService loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
    }

    public (IReadOnlyList<FeatureSearchHit> Hits, int TotalMatched) Search(string? query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
            return (Array.Empty<FeatureSearchHit>(), 0);

        EnsureSnapshot();
        var snapshot = _snapshot!;
        var trimmed = query.Trim();

        var hits = new List<FeatureSearchHit>(Math.Min(limit, 64));
        var total = 0;

        foreach (var hit in snapshot)
        {
            if (!Matches(hit, trimmed))
                continue;

            total++;
            if (hits.Count < limit)
                hits.Add(hit);
        }

        return (hits, total);
    }

    private static bool Matches(FeatureSearchHit hit, string query)
    {
        return Contains(hit.FeatureRef, query)
            || Contains(hit.FeatureType, query)
            || Contains(hit.FeatureTypeName, query)
            || Contains(hit.DatasetFileName, query)
            || Contains(hit.ProductSpec, query);
    }

    private static bool Contains(string? haystack, string needle)
        => haystack is { } h && h.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private void EnsureSnapshot()
    {
        // Cheap signature: count + each processor's identity hash. The
        // loader doesn't expose an unload event, so we re-check on each
        // search; index-build is in-memory and inexpensive.
        var hash = ComputeProcessorHash();
        if (_snapshot is not null && _snapshotProcessorHash == hash)
            return;

        var snapshot = new List<FeatureSearchHit>();
        foreach (var (entry, processor) in _loader.Processors)
        {
            foreach (var summary in processor.EnumerateFeatures())
            {
                snapshot.Add(new FeatureSearchHit
                {
                    Processor = processor,
                    DatasetFileName = entry.DisplayName,
                    ProductSpec = processor.ProductSpec,
                    FeatureRef = summary.FeatureRef,
                    Ordinal = summary.Ordinal,
                    FeatureType = summary.FeatureType,
                    FeatureTypeName = summary.FeatureTypeName,
                });
            }
        }
        _snapshot = snapshot;
        _snapshotProcessorHash = hash;
    }

    private int ComputeProcessorHash()
    {
        var hash = new HashCode();
        hash.Add(_loader.Processors.Count);
        foreach (var (entry, processor) in _loader.Processors)
        {
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(entry));
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(processor));
        }
        return hash.ToHashCode();
    }

    /// <summary>
    /// Test-only seam to force a rebuild on the next call.
    /// </summary>
    internal void Invalidate()
    {
        _snapshot = null;
        _snapshotProcessorHash = 0;
    }
}
