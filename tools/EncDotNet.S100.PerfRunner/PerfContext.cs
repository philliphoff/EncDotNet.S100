namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Context passed to every <see cref="IPerfScenario"/> iteration.
/// </summary>
public sealed class PerfContext
{
    /// <summary>Root path to the test corpus (default: <c>tests/datasets/</c>).</summary>
    public required string CorpusPath { get; init; }

    /// <summary>Whether this iteration is a warmup (metrics may be discarded).</summary>
    public bool IsWarmup { get; init; }

    /// <summary>Zero-based iteration index within the current measured batch.</summary>
    public int Iteration { get; init; }

    /// <summary>
    /// One-based round number, used by interleaved baseline/candidate
    /// orchestration to tag samples so that downstream gating can group
    /// per-round when needed. Defaults to <c>1</c> for non-interleaved
    /// runs.
    /// </summary>
    public int Round { get; init; } = 1;
}
