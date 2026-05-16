using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// PR-F: defends against regressions in large-grid S-102 rendering. The
/// shipped <c>102US004MI1CI262227.h5</c> fixture is ~30 KB / small
/// dimensions, which masks per-pixel and per-cell allocation costs that
/// dominate on real-world grids. This scenario generates a synthetic
/// ~1000×1000 grid once (in a temp directory) and times the warm
/// <c>Render()</c> path on the cached processor — the same shape as
/// <c>S102CoverageScenario</c>, but on a payload that is ~33× larger.
/// </summary>
internal sealed class S102CoverageRenderLargeScenario : IPerfScenario
{
    public string Name => "s102-coverage-render-large";
    public string Description => "S-102 HDF5: warm Render() on a synthetic ~1000×1000 grid.";

    private const int SyntheticDim = 1000;

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

        var result = _processor.Render();

        if (result.Layers.Count == 0)
            throw new InvalidOperationException("Expected at least one layer from S-102 render.");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds a synthetic S-102 HDF5 file in the system temp directory and
    /// returns its path. The file is small enough to leave on disk between
    /// iterations (so we time the warm Render path, not the cold open) but
    /// large enough that per-cell allocations dominate the cost profile.
    /// </summary>
    private static string EnsureSyntheticFixture(int dim)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"encdotnet-s100-perfrunner-s102-{dim}x{dim}.h5");

        if (File.Exists(path))
            return path;

        // Fill with a smooth depth gradient + a constant uncertainty so the
        // depth-shading palette has a realistic distribution of values.
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
        // Atomic move so concurrent runners don't race on a partially-written file.
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    // Layout matches the spec's BathymetryCoverage compound members
    // (depth/uncertainty as f32). Mirrors the writer used by the S-102
    // reader hardening tests.
    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }
}
