using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// <see cref="IDatasetMetadataReader"/> that dispatches a dataset path to
/// the matching product's cheap <c>ReadMetadata</c> producer and memoizes
/// the result in a cross-session <see cref="IDatasetMetadataCache"/> so a
/// previously-probed dataset costs zero parse on a later session
/// (issue #467 WS3).
/// </summary>
/// <remarks>
/// <para>
/// The product is identified by the same content sniff the loader uses
/// (<see cref="DatasetPipelineFactory.DetectProductSpec"/>), keeping the
/// probe and the eventual full load in agreement. The cache
/// (<see cref="IDatasetMetadataCache.GetOrRead"/>) skips the producer on a
/// hit, so a repeat read pays only the cheap spec sniff, never the parse.
/// </para>
/// <para>
/// Only products with a cheap path-based metadata reader are supported:
/// the S-101 / S-57 ENC cells that loose-cell folder framing sees, and the
/// GML products whose bounds the Library probes for loose datasets (S-122,
/// S-124, S-125, S-127, S-129, S-201, S-411, S-421; #809). An unrecognised or unsupported product,
/// or any parse failure, yields <see langword="null"/> and is not cached
/// (there is no negative caching), so a transient error simply retries next
/// time and never blocks loading.
/// </para>
/// </remarks>
internal sealed class CachingDatasetMetadataReader : IDatasetMetadataReader
{
    private readonly IDatasetMetadataCache _cache;

    /// <summary>
    /// Creates a reader backed by <paramref name="cache"/>.
    /// </summary>
    /// <param name="cache">The cross-session metadata cache to memoize into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="cache"/> is null.</exception>
    public CachingDatasetMetadataReader(IDatasetMetadataCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    /// <inheritdoc />
    public DatasetMetadata? TryRead(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var producer = ResolveProducer(DatasetPipelineFactory.DetectProductSpec(path));
        if (producer is null)
            return null;

        try
        {
            return _cache.GetOrRead(path, producer);
        }
        catch
        {
            // A parse failure must never break the caller's flow; fall back to
            // "not cheaply available" and let it do a full load if it wants.
            return null;
        }
    }

    /// <summary>
    /// Maps a detected product-specification name to its cheap path-based
    /// metadata producer, or <see langword="null"/> when the product has no
    /// such producer wired here.
    /// </summary>
    private static Func<string, DatasetMetadata>? ResolveProducer(string? spec) => spec switch
    {
        "S-101" => S101Dataset.ReadMetadata,
        "S-57" => S57Dataset.ReadMetadata,
        // Loose GML datasets, e.g. S-124 warnings and S-122 areas downloaded
        // from SECOM, get their bounds from their feature geometry (#809).
        "S-122" => Datasets.S122.S122Dataset.ReadMetadata,
        "S-124" => Datasets.S124.S124Dataset.ReadMetadata,
        "S-125" => Datasets.S125.S125Dataset.ReadMetadata,
        "S-127" => Datasets.S127.S127Dataset.ReadMetadata,
        "S-129" => Datasets.S129.S129Dataset.ReadMetadata,
        "S-201" => Datasets.S201.S201Dataset.ReadMetadata,
        "S-411" => Datasets.S411.S411Dataset.ReadMetadata,
        "S-421" => Datasets.S421.S421Dataset.ReadMetadata,
        _ => null,
    };
}
