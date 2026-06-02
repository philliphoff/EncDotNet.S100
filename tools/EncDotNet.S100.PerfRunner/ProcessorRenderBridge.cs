using System.Reflection;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Bridges the dataset-processor render entry point across the base /
/// candidate library boundary that the performance gate spans.
/// </summary>
/// <remarks>
/// <para>
/// The perf gate (<c>.github/workflows/perf.yml</c>) deliberately overlays
/// <em>this</em> (head) perf harness onto the <em>base</em> SHA's library
/// source so the base runner is "this PR's runner code linked against the
/// base SHA's library code". A render call compiled here therefore has to
/// bind on both library surfaces:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Base: <c>DatasetResult Render(RenderContext?)</c> (synchronous).
///   </description></item>
///   <item><description>
///     Candidate: <c>Task&lt;DatasetResult&gt; RenderAsync(RenderContext?,
///     CancellationToken)</c> (the async entry point this PR introduces).
///   </description></item>
/// </list>
/// <para>
/// Reflection keeps the scenario call sites source-compatible with either
/// shape without re-introducing a synchronous render API on the production
/// <see cref="IDatasetProcessor"/> interface — the version-bridging concern
/// belongs in the harness the gate overlays, not in the library under test.
/// The method handle is resolved once into a strongly typed delegate so the
/// measured render region pays only an ordinary delegate invocation (no
/// per-iteration reflection cost), keeping the base/candidate comparison
/// meaningful.
/// </para>
/// </remarks>
internal static class ProcessorRenderBridge
{
    private static readonly Func<IDatasetProcessor, RenderContext?, CancellationToken, DatasetResult> s_render = Create();

    /// <summary>
    /// Renders the dataset synchronously, dispatching to whichever render
    /// entry point the linked library exposes.
    /// </summary>
    public static DatasetResult Render(
        IDatasetProcessor processor,
        RenderContext? context = null,
        CancellationToken cancellationToken = default)
        => s_render(processor, context, cancellationToken);

    private static Func<IDatasetProcessor, RenderContext?, CancellationToken, DatasetResult> Create()
    {
        var type = typeof(IDatasetProcessor);

        var asyncMethod = type.GetMethod(
            "RenderAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(RenderContext), typeof(CancellationToken)],
            modifiers: null);

        if (asyncMethod is not null && asyncMethod.ReturnType == typeof(Task<DatasetResult>))
        {
            var invoke = asyncMethod.CreateDelegate<
                Func<IDatasetProcessor, RenderContext?, CancellationToken, Task<DatasetResult>>>();

            return (processor, context, cancellationToken) =>
                invoke(processor, context, cancellationToken).GetAwaiter().GetResult();
        }

        var syncMethod = type.GetMethod(
            "Render",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(RenderContext)],
            modifiers: null);

        if (syncMethod is not null && syncMethod.ReturnType == typeof(DatasetResult))
        {
            var invoke = syncMethod.CreateDelegate<
                Func<IDatasetProcessor, RenderContext?, DatasetResult>>();

            return (processor, context, _) => invoke(processor, context);
        }

        throw new InvalidOperationException(
            "IDatasetProcessor exposes neither RenderAsync(RenderContext?, CancellationToken) " +
            "nor Render(RenderContext?); the perf harness cannot bind a render entry point.");
    }
}
