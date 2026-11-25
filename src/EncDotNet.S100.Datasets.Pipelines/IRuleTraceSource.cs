using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A processor whose features are portrayed by Lua rules (S-100 Part 9a), which
/// can portray one of them again with a line hook and say which rule lines
/// ran: the chart reference's rule trace (symbol dictionary R12b, #1719).
/// </summary>
/// <remarks>
/// The trace runs in its own Lua state on <c>traceEngine</c>, whichever engine
/// draws the map, so it never touches the render path or its caches.
/// </remarks>
public interface IRuleTraceSource
{
    /// <summary>
    /// Portrays one feature with a line hook and returns its drawing
    /// instructions and the rule lines that ran.
    /// </summary>
    /// <remarks>
    /// The returned lines are those of the feature's own portrayal only: the
    /// lines that load a rule file (its <c>require</c>s and function
    /// definitions), which run the first time a file is needed, are left out
    /// by portraying the feature once before the traced run.
    /// </remarks>
    /// <param name="featureReference">
    /// The feature, as its drawing instructions reference it
    /// (<see cref="DrawingInstruction.FeatureReference"/>).
    /// </param>
    /// <param name="traceEngine">An engine with a line hook (LuaCSharp).</param>
    /// <param name="mariner">The mariner settings the map is drawn with.</param>
    /// <param name="cancellationToken">Cancels the trace before or between its steps.</param>
    /// <returns>The trace, or <see langword="null"/> when the processor can't portray the feature with Lua rules.</returns>
    Task<LuaFeatureTrace?> TraceFeatureAsync(
        string featureReference, ILuaEngine traceEngine, MarinerSettings mariner, CancellationToken cancellationToken = default);
}

/// <summary>Shared by the processors that implement <see cref="IRuleTraceSource"/>.</summary>
internal static class RuleTrace
{
    /// <summary>
    /// Opens a trace session, portrays the feature twice and returns the
    /// second trace, which has no rule file's load-time lines in it.
    /// </summary>
    /// <param name="open">Opens the trace session.</param>
    /// <param name="featureReference">The feature.</param>
    /// <param name="cancellationToken">Cancels between steps.</param>
    public static async Task<LuaFeatureTrace?> TraceAsync(
        Func<CancellationToken, Task<LuaTraceSession>> open, string featureReference, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureReference);
        using var session = await open(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // Warm: the first portrayal loads the rule files the feature needs.
        session.Trace([featureReference]);
        cancellationToken.ThrowIfCancellationRequested();
        return session.Trace([featureReference]) is [var trace] ? trace : null;
    }
}
