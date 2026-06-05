namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// Helpers for env-var-driven real-world dataset scenarios. The actual
/// trial-cell data (UKHO S-100 / S-102 / S-111) is licensed and lives
/// outside the repo; the scenarios below read its location from
/// environment variables so paths are never committed.
/// </summary>
internal static class RealCorpusEnv
{
    public const string S101Var = "ENC_DOTNET_PERF_REAL_S101";
    public const string S102Var = "ENC_DOTNET_PERF_REAL_S102";
    public const string S111Var = "ENC_DOTNET_PERF_REAL_S111";

    public static string Require(string envVar)
    {
        var path = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"Real-corpus scenario requires environment variable {envVar} " +
                "to point at a dataset file. This scenario is not used in CI " +
                "and intentionally has no checked-in fixture.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Dataset referenced by {envVar} does not exist: {path}", path);
        }

        return path;
    }
}

/// <summary>
/// Cold-start S-101 portrayal against a real UKHO trial cell. Path
/// supplied via the <c>ENC_DOTNET_PERF_REAL_S101</c> environment variable
/// (a single <c>.000</c> file).
/// </summary>
internal sealed class S101RealColdScenario : IPerfScenario
{
    public string Name => "s101-real-cold";
    public string Description => "S-101 cold-start: real UKHO .000 cell from $ENC_DOTNET_PERF_REAL_S101.";

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        var path = RealCorpusEnv.Require(RealCorpusEnv.S101Var);

        var factory = SharedInfrastructure.CreatePipelineFactory();
        var processor = factory.CreateProcessor(path);
        var result = ProcessorRenderBridge.Render(processor);

        if (result.Layers.Count == 0)
            throw new InvalidOperationException("Expected at least one layer from S-101 render.");

        return Task.CompletedTask;
    }
}

/// <summary>
/// Warm S-101 render against a real UKHO trial cell, sharing the
/// processor across iterations.
/// </summary>
internal sealed class S101RealWarmScenario : IPerfScenario
{
    public string Name => "s101-real-warm";
    public string Description => "S-101 warm pipeline+render on a real UKHO .000 cell from $ENC_DOTNET_PERF_REAL_S101.";

    private Datasets.Pipelines.IDatasetProcessor? _processor;

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        if (_processor is null)
        {
            var path = RealCorpusEnv.Require(RealCorpusEnv.S101Var);
            var factory = SharedInfrastructure.CreatePipelineFactory();
            _processor = factory.CreateProcessor(path);
        }

        var result = ProcessorRenderBridge.Render(_processor);
        if (result.Layers.Count == 0)
            throw new InvalidOperationException("Expected at least one layer from S-101 render.");

        return Task.CompletedTask;
    }
}

/// <summary>
/// Warm S-102 coverage render against a real UKHO bathymetric tile.
/// Path supplied via the <c>ENC_DOTNET_PERF_REAL_S102</c> environment
/// variable (a single <c>.h5</c> file).
/// </summary>
internal sealed class S102RealWarmScenario : IPerfScenario
{
    public string Name => "s102-real-warm";
    public string Description => "S-102 warm coverage render on a real UKHO .h5 tile from $ENC_DOTNET_PERF_REAL_S102.";

    private Datasets.Pipelines.IDatasetProcessor? _processor;

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        if (_processor is null)
        {
            var path = RealCorpusEnv.Require(RealCorpusEnv.S102Var);
            var factory = SharedInfrastructure.CreatePipelineFactory();
            _processor = factory.CreateProcessor(path);
        }

        var result = ProcessorRenderBridge.Render(_processor);
        if (result.Layers.Count == 0)
            throw new InvalidOperationException("Expected at least one layer from S-102 render.");

        return Task.CompletedTask;
    }
}

/// <summary>
/// Warm S-111 surface-currents render against a real UKHO trial file
/// (typically a single Solent regularly-gridded time-series cell). Path
/// supplied via the <c>ENC_DOTNET_PERF_REAL_S111</c> environment variable.
/// </summary>
internal sealed class S111RealWarmScenario : IPerfScenario
{
    public string Name => "s111-real-warm";
    public string Description => "S-111 warm surface-currents render on a real UKHO .h5 file from $ENC_DOTNET_PERF_REAL_S111.";

    private Datasets.Pipelines.IDatasetProcessor? _processor;

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        if (_processor is null)
        {
            var path = RealCorpusEnv.Require(RealCorpusEnv.S111Var);
            var factory = SharedInfrastructure.CreatePipelineFactory();
            _processor = factory.CreateProcessor(path);
        }

        var result = ProcessorRenderBridge.Render(_processor);
        if (result.Layers.Count == 0)
            throw new InvalidOperationException("Expected at least one layer from S-111 render.");

        return Task.CompletedTask;
    }
}
