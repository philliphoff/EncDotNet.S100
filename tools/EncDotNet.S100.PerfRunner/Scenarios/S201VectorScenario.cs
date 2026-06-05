namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// S-201 vector pipeline: GML + XSLT path. Mirrors
/// <see cref="S124VectorScenario"/> for the AtoN authority-to-authority
/// product. Reads the first <c>*.gml</c> dataset found under
/// <c>{CorpusPath}/S201</c>.
/// </summary>
internal sealed class S201VectorScenario : IPerfScenario
{
    public string Name => "s201-vector";
    public string Description => "S-201 GML AtoN information: XSLT vector pipeline.";

    private Datasets.Pipelines.IDatasetProcessor? _processor;

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        if (_processor is null)
        {
            var gmlDir = Path.Combine(ctx.CorpusPath, "S201");
            var files = Directory.GetFiles(gmlDir, "*.gml");
            if (files.Length == 0)
                throw new InvalidOperationException($"No .gml files found in {gmlDir}");

            var factory = SharedInfrastructure.CreatePipelineFactory();
            _processor = factory.CreateProcessor(files[0]);
        }

        var result = ProcessorRenderBridge.Render(_processor);

        if (result.Layers.Count == 0)
            throw new InvalidOperationException("Expected at least one layer from S-201 render.");

        return Task.CompletedTask;
    }
}
