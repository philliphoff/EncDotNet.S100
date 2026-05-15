using System.Collections.Immutable;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class DatasetCatalogTests
{
    [Fact]
    public void Replace_raises_Changed_with_specified_kind_and_id()
    {
        var catalog = new FakeDatasetCatalog();
        DatasetCatalogChangedEventArgs? captured = null;
        catalog.Changed += (_, e) => captured = e;

        var dataset = LoadedDatasetFactory.S124("a");
        catalog.Add(dataset);

        Assert.NotNull(captured);
        Assert.Equal(DatasetCatalogChangeKind.Added, captured!.Kind);
        Assert.Equal(dataset.Id, captured.DatasetId);
        Assert.Single(catalog.Datasets);
    }

    [Fact]
    public void Snapshot_captured_before_replace_is_stable()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a"));
        var before = catalog.Datasets;

        catalog.Replace(ImmutableArray<LoadedDataset>.Empty);

        Assert.Single(before);
        Assert.Empty(catalog.Datasets);
    }
}
