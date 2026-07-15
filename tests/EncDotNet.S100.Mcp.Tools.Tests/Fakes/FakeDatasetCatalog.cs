using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

internal sealed class FakeDatasetCatalog : IDatasetCatalog
{
    public IReadOnlyList<LoadedDataset> Datasets { get; private set; } = [];

    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;

    public void Replace(IReadOnlyList<LoadedDataset> next, DatasetCatalogChangeKind kind = DatasetCatalogChangeKind.Batch, DatasetId? id = null)
    {
        Datasets = next;
        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs { Kind = kind, DatasetId = id });
    }

    public void Add(LoadedDataset dataset)
    {
        Replace([.. Datasets, dataset], DatasetCatalogChangeKind.Added, dataset.Id);
    }
}
