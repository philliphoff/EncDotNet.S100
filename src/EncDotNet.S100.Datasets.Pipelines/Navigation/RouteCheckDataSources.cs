using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// The charted features a <see cref="RouteCheckEngine"/> reads, built from
/// loaded datasets.
/// </summary>
/// <param name="Charts">S-101 cells (and S-401, and translated S-57) with their land, coastlines and drying areas.</param>
public sealed record RouteCheckDataSources(IRouteChartSource Charts)
{
    /// <summary>
    /// Builds sources from a catalog snapshot. S-101 cells (and S-401, and
    /// translated S-57) are used; other products are ignored. Each cell's
    /// features and spatial index are built on first use and kept, so build
    /// the sources once per set of loaded datasets and check as often as the
    /// route changes.
    /// </summary>
    /// <param name="datasets">The loaded datasets, such as <see cref="IDatasetCatalog.Datasets"/>.</param>
    /// <returns>The sources.</returns>
    public static RouteCheckDataSources FromDatasets(IEnumerable<LoadedDataset> datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        return new RouteCheckDataSources(new CatalogRouteChartSource(datasets.ToList()));
    }
}
