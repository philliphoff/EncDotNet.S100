using EncDotNet.S100.Datasets.Pipelines.Catalog;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>A fixed set of datasets, for a one-shot command.</summary>
/// <param name="datasets">The datasets.</param>
internal sealed class StaticDatasetCatalog(IReadOnlyList<LoadedDataset> datasets) : IDatasetCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<LoadedDataset> Datasets { get; } = datasets;

    /// <inheritdoc />
    /// <remarks>Never raised.</remarks>
    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }
}
