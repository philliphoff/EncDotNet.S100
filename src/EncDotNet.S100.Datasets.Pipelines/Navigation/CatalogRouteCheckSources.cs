using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// Builds the <see cref="RouteCheckSources"/> every route check reads from
/// loaded datasets.
/// </summary>
/// <remarks>
/// <para>
/// The bundle uses S-101 cells (and S-401, and translated S-57) for charted
/// features and regulated areas, S-102 surfaces with an S-101 fallback for
/// charted depth, S-104 grids and station series for water level, S-122
/// datasets for protected areas, S-124 datasets for navigational
/// warnings, GRIB2 and NetCDF weather grids for wind and waves, and NHC
/// tropical cyclone advisories (the last one loaded of each storm) for the
/// weather check's tropical cyclone rule. Other products are ignored.
/// </para>
/// <para>
/// Each cell's features and spatial index are built on first use and kept,
/// and the depth, water-level, warning and weather sources are built the
/// first time a check reads them. So build the sources once per set of loaded datasets
/// (<see cref="RouteCheckSourceCache"/> does that) and check as often as the
/// route changes. Every source is safe to call from several threads at once.
/// </para>
/// </remarks>
public static class CatalogRouteCheckSources
{
    /// <summary>Builds the sources from a catalog snapshot.</summary>
    /// <param name="datasets">The loaded datasets, such as <see cref="IDatasetCatalog.Datasets"/>.</param>
    /// <param name="transforms">Reprojects WGS-84 into a projected S-102 or S-104 grid's native CRS.</param>
    /// <param name="options">Tuning, or <c>null</c> for <see cref="RouteCheckSourceOptions.Default"/>.</param>
    /// <returns>The sources.</returns>
    public static RouteCheckSources FromDatasets(
        IEnumerable<LoadedDataset> datasets,
        ICrsTransformFactory transforms,
        RouteCheckSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(transforms);
        options ??= RouteCheckSourceOptions.Default;

        var snapshot = datasets.ToList();
        var charts = new CatalogRouteChartSource(snapshot);
        var reach = new WaterReach(charts, options.MaxStationDistanceMeters);
        return new RouteCheckSources
        {
            Charts = charts,
            Depth = new LazyDepth(() => new CatalogChartedDepthSource(snapshot, transforms, options)),
            WaterLevel = new LazyWaterLevel(() => WaterLevel(snapshot, transforms, options, reach)),
            WaterMask = charts,
            Areas = new CatalogRouteAreaSource(charts, snapshot),
            Warnings = new LazyWarnings(() => new RouteWarningList(
                RouteWarningSources.FromDatasets(snapshot),
                RouteWarningSources.SetFromDatasets(snapshot))),
            Weather = CatalogRouteWeatherSource.HasWeather(snapshot)
                ? new LazyWeather(() => new CatalogRouteWeatherSource(snapshot))
                : null,
            Cyclones = CatalogRouteCyclone.FromDatasets(snapshot),
        };
    }

    /// <summary>
    /// The water mask of a set of loaded datasets: where their S-101 cells
    /// (and S-401, and translated S-57) chart land and water. A station's
    /// reach is measured through it, and the adjusted-area edge is clipped to it.
    /// </summary>
    /// <param name="datasets">The loaded datasets.</param>
    /// <returns>The mask; every point is <see cref="WaterMaskClass.Uncharted"/> when no chart is loaded.</returns>
    public static IWaterMask WaterMaskFromDatasets(IEnumerable<LoadedDataset> datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        return new CatalogRouteChartSource(datasets.ToList());
    }

    // S-104 first, then tide predictions where S-104 doesn't cover a point and time.
    private static IWaterLevelSource WaterLevel(
        IReadOnlyList<LoadedDataset> snapshot, ICrsTransformFactory transforms, RouteCheckSourceOptions options, WaterReach reach)
    {
        var s104 = new CatalogWaterLevelSource(snapshot, transforms, options, reach);
        return options.TidePredictions is { } catalogue
            ? WaterLevelSources.WithFallback(s104, new TidePredictionWaterLevelSource(catalogue, reach, options.TidePredictionOptions, ChartBounds(snapshot)))
            : s104;
    }

    // The bounds of the loaded charts and bathymetry: where predicted water
    // level can be applied to a depth, so the stations the footprint lists.
    private static List<BoundingBox> ChartBounds(IReadOnlyList<LoadedDataset> snapshot)
    {
        var result = new List<BoundingBox>();
        foreach (var dataset in snapshot)
        {
            if (dataset.Data is S101DatasetData or S102CoverageData && dataset.Bounds is { } bounds)
            {
                result.Add(bounds);
            }
        }

        return result;
    }

    private sealed class LazyDepth(Func<IChartedDepthSource> create) : IChartedDepthSource
    {
        private readonly Lazy<IChartedDepthSource> _source = new(create, LazyThreadSafetyMode.ExecutionAndPublication);

        public double? FinestGridSpacingMeters(BoundingBox extent) => _source.Value.FinestGridSpacingMeters(extent);

        public ChartedDepth? Sample(GeoPosition position) => _source.Value.Sample(position);

        public ChartedDepth? SampleBathymetry(GeoPosition position) => _source.Value.SampleBathymetry(position);
    }

    private sealed class LazyWaterLevel(Func<IWaterLevelSource> create) : IWaterLevelSource
    {
        private readonly Lazy<IWaterLevelSource> _source = new(create, LazyThreadSafetyMode.ExecutionAndPublication);

        public IReadOnlyList<IWaterLevelSeries> Locate(GeoPosition position) => _source.Value.Locate(position);

        public IReadOnlyList<WaterLevelSourceInfo> Datasets => _source.Value.Datasets;

        public IReadOnlyList<CoverageArea> Footprint => _source.Value.Footprint;
    }

    private sealed class LazyWeather(Func<IRouteWeatherSource> create) : IRouteWeatherSource
    {
        private readonly Lazy<IRouteWeatherSource> _source = new(create, LazyThreadSafetyMode.ExecutionAndPublication);

        public bool Carries(WeatherQuantity quantity) => _source.Value.Carries(quantity);

        public IReadOnlyList<IRouteWeatherSeries> Locate(GeoPosition position, WeatherQuantity quantity) => _source.Value.Locate(position, quantity);
    }

    private sealed class LazyWarnings(Func<IRouteWarningSource> create) : IRouteWarningSource
    {
        private readonly Lazy<IRouteWarningSource> _source = new(create, LazyThreadSafetyMode.ExecutionAndPublication);

        public NavigationalWarningSet Set => _source.Value.Set;

        public IReadOnlyList<RouteWarning> WarningsNear(BoundingBox extent) => _source.Value.WarningsNear(extent);
    }
}

/// <summary>Tuning for <see cref="CatalogRouteCheckSources.FromDatasets"/>.</summary>
public sealed record RouteCheckSourceOptions
{
    /// <summary>The default options.</summary>
    public static RouteCheckSourceOptions Default { get; } = new();

    /// <summary>
    /// How far an S-104 station's series applies, in metres. A point farther
    /// than this from every station is outside the station dataset's extent.
    /// Default 10 km.
    /// </summary>
    public double MaxStationDistanceMeters { get; init; } = 10_000;

    /// <summary>
    /// Harmonic tide predictions to use for water level where no S-104
    /// covers a point and time, or <c>null</c> (the default) for none. A
    /// prediction is the astronomical tide only, not a forecast, and every
    /// value it gives says so (<see cref="WaterLevelSourceInfo.IsPredicted"/>).
    /// </summary>
    public EncDotNet.S100.Tides.Stations.TideStationCatalogue? TidePredictions { get; init; }

    /// <summary>The datum and dataset id of <see cref="TidePredictions"/>, or <c>null</c> for <see cref="TidePredictionOptions.Default"/>.</summary>
    public TidePredictionOptions? TidePredictionOptions { get; init; }

    /// <summary>
    /// How far an S-101 sounding is used as a depth when no S-101 depth or
    /// dredged area covers a point, in metres. Default 250 m.
    /// </summary>
    public double MaxSoundingDistanceMeters { get; init; } = 250;
}

/// <summary>
/// Keeps one <see cref="RouteCheckSources"/> per catalog snapshot, so every
/// check in a host shares the cells' indexes and S-102 grids, and rebuilds
/// them when the loaded datasets change or tide predictions are turned on or off.
/// </summary>
/// <remarks>
/// A snapshot is matched by reference: <see cref="IDatasetCatalog"/> publishes
/// a new list on every change. <see cref="Get"/> is safe to call from several
/// threads at once.
/// </remarks>
public sealed class RouteCheckSourceCache
{
    private readonly ICrsTransformFactory _transforms;
    private readonly RouteCheckSourceOptions _options;
    private readonly object _gate = new();
    private IReadOnlyList<LoadedDataset>? _snapshot;
    private RouteCheckSources? _sources;
    private EncDotNet.S100.Tides.Stations.TideStationCatalogue? _tidePredictions;

    /// <summary>Creates the cache.</summary>
    /// <param name="transforms">Reprojects WGS-84 into a projected S-102 or S-104 grid's native CRS.</param>
    /// <param name="options">Tuning, or <c>null</c> for <see cref="RouteCheckSourceOptions.Default"/>.</param>
    public RouteCheckSourceCache(ICrsTransformFactory transforms, RouteCheckSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transforms);
        _transforms = transforms;
        _options = options ?? RouteCheckSourceOptions.Default;
        _tidePredictions = _options.TidePredictions;
    }

    /// <summary>
    /// The tide predictions every check, the chart and the pick report use
    /// where no S-104 covers, or <c>null</c> for none (see
    /// <see cref="RouteCheckSourceOptions.TidePredictions"/>). Setting a
    /// different value rebuilds the sources on the next <see cref="Get"/> and
    /// raises <see cref="Changed"/>.
    /// </summary>
    public EncDotNet.S100.Tides.Stations.TideStationCatalogue? TidePredictions
    {
        get
        {
            lock (_gate)
            {
                return _tidePredictions;
            }
        }

        set
        {
            lock (_gate)
            {
                if (ReferenceEquals(value, _tidePredictions))
                {
                    return;
                }

                _tidePredictions = value;
                _sources = null;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Raised when the sources change for a reason other than the loaded
    /// datasets: <see cref="TidePredictions"/> was set.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The sources for a catalog snapshot, built on the first call for it.</summary>
    /// <param name="datasets">The snapshot, such as <see cref="IDatasetCatalog.Datasets"/>.</param>
    /// <returns>The sources.</returns>
    public RouteCheckSources Get(IReadOnlyList<LoadedDataset> datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        lock (_gate)
        {
            if (!ReferenceEquals(datasets, _snapshot) || _sources is null)
            {
                _sources = CatalogRouteCheckSources.FromDatasets(datasets, _transforms, _options with { TidePredictions = _tidePredictions });
                _snapshot = datasets;
            }

            return _sources;
        }
    }
}
