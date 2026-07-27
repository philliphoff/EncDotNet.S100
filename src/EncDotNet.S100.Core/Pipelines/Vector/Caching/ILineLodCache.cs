namespace EncDotNet.S100.Pipelines.Vector.Caching;

/// <summary>
/// Caches the precomputed <see cref="LineLodPyramid"/> for one line feature
/// so that <em>cold</em> first-paint work in the renderer's per-frame
/// simplification path (see <c>CachedVectorStyleRenderer</c> in
/// <c>EncDotNet.S100.Renderers.Mapsui</c>) can be skipped: instead of running
/// a Douglas-Peucker pass on every cache-miss rebuild, the renderer looks up
/// the coarsest pyramid level whose tolerance is still sub-pixel at the
/// current viewport and consumes it directly.
/// </summary>
/// <remarks>
/// <para>
/// This cache is deliberately independent from <see cref="IPortrayalInstructionCache"/>.
/// The portrayal-instruction cache serves the <em>single, full-resolution</em>
/// post-Lua drawing-instruction list; multiplying its entries by LOD-band
/// count would couple two unrelated concerns (portrayal correctness and
/// geometric generalisation). The pyramid is read <em>downstream</em> of
/// portrayal, when a renderer is about to build an <c>SKPath</c> from a
/// feature's geometry.
/// </para>
/// <para>
/// Callers key entries by an opaque string that must fully identify the
/// simplification inputs: the source dataset's content hash and the feature
/// reference. If the input geometry, feature identity, or simplification
/// version can change, they must be folded into the key so a stale entry
/// cannot be served. The disk implementation validates a format-version
/// header on read to invalidate entries from previous binary layouts.
/// </para>
/// <para>
/// The cache is invisible to correctness — a miss simply falls back to
/// today's inline simplification path — so any failure to read or write is
/// swallowed (no render is ever broken by a cache-layer error).
/// </para>
/// </remarks>
public interface ILineLodCache
{
    /// <summary>
    /// Returns the pyramid for <paramref name="key"/>, invoking
    /// <paramref name="factory"/> to compute and store it on a miss.
    /// </summary>
    /// <param name="key">
    /// Opaque key that fully identifies the simplification inputs (dataset
    /// content hash + feature reference + any tunables). Same-key hits must
    /// always match a fresh factory invocation.
    /// </param>
    /// <param name="factory">
    /// Runs the pyramid build on a miss. Only invoked when the cache does
    /// not already hold an entry for <paramref name="key"/>.
    /// </param>
    /// <returns>
    /// The pyramid for <paramref name="key"/>. On a disk-cache hit the
    /// returned instance is freshly deserialised but value-equivalent to the
    /// factory's output.
    /// </returns>
    LineLodPyramid GetOrCompute(string key, Func<LineLodPyramid> factory);

    /// <summary>
    /// Number of <see cref="GetOrCompute"/> calls served from the cache
    /// (the factory was skipped). Exposed for diagnostics and tests.
    /// </summary>
    long Hits { get; }

    /// <summary>
    /// Number of <see cref="GetOrCompute"/> calls that ran the factory
    /// (cache miss). Exposed for diagnostics and tests.
    /// </summary>
    long Misses { get; }
}
