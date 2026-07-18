using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;

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
/// Only products with a cheap path-based metadata reader are supported;
/// the current caller (loose-cell folder framing) sees S-101 / S-57 ENC
/// cells, and S-101 is wired here. An unrecognised or unsupported product,
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
        _ => null,
    };
}
