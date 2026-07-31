using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Caches the result of the S-101 pattern-fill priority clip
/// (<see cref="MapsuiDisplayListRenderer"/>'s
/// <see cref="EncDotNet.S100.Rendering.Scene.PatternPriorityClipper"/>) so that re-renders which do not change the
/// clip inputs — most importantly Day/Dusk/Night palette switches — skip the
/// expensive NetTopologySuite overlay work.
/// </summary>
/// <remarks>
/// <para>
/// The clip subtracts higher-priority pattern areas and opaque land out of
/// each lower-priority semi-transparent raster pattern tile. On the densest
/// real S-101 cells (a ~64,000-vertex M_QUAL coverage polygon) this costs on
/// the order of seconds, dominated by a single <c>Buffer(0)</c> validity
/// repair, and every in-domain speed lever has been shown not to help — so the
/// per-build cost is treated as irreducible.
/// </para>
/// <para>
/// Crucially the clip runs once per <em>layer build</em>
/// (<see cref="MapsuiDisplayListRenderer.Render"/>), not per frame; pan/zoom
/// reuses the baked Mapsui features. <c>Render</c> re-fires on dataset load,
/// palette switch (the dominant trigger), and ECDIS display-setting changes.
/// Because the clipped boundary geometry is fully determined by the mariner +
/// ECDIS display state plus the fixed dataset geometry — and is independent of
/// the palette, which only recolours the pattern tiles applied <em>after</em>
/// clipping — keying this cache on the portrayal cache key lets a palette
/// switch reuse the previously computed geometry verbatim.
/// </para>
/// <para>
/// Both render arms consult this cache. The Mapsui feature ("A") arm wraps its
/// own clip directly; the default TiledScene ("B") arm — which clips inside the
/// shared <see cref="EncDotNet.S100.Rendering.Scene.VectorSceneBuilder"/> when
/// lowering the <see cref="EncDotNet.S100.Rendering.Scene.VectorScene"/> IR —
/// reaches it through a <see cref="EncDotNet.S100.Rendering.Scene.PatternClipMemoizer"/>
/// adapter (<see cref="MapsuiDisplayListRenderer"/>). Because both arms produce
/// identical clip topology and share this key, the expensive overlay is computed
/// at most once per build regardless of which arm is active.
/// </para>
/// <para>
/// The abstraction is intentionally minimal so that the single-slot in-memory
/// implementation (<see cref="InMemoryPatternClipCache"/>) and the disk-backed
/// (WKB sidecar) implementation (<see cref="DiskPatternClipCache"/>) — which
/// also eliminates the cold first-load cost of previously-seen cells — both fit
/// behind the same contract.
/// </para>
/// </remarks>
public interface IPatternClipCache
{
    /// <summary>
    /// Returns the cached clipped pattern geometry for <paramref name="key"/>,
    /// invoking <paramref name="factory"/> to compute and store it on a miss.
    /// </summary>
    /// <param name="key">
    /// An opaque key that fully identifies the clip inputs (for S-101 this is
    /// the mariner + ECDIS display-state portrayal cache key). The same key
    /// must always map to the same clip result.
    /// </param>
    /// <param name="factory">
    /// Computes the clipped pattern geometry when the key is not cached. Only
    /// invoked on a miss.
    /// </param>
    /// <returns>
    /// The clipped pattern entries, each carrying its palette-independent
    /// pattern reference, drawing priority, and clipped geometry. On a hit the
    /// exact cached instances are returned.
    /// </returns>
    IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)> GetOrCompute(
        string key,
        Func<IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)>> factory);

    /// <summary>
    /// Number of <see cref="GetOrCompute"/> calls served from the cache (the
    /// expensive clip overlay was skipped). Exposed for diagnostics and tests.
    /// </summary>
    long Hits { get; }

    /// <summary>
    /// Number of <see cref="GetOrCompute"/> calls that ran the factory (cache
    /// miss). Exposed for diagnostics and tests.
    /// </summary>
    long Misses { get; }
}
