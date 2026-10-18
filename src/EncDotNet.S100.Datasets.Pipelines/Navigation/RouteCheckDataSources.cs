using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// The charted features and bathymetry a <see cref="RouteCheckEngine"/>
/// reads, built from loaded datasets.
/// </summary>
/// <param name="Charts">
/// S-101 cells (and S-401, and translated S-57) with their land, coastlines,
/// depth, dredged and drying areas, dangers and soundings.
/// </param>
/// <param name="Bathymetry">
/// S-102 surfaces for the safety-contour check, or <c>null</c> when none is
/// loaded or no CRS transforms were given. It samples S-102 only, with no
/// S-101 fallback.
/// </param>
public sealed record RouteCheckDataSources(IRouteChartSource Charts, IChartedDepthSource? Bathymetry = null)
{
    /// <summary>
    /// Builds sources from a catalog snapshot. S-101 cells (and S-401, and
    /// translated S-57) are used, and S-102 surfaces when
    /// <paramref name="transforms"/> is given; other products are ignored.
    /// Each cell's features and spatial index are built on first use and
    /// kept, so build the sources once per set of loaded datasets and check
    /// as often as the route changes.
    /// </summary>
    /// <param name="datasets">The loaded datasets, such as <see cref="IDatasetCatalog.Datasets"/>.</param>
    /// <param name="transforms">
    /// Reprojects WGS-84 into a projected S-102 grid's native CRS, or
    /// <c>null</c> to leave S-102 out.
    /// </param>
    /// <returns>The sources.</returns>
    public static RouteCheckDataSources FromDatasets(IEnumerable<LoadedDataset> datasets, ICrsTransformFactory? transforms = null)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        var snapshot = datasets.ToList();
        IChartedDepthSource? bathymetry = null;
        if (transforms is not null)
        {
            var surfaces = snapshot.Where(d => d.Data is S102CoverageData).ToList();
            if (surfaces.Count > 0)
            {
                bathymetry = new CatalogChartedDepthSource(surfaces, transforms, PassageDataSourceOptions.Default);
            }
        }

        return new RouteCheckDataSources(new CatalogRouteChartSource(snapshot), bathymetry);
    }

    /// <summary>Creates an engine over these sources.</summary>
    /// <param name="options">Tuning, or <c>null</c> for <see cref="RouteCheckOptions.Default"/>.</param>
    /// <returns>The engine.</returns>
    public RouteCheckEngine CreateEngine(RouteCheckOptions? options = null) => new(Charts, options, Bathymetry);
}
