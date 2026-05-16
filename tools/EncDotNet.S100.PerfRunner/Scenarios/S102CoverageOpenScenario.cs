using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.PerfRunner.Scenarios;

/// <summary>
/// PR-F: defends against cold-open regressions in S-102. Each iteration
/// opens the HDF5 fixture, parses the S-102 dataset header, and disposes
/// the file — no portrayal, no render. Catches changes that bloat the
/// per-attribute reads in <c>S102DatasetReader.Read</c> (which probes
/// the root for multiple optional attributes) or the
/// <c>PureHdfGroup</c> attribute/group scans.
/// </summary>
internal sealed class S102CoverageOpenScenario : IPerfScenario
{
    public string Name => "s102-coverage-open";
    public string Description => "S-102 HDF5: cold-open + dataset header read (no render).";

    public Task RunAsync(PerfContext ctx, CancellationToken ct)
    {
        var h5Path = Path.Combine(ctx.CorpusPath, "S102", "102US004MI1CI262227.h5");
        if (!File.Exists(h5Path))
            throw new FileNotFoundException($"S-102 fixture not found: {h5Path}");

        using var file = PureHdfFile.Open(h5Path);
        var dataset = S102DatasetReader.Read(file);

        if (dataset.Coverages.Count == 0)
            throw new InvalidOperationException("Expected at least one coverage in S-102 dataset.");

        return Task.CompletedTask;
    }
}
