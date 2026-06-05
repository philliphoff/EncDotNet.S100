namespace EncDotNet.S100.Pipelines.Vector.Caching;

/// <summary>
/// Caches the post-pipeline S-100 Part 9 drawing-instruction list produced for
/// a dataset so that re-opening (or reloading) a previously-portrayed dataset
/// can skip the expensive portrayal run. For S-101 the portrayal run is the
/// MoonSharp execution of the Part 9A Lua rules, which dominates cold-load wall
/// time (~1 s per cell); the rest of the load — ISO 8211 parse, layer build,
/// paint — is comparatively cheap.
/// </summary>
/// <remarks>
/// <para>
/// The cached artifact is the fully-prepared
/// <see cref="DrawingInstruction"/> list as it leaves
/// <c>PortrayalPipeline.ProcessAsync</c> — i.e. <em>after</em> parsing, anchor
/// resolution, transforms, viewing-group / display-plane filtering, and the
/// S-100 Part 9 priority sort. Caching at this boundary (rather than the raw
/// Lua-emitted instruction strings) means a hit reproduces the exact list a
/// fresh run would, without re-implementing or bypassing any post-Lua stage.
/// Everything the S-101 processor does after this point (the area / non-area
/// split, the pattern-fill clip, the out-of-scale-band cap) runs downstream of
/// the cached list and is therefore unaffected.
/// </para>
/// <para>
/// Correctness depends entirely on the <c>key</c> the caller supplies: it must
/// fully identify every input that can change the emitted list — the dataset
/// content, the feature- and portrayal-catalogue content (including any runtime
/// overrides), the portrayal engine / rule version, and the mariner + ECDIS
/// display state. The same key must always map to the same list. See
/// <c>S101DatasetProcessor</c>'s portrayal-content scope for the concrete key
/// construction.
/// </para>
/// <para>
/// The abstraction mirrors <c>IPatternClipCache</c> so an in-memory
/// implementation (<see cref="InMemoryPortrayalInstructionCache"/>, for tests
/// and single-session reuse) and a disk-backed implementation
/// (<see cref="DiskPortrayalInstructionCache"/>, which also survives process
/// restart) fit behind one contract.
/// </para>
/// </remarks>
public interface IPortrayalInstructionCache
{
    /// <summary>
    /// Returns the cached drawing-instruction list for <paramref name="key"/>,
    /// invoking <paramref name="factory"/> to compute and store it on a miss.
    /// </summary>
    /// <param name="key">
    /// An opaque key that fully identifies the portrayal inputs (see the type
    /// remarks). The same key must always map to the same instruction list.
    /// </param>
    /// <param name="factory">
    /// Runs the portrayal pipeline to produce the instruction list when the key
    /// is not cached. Only invoked on a miss.
    /// </param>
    /// <returns>
    /// The drawing-instruction list, in the same order as produced by the
    /// pipeline (the order is significant: it is the renderer's final priority
    /// tie-breaker). On a disk-cache hit the returned instances are freshly
    /// deserialized but value-equivalent to the originals.
    /// </returns>
    IReadOnlyList<DrawingInstruction> GetOrCompute(
        string key,
        Func<IReadOnlyList<DrawingInstruction>> factory);

    /// <summary>
    /// Number of <see cref="GetOrCompute"/> calls served from the cache (the
    /// portrayal run was skipped). Exposed for diagnostics and tests.
    /// </summary>
    long Hits { get; }

    /// <summary>
    /// Number of <see cref="GetOrCompute"/> calls that ran the factory (cache
    /// miss). Exposed for diagnostics and tests.
    /// </summary>
    long Misses { get; }
}
