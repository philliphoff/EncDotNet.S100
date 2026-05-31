using EncDotNet.S100.Core;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// Exchange set open: open a synthetic multi-product exchange set,
/// walk every dataset via <see cref="Datasets.Pipelines.ExchangeSetLoader"/>,
/// and sum durations.
/// </summary>
internal sealed class ExchangeSetOpenScenario : IPerfScenario
{
    public string Name => "exchange-set-open";
    public string Description => "Open a synthetic exchange set and walk all datasets.";

    public async Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        var esDir = Path.Combine(ctx.CorpusPath, "ExchangeSets", "Synthetic-Mixed");
        if (!Directory.Exists(esDir))
            throw new DirectoryNotFoundException($"Exchange set fixture not found: {esDir}");

        var source = FileSystemAssetSource.Create(esDir);
        using var exchangeSet = await ExchangeSet.OpenAsync(source, cancellationToken: ct);

        var factory = SharedInfrastructure.CreatePipelineFactory();
        var loader = new Datasets.Pipelines.ExchangeSetLoader(factory);

        await foreach (var result in loader.LoadAllAsync(exchangeSet, cancellationToken: ct))
        {
            if (result.Processor is not null)
            {
                // Drive the pipeline for each dataset.
                try
                {
                    result.Processor.RenderAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // Some datasets in the synthetic set may not have
                    // full data; continue to the next.
                }
            }
        }
    }
}
