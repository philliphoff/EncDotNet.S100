namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// Burst-render variant of <see cref="S101RenderWarmScenario"/> introduced
/// for issue #488: opens an S-101 dataset once and invokes
/// <see cref="ProcessorRenderBridge.Render"/> <see cref="RepeatCount"/>
/// times per iteration on a cached processor. Isolates steady-state
/// projection cost from open + first-frame cost, so a reproject-once
/// cache (PR #2) shows up as (1 miss + N-1 hits) / N per iteration
/// instead of hiding behind per-iteration bookkeeping overhead.
/// </summary>
internal sealed class S101RenderRepeatScenario : IPerfScenario
{
    private const int RepeatCount = 10;

    public string Name => "s101-render-repeat";
    public string Description =>
        $"S-101 warm: open once, Render() ×{RepeatCount} per iteration (issue #488 baseline).";

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

        int layerCount = 0;
        for (int i = 0; i < RepeatCount; i++)
        {
            var result = ProcessorRenderBridge.Render(_processor);
            layerCount = result.Layers.Count;
        }

        if (layerCount == 0)
            throw new InvalidOperationException("Expected at least one layer from S-101 render.");

        return Task.CompletedTask;
    }
}
