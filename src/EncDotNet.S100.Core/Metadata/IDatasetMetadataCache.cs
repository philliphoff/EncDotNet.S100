namespace EncDotNet.S100.Core.Metadata;

/// <summary>
/// A cross-session cache of <see cref="DatasetMetadata"/> keyed by a
/// dataset's source file identity, letting a host recover a dataset's
/// cheap "peek" facts (spec, extent, CRS, display-scale window, temporal
/// coverage) without re-parsing the dataset — including after a process
/// restart (issue #467 WS3).
/// </summary>
/// <remarks>
/// <para>
/// The cache is the enabler for the relaxed parse-once rule: a persisted
/// metadata entry is what makes "cheap metadata parse now, full render
/// parse later" pay off, and it is the primary enabler of loose-dataset
/// lazy registration. A hit means <em>zero parse</em> on subsequent
/// sessions.
/// </para>
/// <para>
/// Correctness rests on the key: an entry is valid only while the source
/// file's last-write time and length are unchanged. Any mismatch — or a
/// serialization-format bump, or a corrupt entry — is a miss, and a miss
/// must never break loading (the producer still runs).
/// </para>
/// </remarks>
public interface IDatasetMetadataCache
{
    /// <summary>Number of reads served from a valid cached entry.</summary>
    long Hits { get; }

    /// <summary>
    /// Number of lookups (via either <see cref="GetOrRead"/> or
    /// <see cref="TryGet"/>) not served from a valid cached entry. In
    /// <see cref="GetOrRead"/> such a miss is where the producer runs;
    /// in <see cref="TryGet"/> it simply returns <see langword="false"/>.
    /// </summary>
    long Misses { get; }

    /// <summary>
    /// Returns the cached metadata for <paramref name="sourcePath"/> when a
    /// valid entry exists (source unchanged since it was written), otherwise
    /// invokes <paramref name="producer"/>, persists its result, and returns
    /// it. A cache failure never propagates: the freshly produced value is
    /// still returned.
    /// </summary>
    /// <param name="sourcePath">
    /// Absolute path to the dataset file whose metadata is cached. Its
    /// last-write time and length form the validity key.
    /// </param>
    /// <param name="producer">
    /// Cold path invoked on a miss to parse the metadata from
    /// <paramref name="sourcePath"/>. Receives the same path.
    /// </param>
    /// <returns>The cached or freshly produced metadata.</returns>
    DatasetMetadata GetOrRead(string sourcePath, Func<string, DatasetMetadata> producer);

    /// <summary>
    /// Attempts to read a valid cached entry for <paramref name="sourcePath"/>
    /// without producing on a miss. Useful for a pure "is it already known?"
    /// probe.
    /// </summary>
    /// <param name="sourcePath">Absolute path to the dataset file.</param>
    /// <param name="metadata">The cached metadata when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> on a valid hit; otherwise <see langword="false"/>.</returns>
    bool TryGet(string sourcePath, out DatasetMetadata metadata);
}
