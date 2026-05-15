using System.Collections.Immutable;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal sealed class FakeDatasetCatalog : IDatasetCatalog
{
    public ImmutableArray<LoadedDataset> Datasets { get; private set; } = ImmutableArray<LoadedDataset>.Empty;

    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;

    public void Replace(ImmutableArray<LoadedDataset> next, DatasetCatalogChangeKind kind = DatasetCatalogChangeKind.Batch, DatasetId? id = null)
    {
        Datasets = next;
        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs { Kind = kind, DatasetId = id });
    }

    public void Add(LoadedDataset dataset)
    {
        Replace(Datasets.Add(dataset), DatasetCatalogChangeKind.Added, dataset.Id);
    }
}
