namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// Warm S-101 render: pipeline + Mapsui display-list render to
/// in-memory layer (no UI thread). Captures render-stage cost.
/// </summary>
internal sealed class S101RenderWarmScenario : IPerfScenario
{
    public string Name => "s101-render-warm";
    public string Description => "S-101 warm pipeline + Mapsui display-list render (headless).";

    private Datasets.Pipelines.IDatasetProcessor? _processor;

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        if (_processor is null)
        {
            var datasetDir = Path.Combine(ctx.CorpusPath, "S101", "S-101", "DATASET_FILES");
            var files = Directory.GetFiles(datasetDir, "*.000");
            if (files.Length == 0)
                throw new InvalidOperationException($"No .000 files found in {datasetDir}");

            var factory = SharedInfrastructure.CreatePipelineFactory();
            _processor = factory.CreateProcessor(files[0]);
        }

        // Render() invokes the full pipeline including the Mapsui
        // display-list renderer — the returned layers are in-memory
        // Mapsui MemoryLayers, no UI thread required.
        var result = ProcessorRenderBridge.Render(_processor);

        // Touch the layer list to prevent dead-code elimination.
        if (result.Layers.Count == 0)
            throw new InvalidOperationException("Expected at least one layer from S-101 render.");

        return Task.CompletedTask;
    }
}
