namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Executes the S-100 Part 9A Lua portrayal stage for a bound dataset and
/// returns typed drawing instructions. Implementations are product-specific
/// (e.g. <c>S101LuaRuleExecutor</c>): they encapsulate the host-API setup,
/// module loading, <c>PortrayalMain()</c> invocation, emit-string parsing,
/// and any product-specific post-processing.
/// </summary>
/// <remarks>
/// The executor is the unified pipeline's hook into Part 9A: <see cref="VectorPipeline"/>
/// invokes it once per render and concatenates its results with the XSLT-stage
/// output before viewing-group filtering and priority sorting. Concrete executors
/// typically bind their dataset at construction time, so callers configure them
/// per render rather than per pipeline.
/// </remarks>
public interface ILuaRuleExecutor
{
    /// <summary>
    /// Runs the Lua portrayal stage and returns typed drawing instructions
    /// ready for the renderer.
    /// </summary>
    /// <param name="mariner">Mariner-configurable display preferences (S-100 Part 9 §4.2).</param>
    /// <param name="cancellationToken">
    /// Signals that the render has been cancelled. The Lua interpreter step
    /// itself is not interruptible mid-script, so implementations honour the
    /// token at coarse boundaries (e.g. before invoking <c>PortrayalMain()</c>
    /// and while parsing emit strings); the method remains synchronous.
    /// </param>
    IReadOnlyList<DrawingInstruction> Execute(MarinerSettings mariner, CancellationToken cancellationToken = default);
}
