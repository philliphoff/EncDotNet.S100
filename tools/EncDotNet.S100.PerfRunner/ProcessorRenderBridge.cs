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
    private static readonly Func<IDatasetProcessor, RenderContext?, CancellationToken, DatasetResult> RenderDelegate = Create();

    /// <summary>
    /// Renders the dataset synchronously, dispatching to whichever render
    /// entry point the linked library exposes.
    /// </summary>
    public static DatasetResult Render(
        IDatasetProcessor processor,
        RenderContext? context = null,
        CancellationToken cancellationToken = default)
        => RenderDelegate(processor, context, cancellationToken);

    private static Func<IDatasetProcessor, RenderContext?, CancellationToken, DatasetResult> Create()
    {
        // Candidate shape (issue #189 PR2): RenderAsync moved off
        // IDatasetProcessor onto MapsuiDatasetRenderer in the Mapsui package.
        var rendererPath = TryCreateMapsuiRendererInvoker();
        if (rendererPath is not null)
        {
            return rendererPath;
        }

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
            "IDatasetProcessor exposes neither a MapsuiDatasetRenderer render entry, " +
            "RenderAsync(RenderContext?, CancellationToken), nor Render(RenderContext?); " +
            "the perf harness cannot bind a render entry point.");
    }

    private static Func<IDatasetProcessor, RenderContext?, CancellationToken, DatasetResult>? TryCreateMapsuiRendererInvoker()
    {
        Type? rendererType;
        try
        {
            var mapsuiAssembly = Assembly.Load(new AssemblyName("EncDotNet.S100.Renderers.Mapsui"));
            rendererType = mapsuiAssembly.GetType(
                "EncDotNet.S100.Renderers.Mapsui.MapsuiDatasetRenderer",
                throwOnError: false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException)
        {
            return null;
        }

        if (rendererType is null)
        {
            return null;
        }

        var renderMethod = rendererType.GetMethod(
            "RenderAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(IDatasetProcessor), typeof(RenderContext), typeof(CancellationToken)],
            modifiers: null);
        if (renderMethod is null || renderMethod.ReturnType != typeof(Task<DatasetResult>))
        {
            return null;
        }

        var ctor = rendererType.GetConstructors().FirstOrDefault(c =>
        {
            var parameters = c.GetParameters();
            return parameters.Length >= 1
                && parameters[0].ParameterType.IsInstanceOfType(SharedInfrastructure.CrsFactory);
        });
        if (ctor is null)
        {
            return null;
        }

        var ctorParameters = ctor.GetParameters();
        var ctorArgs = new object?[ctorParameters.Length];
        ctorArgs[0] = SharedInfrastructure.CrsFactory;
        for (var i = 1; i < ctorParameters.Length; i++)
        {
            ctorArgs[i] = ctorParameters[i].HasDefaultValue ? ctorParameters[i].DefaultValue : null;
        }

        var renderer = ctor.Invoke(ctorArgs);

        return (processor, context, cancellationToken) =>
        {
            var task = (Task<DatasetResult>)renderMethod.Invoke(
                renderer,
                [processor, context, cancellationToken])!;
            return task.GetAwaiter().GetResult();
        };
    }
}
