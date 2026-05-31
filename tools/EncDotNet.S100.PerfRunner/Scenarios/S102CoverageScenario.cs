namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// S-102 coverage: read HDF5 bathymetry fixture, run coverage pipeline,
/// and render via MapsuiCoverageRenderer.
/// </summary>
internal sealed class S102CoverageScenario : IPerfScenario
{
    public string Name => "s102-coverage";
    public string Description => "S-102 HDF5 bathymetry: coverage pipeline + render.";

    private Datasets.Pipelines.IDatasetProcessor? _processor;

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        if (_processor is null)
        {
            var h5Path = Path.Combine(ctx.CorpusPath, "S102", "102US004MI1CI262227.h5");
            if (!File.Exists(h5Path))
                throw new FileNotFoundException($"S-102 fixture not found: {h5Path}");

            var factory = SharedInfrastructure.CreatePipelineFactory();
            _processor = factory.CreateProcessor(h5Path);
        }

        var result = _processor.RenderAsync().GetAwaiter().GetResult();

        if (result.Layers.Count == 0)
            throw new InvalidOperationException("Expected at least one layer from S-102 render.");

        return Task.CompletedTask;
    }
}
