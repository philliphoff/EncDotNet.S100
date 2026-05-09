namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// Warm S-101 portrayal: pipeline-only cost (no rendering) after warmup.
/// </summary>
internal sealed class S101PortrayWarmScenario : IPerfScenario
{
    public string Name => "s101-portray-warm";
    public string Description => "S-101 warm portrayal pipeline (no render), iterated after warmup.";

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

        _processor.Render();
        return Task.CompletedTask;
    }
}
