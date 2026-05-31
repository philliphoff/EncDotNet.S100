namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// S-124 vector pipeline: XSLT-only path (no Lua). The simplest
/// vector product, good for isolating XSLT transform overhead.
/// </summary>
internal sealed class S124VectorScenario : IPerfScenario
{
    public string Name => "s124-vector";
    public string Description => "S-124 GML navigational warnings: XSLT vector pipeline.";

    private Datasets.Pipelines.IDatasetProcessor? _processor;

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        if (_processor is null)
        {
            var gmlDir = Path.Combine(ctx.CorpusPath, "S124");
            var files = Directory.GetFiles(gmlDir, "*.gml");
            if (files.Length == 0)
                throw new InvalidOperationException($"No .gml files found in {gmlDir}");

            var factory = SharedInfrastructure.CreatePipelineFactory();
            _processor = factory.CreateProcessor(files[0]);
        }

        var result = ProcessorRenderBridge.Render(_processor);

        if (result.Layers.Count == 0)
            throw new InvalidOperationException("Expected at least one layer from S-124 render.");

        return Task.CompletedTask;
    }
}
