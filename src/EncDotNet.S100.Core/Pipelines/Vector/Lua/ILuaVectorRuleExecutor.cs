namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// Diagnostic extension of <see cref="IVectorRuleExecutor"/> for Lua-based
/// portrayal engines (S-100 Part 9A). Exposes the raw pre-parse emit stream
/// (<see cref="EmittedInstruction"/>) in addition to the typed
/// <see cref="IVectorRuleExecutor.ExecuteAsync"/> output.
/// </summary>
/// <remarks>
/// The raw surface is intentionally kept off the shared
/// <see cref="IVectorRuleExecutor"/> interface because the emit-string concept
/// is Lua-specific and has no analogue for the XSLT engine. It is consumed by
/// tooling (<c>tools/TestS101Lua</c>) and tests that inspect the emit stream.
/// </remarks>
public interface ILuaVectorRuleExecutor : IVectorRuleExecutor
{
    /// <summary>
    /// Runs the Lua portrayal pass and returns the raw emitted
    /// drawing-instruction strings keyed by feature reference, without parsing
    /// or post-processing.
    /// </summary>
    /// <param name="mariner">Mariner-configurable display preferences (S-100 Part 9 §4.2).</param>
    /// <param name="cancellationToken">Honoured at coarse boundaries before Lua invocation.</param>
    Task<IReadOnlyList<EmittedInstruction>> ExecuteRawAsync(
        MarinerSettings mariner, CancellationToken cancellationToken = default);
}
