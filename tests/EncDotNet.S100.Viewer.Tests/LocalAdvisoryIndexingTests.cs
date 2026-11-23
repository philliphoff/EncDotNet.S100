using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// An NHC advisory in a local Library source indexes through the viewer's
/// metadata probe as one TC item, and a change to the zips beside its
/// forecast zip reaches the item, past the metadata cache (#1688).
/// </summary>
public sealed class LocalAdvisoryIndexingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-local-advisory-" + Guid.NewGuid().ToString("N"));

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
    public async Task A_forecast_zip_indexes_as_TC_and_a_changed_best_track_refreshes_it()
    {
        var zip = TropicalCycloneFixtures.CopyIan(Path.Combine(_root, "ian"));
        var probe = new CachingDatasetMetadataReader(
            new DiskDatasetMetadataCache(EncDotNet.S100.Testing.TestContentKinds.Open(Path.Combine(_root, "cache"), 1_000_000, DiskDatasetMetadataCache.KindOptions)),
            registry: EncDotNet.S100.Datasets.Pipelines.ProductRegistry.Default);
        var indexer = new LocalSourceIndexer((path, _) => probe.TryRead(path));
        var source = new ExchangeSetSource(Guid.NewGuid(), null, zip);

        var first = await indexer.IndexAsync(source, null, TestContext.Current.CancellationToken);

        var item = Assert.Single(first.Items);
        Assert.Equal("TC", item.ProductSpec);
        Assert.Equal("al092022_5day_020.zip", item.Key);
        Assert.NotNull(item.Bounds);
        Assert.Empty(first.Diagnostics);

        // Without the best track the advisory starts at its synoptic time, not Ian's first past point.
        File.Delete(Path.Combine(_root, "ian", "al092022_best_track.zip"));
        var second = await indexer.IndexAsync(source, null, TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
        Assert.True(
            DateTimeOffset.Parse(Assert.Single(second.Items).Properties["timeStart"], CultureInfo.InvariantCulture)
            > DateTimeOffset.Parse(item.Properties["timeStart"], CultureInfo.InvariantCulture));
    }
}
