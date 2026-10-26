using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Datasets.Pipelines;

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
/// (<see cref="DatasetPipelineFactory.DetectProductSpec(string)"/>), keeping the
/// probe and the eventual full load in agreement, and its producer is the
/// registration's <see cref="ProductRegistration.ReadMetadata"/> trait. The
/// cache (<see cref="IDatasetMetadataCache.GetOrRead"/>) skips the producer on
/// a hit, so a repeat read pays only the cheap spec sniff, never the parse.
/// </para>
/// <para>
/// Only products whose registration supplies a cheap path-based metadata reader
/// are supported: among the built-in ones, the S-101 / S-57 ENC cells that
/// loose-cell folder framing sees, the GML products whose bounds the Library
/// probes for loose datasets (S-122, S-124, S-125, S-127, S-129, S-201, S-411,
/// S-421; #809), and the HDF5 coverages (S-102, S-104, S-111; #1037). An
/// unrecognised product, one without a reader, or any parse failure, yields
/// <see langword="null"/> and is not cached (there is no negative caching), so
/// a transient error simply retries next time and never blocks loading.
/// </para>
/// </remarks>
internal sealed class CachingDatasetMetadataReader : IDatasetMetadataReader
{
    private readonly IDatasetMetadataCache _cache;
    private readonly ProductRegistry _registry;

    /// <summary>
    /// Creates a reader backed by <paramref name="cache"/>.
    /// </summary>
    /// <param name="cache">The cross-session metadata cache to memoize into.</param>
    /// <param name="registry">
    /// The products to detect and read; <see langword="null"/> for
    /// <see cref="ProductRegistry.Default"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="cache"/> is null.</exception>
    public CachingDatasetMetadataReader(IDatasetMetadataCache cache, ProductRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        _registry = registry ?? ProductRegistry.Default;
    }

    /// <inheritdoc />
    public DatasetMetadata? TryRead(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (DatasetPipelineFactory.DetectProductSpec(path, _registry) is not { } spec
            || !_registry.TryResolve(spec, out var registration)
            || registration.ReadMetadata is not { } producer)
        {
            return null;
        }

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
}
