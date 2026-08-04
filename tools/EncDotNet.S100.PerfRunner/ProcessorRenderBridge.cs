using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Renders a dataset through the Mapsui renderer for performance scenarios.
/// </summary>
/// <remarks>
/// The perf gate (<c>.github/workflows/perf.yml</c>) deliberately overlays
/// <em>this</em> (head) perf harness onto the <em>base</em> SHA's library
/// source. Keeping the render result local-variable type inferred lets the
/// same source compile before and after the Mapsui result ownership rename.
/// </remarks>
internal static class ProcessorRenderBridge
{
    private static readonly MapsuiDatasetRenderer Renderer = new(SharedInfrastructure.CrsFactory);

    /// <summary>
    /// Renders the dataset synchronously and returns the produced layer count.
    /// </summary>
    public static int RenderLayerCount(
        IDatasetProcessor processor,
        RenderContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var result = Renderer.RenderAsync(processor, context, cancellationToken)
            .GetAwaiter()
            .GetResult();
        return result.Layers.Count;
    }
}
