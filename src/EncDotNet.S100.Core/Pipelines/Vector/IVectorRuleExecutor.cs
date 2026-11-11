namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Executes one portrayal-rule engine for a bound dataset and returns typed
/// drawing instructions. This is the unified pipeline's pluggable rule stage:
/// <see cref="VectorPipeline"/> invokes each registered executor once per
/// render and concatenates the results before viewing-group filtering and
/// priority sorting.
/// </summary>
/// <remarks>
/// Implementations are engine-specific siblings — the Lua engine
/// (S-100 Part 9A) and the XSLT engine (S-100 Part 9 §9.4) each implement
/// this interface. Concrete executors typically bind their dataset and
/// portrayal catalogue at construction time, so callers configure them per
/// render rather than per pipeline.
/// </remarks>
public interface IVectorRuleExecutor
{
    /// <summary>
    /// Runs the rule stage and returns typed drawing instructions ready for
    /// the renderer.
    /// </summary>
    /// <param name="mariner">Mariner-configurable display preferences (S-100 Part 9 §4.2).</param>
    /// <param name="cancellationToken">
    /// Signals that the render has been cancelled. Script interpreters are
    /// generally not interruptible mid-evaluation, so implementations honour
    /// the token at coarse boundaries; the asynchronous shape exists to allow
    /// implementations to await catalogue asset loads (compiled XSLT, Lua
    /// source) before driving the synchronous engine.
    /// </param>
    Task<IReadOnlyList<DrawingInstruction>> ExecuteAsync(MarinerSettings mariner, CancellationToken cancellationToken = default);
}
