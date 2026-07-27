namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// Burst-render scenario for the S-111 arrow renderer path introduced by
/// issue #488. Loads the bundled DBOFS surface-current fixture once and
/// invokes <see cref="ProcessorRenderBridge.Render"/>
/// <see cref="RepeatCount"/> times per iteration. This is the evidence
/// scenario for the option-2 (coverage-only) wiring — the underlying
/// path exercises <see cref="Renderers.Mapsui.MapsuiCoverageArrowRenderer"/>,
/// one of the three renderers today that lack a
/// <c>LayoutCacheEntry</c>-style projection cache and therefore
/// reproject every grid-cell centre on every <c>Render()</c>.
/// </summary>
internal sealed class S111ArrowRepeatScenario : IPerfScenario
{
    private const int RepeatCount = 10;

    public string Name => "s111-arrow-repeat";
    public string Description =>
        $"S-111 warm: open once, Render() ×{RepeatCount} on bundled DBOFS surface-current fixture.";

    private Datasets.Pipelines.IDatasetProcessor? _processor;

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        if (_processor is null)
        {
            var datasetDir = Path.Combine(ctx.CorpusPath, "S111");
            var files = Directory.GetFiles(datasetDir, "*.h5");
            if (files.Length == 0)
                throw new InvalidOperationException($"No .h5 files found in {datasetDir}");

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
            throw new InvalidOperationException("Expected at least one layer from S-111 render.");

        return Task.CompletedTask;
    }
}
