using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// Burst-render variant of <see cref="S102CoverageRenderLargeScenario"/>
/// introduced for issue #488: opens a synthetic ~1000×1000 S-102 grid
/// once and invokes <see cref="ProcessorRenderBridge.RenderLayerCount"/>
/// <see cref="RepeatCount"/> times per iteration on the cached
/// processor. Isolates steady-state per-render projection cost from
/// open + first-frame cost, so a reproject-once cache (PR #2) shows
/// up as (1 miss + N-1 hits) / N per iteration and lets the reviewer
/// gauge the marginal saving vs. the amortisation already provided by
/// <c>MapsuiCoverageRenderer.LayoutCacheEntry</c> (PR #179).
/// </summary>
internal sealed class S102CoverageRenderRepeatScenario : IPerfScenario
{
    private const int SyntheticDim = 1000;
    private const int RepeatCount = 10;

    public string Name => "s102-coverage-render-repeat";
    public string Description =>
        $"S-102 warm: open once, Render() ×{RepeatCount} per iteration on ~{SyntheticDim}×{SyntheticDim} grid.";

    private Datasets.Pipelines.IDatasetProcessor? _processor;
    private string? _fixturePath;

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        if (_processor is null)
        {
            _fixturePath = EnsureSyntheticFixture(SyntheticDim);
            var factory = SharedInfrastructure.CreatePipelineFactory();
            _processor = factory.CreateProcessor(_fixturePath);
        }

        int layerCount = 0;
        for (int i = 0; i < RepeatCount; i++)
        {
            layerCount = ProcessorRenderBridge.RenderLayerCount(_processor);
        }

        if (layerCount == 0)
            throw new InvalidOperationException("Expected at least one layer from S-102 render.");

        return Task.CompletedTask;
    }

    private static string EnsureSyntheticFixture(int dim)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"encdotnet-s100-perfrunner-s102-{dim}x{dim}.h5");

        if (File.Exists(path))
            return path;

        var values = new SpecBathyRow[dim * dim];
        for (int r = 0; r < dim; r++)
        {
            for (int c = 0; c < dim; c++)
            {
                float d = 1f + (r + c) * (50f / (dim * 2f));
                values[r * dim + c] = new SpecBathyRow { Depth = d, Uncertainty = 0.1f };
            }
        }

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = 50.0,
                ["gridOriginLongitude"] = -1.0,
                ["gridSpacingLatitudinal"] = 0.0001,
                ["gridSpacingLongitudinal"] = 0.0001,
                ["numPointsLatitudinal"] = dim,
                ["numPointsLongitudinal"] = dim,
            },
            ["Group_001"] = new H5Group { ["values"] = values },
        };

        var file = new H5File
        {
            Attributes = new()
            {
                ["horizontalCRS"] = 4326,
            },
            ["BathymetryCoverage"] = new H5Group
            {
                ["BathymetryCoverage.01"] = instance,
            },
        };

        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);

        var tmp = path + ".tmp";
        file.Write(tmp, options);
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }
}
