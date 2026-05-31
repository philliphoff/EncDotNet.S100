namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// Cold-start S-101 portrayal: one iteration with no warmup. Captures
/// parse + first-pass Lua + first-pass XSLT compile cost.
/// </summary>
internal sealed class S101PortrayColdScenario : IPerfScenario
{
    public string Name => "s101-portray-cold";
    public string Description => "S-101 cold-start: open, parse, and portray a single fixture once.";

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        var datasetDir = Path.Combine(ctx.CorpusPath, "S101", "S-101", "DATASET_FILES");
        var files = Directory.GetFiles(datasetDir, "*.000");
        if (files.Length == 0)
            throw new InvalidOperationException($"No .000 files found in {datasetDir}");

        var factory = SharedInfrastructure.CreatePipelineFactory();
        var processor = factory.CreateProcessor(files[0]);
        processor.RenderAsync().GetAwaiter().GetResult();

        return Task.CompletedTask;
    }
}
