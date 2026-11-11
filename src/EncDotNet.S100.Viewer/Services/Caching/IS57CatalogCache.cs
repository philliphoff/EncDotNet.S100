using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Viewer.Services.Caching;

/// <summary>
/// Cross-session cache of the base-cell descriptor list read from an S-57 /
/// S-63 exchange-set catalogue (<c>CATALOG.031</c>), keyed by the catalogue
/// file's identity (issue #467 WS3 Slice 2). A hit lets a large set re-open
/// (and feed lazy registration) without re-parsing the binary ISO 8211
/// catalogue.
/// </summary>
/// <remarks>
/// The descriptors are exactly what both the eager and the lazy (deferred)
/// S-57 open paths consume: each cell's name, base-cell relative path,
/// ordered update relative paths, and geographic extent. Dataset bytes are
/// still loaded on demand from the folder source, so a cached descriptor
/// list never serves stale cell content.
/// </remarks>
internal interface IS57CatalogCache
{
    /// <summary>Total cache hits observed (diagnostics).</summary>
    long Hits { get; }

    /// <summary>Total cache misses observed (diagnostics).</summary>
    long Misses { get; }

    /// <summary>
    /// Returns the persisted descriptor list for the catalogue at
    /// <paramref name="cataloguePath"/> when a valid sidecar exists, otherwise
    /// runs <paramref name="producer"/>, persists its result (when the
    /// catalogue can be stat'd), and returns it.
    /// </summary>
    /// <param name="cataloguePath">Absolute path to the <c>CATALOG.031</c> file.</param>
    /// <param name="producer">
    /// Produces the descriptor list on a miss, given the catalogue path.
    /// </param>
    /// <returns>The cached or freshly produced descriptor list.</returns>
    IReadOnlyList<S57ExchangeSetCell> GetOrRead(
        string cataloguePath,
        Func<string, IReadOnlyList<S57ExchangeSetCell>> producer);
}
