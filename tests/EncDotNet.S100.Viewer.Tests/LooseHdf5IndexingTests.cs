using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Testing.Scenarios;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The scenario generator's HDF5 coverages index as loose datasets through the
/// viewer's metadata probe, with no exchange-set catalogue to describe them (#1037).
/// </summary>
public sealed class LooseHdf5IndexingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-loose-hdf5-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Generated_coverages_index_loose_with_the_catalogue_spec_and_bounds()
    {
        // Between them: S-102, S-104 grid and stations, S-111 grid and stations.
        var scenarios = new[] { SyntheticScenarios.ApproachWithTideWindow(), SyntheticScenarios.PortPlanVsOwnRoute() }
            .Select(d => ScenarioGenerator.Generate(d, Path.Combine(_root, d.Name)))
            .ToList();

        var loose = Path.Combine(_root, "loose");
        Directory.CreateDirectory(loose);
        var probe = new CachingDatasetMetadataReader(new DiskDatasetMetadataCache(Path.Combine(_root, "cache"), 1_000_000));
        var indexer = new LocalSourceIndexer((path, _) => probe.TryRead(path));

        var expected = new Dictionary<string, CollectionItem>(StringComparer.Ordinal);
        foreach (var scenario in scenarios)
        {
            var catalogued = await indexer.IndexAsync(
                new ExchangeSetSource(Guid.NewGuid(), null, scenario.Root), null, TestContext.Current.CancellationToken);
            foreach (var dataset in scenario.Datasets.Where(d => d.FullPath.EndsWith(".h5", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileName(dataset.FullPath);
                File.Copy(dataset.FullPath, Path.Combine(loose, name));
                expected[name] = catalogued.Items.Single(i => i.Key.EndsWith(name, StringComparison.Ordinal));
            }
        }

        var index = await indexer.IndexAsync(
            new LocalFolderSource(Guid.NewGuid(), null, loose), null, TestContext.Current.CancellationToken);

        Assert.Empty(index.Diagnostics);
        Assert.Equal(
            ["S-102", "S-104", "S-111"],
            expected.Values.Select(i => i.ProductSpec).Distinct().Order());
        Assert.Equal(expected.Keys.Order(), index.Items.Select(i => i.Key).Order());
        foreach (var item in index.Items)
        {
            var catalogued = expected[item.Key];
            Assert.Equal(catalogued.ProductSpec, item.ProductSpec);
            Assert.NotNull(item.Bounds);
            Assert.NotNull(catalogued.Bounds);
            // The catalogue box and the grid footprint may differ by part of a cell.
            const double tolerance = 0.005;
            Assert.Equal(catalogued.Bounds.Value.South, item.Bounds.Value.South, tolerance);
            Assert.Equal(catalogued.Bounds.Value.West, item.Bounds.Value.West, tolerance);
            Assert.Equal(catalogued.Bounds.Value.North, item.Bounds.Value.North, tolerance);
            Assert.Equal(catalogued.Bounds.Value.East, item.Bounds.Value.East, tolerance);
        }
    }
}
