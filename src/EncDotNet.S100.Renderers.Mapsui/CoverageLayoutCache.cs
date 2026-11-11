using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Single-slot cache of the palette- and value-independent projection layout
/// that <see cref="MapsuiCoverageRenderer"/> computes for a grid (the per-node
/// native CRS → Web Mercator reprojection and the node→pixel mapping).
/// </summary>
/// <remarks>
/// <para>
/// The layout depends only on the grid geometry, so it can be reused across
/// renders that change only the colour palette, ECDIS display mode, coverage
/// time-step, land-area mask or opacity. The renderer itself is built afresh
/// per render (its <c>LayerName</c>, <c>LandAreas</c> and <c>Opacity</c> are
/// per-call), so the cache is held separately by whoever owns the dataset —
/// <see cref="MapsuiDatasetLayerSession"/> keeps one per dataset entry and
/// drops it with the entry (issue #855).
/// </para>
/// <para>
/// Holding one slot bounds the memory to a single grid's layout. A geometry
/// change replaces the slot. Entries are immutable and published atomically,
/// so a background render and a compose-time rebuild may share the cache.
/// </para>
/// </remarks>
internal sealed class CoverageLayoutCache
{
    private MapsuiCoverageRenderer.LayoutCacheEntry? _entry;
    private long _hits;
    private long _misses;

    /// <summary>Number of times the projection layout was reused from cache.</summary>
    public long Hits => Interlocked.Read(ref _hits);

    /// <summary>Number of times the projection layout was (re)built.</summary>
    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>
    /// Returns the cached layout when it matches the grid geometry, otherwise
    /// builds it with <paramref name="build"/> and caches the result. The match
    /// is value-based: native CRS, grid dimensions and the affine
    /// georeferencing parameters (origin and spacing).
    /// </summary>
    public MapsuiCoverageRenderer.LayoutCacheEntry GetOrBuild(
        string crs,
        int srcRows,
        int srcCols,
        GridMetadata meta,
        Func<MapsuiCoverageRenderer.LayoutCacheEntry> build)
    {
        var cached = Volatile.Read(ref _entry);
        if (cached is not null && cached.Matches(crs, srcRows, srcCols, meta))
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        var built = build();
        Volatile.Write(ref _entry, built);
        Interlocked.Increment(ref _misses);
        return built;
    }
}
