using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// The charted-depth and water-level sources a <see cref="PassageCheckEngine"/>
/// reads, built from loaded datasets.
/// </summary>
/// <param name="Depth">S-102 bathymetry with S-101 fallback.</param>
/// <param name="WaterLevel">S-104 grids and stations.</param>
public sealed record PassageDataSources(IChartedDepthSource Depth, IWaterLevelSource WaterLevel)
{
    /// <summary>
    /// Builds sources from a catalog snapshot. S-102 coverages, S-101 (and
    /// S-401, and translated S-57) cells, S-104 grids and S-104 station
    /// series are used; other products are ignored.
    /// </summary>
    /// <param name="datasets">The loaded datasets, such as <see cref="IDatasetCatalog.Datasets"/>.</param>
    /// <param name="transforms">Reprojects WGS-84 into a projected S-102 or S-104 grid's native CRS.</param>
    /// <param name="options">Tuning, or <c>null</c> for the defaults.</param>
    /// <returns>The sources.</returns>
    public static PassageDataSources FromDatasets(
        IEnumerable<LoadedDataset> datasets,
        ICrsTransformFactory transforms,
        PassageDataSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(transforms);
        options ??= PassageDataSourceOptions.Default;

        var snapshot = datasets.ToList();
        return new PassageDataSources(
            new CatalogChartedDepthSource(snapshot, transforms, options),
            new CatalogWaterLevelSource(snapshot, transforms, options));
    }
}

/// <summary>Tuning for <see cref="PassageDataSources.FromDatasets"/>.</summary>
public sealed record PassageDataSourceOptions
{
    /// <summary>The default options.</summary>
    public static PassageDataSourceOptions Default { get; } = new();

    /// <summary>
    /// How far an S-104 station's series applies, in metres. A point farther
    /// than this from every station is outside the station dataset's extent.
    /// Default 10 km.
    /// </summary>
    public double MaxStationDistanceMeters { get; init; } = 10_000;

    /// <summary>
    /// How far an S-101 sounding is used as a depth when no S-101 depth or
    /// dredged area covers a point, in metres. Default 250 m.
    /// </summary>
    public double MaxSoundingDistanceMeters { get; init; } = 250;
}
