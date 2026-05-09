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

    /// <summary>Zero-based iteration index.</summary>
    public int Iteration { get; init; }
}
