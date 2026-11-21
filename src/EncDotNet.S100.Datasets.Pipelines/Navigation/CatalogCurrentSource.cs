using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// Currents from the loaded S-111 datasets (#1033), with another source,
/// such as current-station predictions, where S-111 doesn't cover a place or
/// time.
/// </summary>
/// <remarks>
/// <para>
/// At a place, the S-111 datasets whose bounds contain it are sampled as the
/// <c>sample</c> tool samples them (<see cref="CoverageSampling"/>): the
/// finest grid, else the nearest station, at the time step nearest the time
/// of a dataset whose range contains it. A time outside every dataset's
/// range, or a cell with no value, falls back to the other source.
/// <see cref="IRouteCurrentSeries.Read"/> says which source each value came
/// from.
/// </para>
/// <para>Safe to call from several threads at once.</para>
/// </remarks>
public sealed class CatalogCurrentSource : IRouteCurrentSource
{
    private readonly List<CoverageSamplerEntry<LoadedDataset>> _entries;
    private readonly ICrsTransformFactory _transforms;
    private readonly IRouteCurrentSource? _fallback;

    /// <summary>Creates the source.</summary>
    /// <param name="datasets">The loaded datasets; the S-111 ones are read.</param>
    /// <param name="transforms">Reprojects WGS-84 into a projected grid's native CRS.</param>
    /// <param name="fallback">The source where S-111 doesn't cover a place or time, or <c>null</c>.</param>
    public CatalogCurrentSource(IEnumerable<LoadedDataset> datasets, ICrsTransformFactory transforms, IRouteCurrentSource? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(transforms);
        _entries = [.. datasets
            .Where(d => d.Spec.Name == "S-111" && d.Data is ICoverageSampler)
            .Select(d => new CoverageSamplerEntry<LoadedDataset>(d, (ICoverageSampler)d.Data, d.Bounds))];
        _transforms = transforms;
        _fallback = fallback;
    }

    /// <summary>Whether any S-111 dataset is loaded.</summary>
    public bool HasS111 => _entries.Count > 0;

    /// <summary>Whether any loaded dataset is S-111 with a sampler.</summary>
    /// <param name="datasets">The loaded datasets.</param>
    /// <returns><c>true</c> when one is.</returns>
    public static bool HasCurrents(IEnumerable<LoadedDataset> datasets) =>
        datasets.Any(d => d.Spec.Name == "S-111" && d.Data is ICoverageSampler);

    /// <inheritdoc/>
    public IRouteCurrentSeries? Locate(GeoPosition position)
    {
        var covering = _entries.Where(e => e.DeclaredBounds is not { } b || Contains(b, position)).ToList();
        var fallback = _fallback?.Locate(position);
        if (covering.Count == 0)
        {
            return fallback;
        }

        return new Series(covering, new CoverageSampleQuery(position.Latitude, position.Longitude, _transforms), fallback);
    }

    /// <inheritdoc/>
    public IRouteCurrentStation? FindStation(string idOrName) => _fallback?.FindStation(idOrName);

    private static bool Contains(BoundingBox box, GeoPosition p) =>
        p.Latitude >= box.SouthLatitude && p.Latitude <= box.NorthLatitude
        && (box.WestLongitude <= box.EastLongitude
            ? p.Longitude >= box.WestLongitude && p.Longitude <= box.EastLongitude
            : p.Longitude >= box.WestLongitude || p.Longitude <= box.EastLongitude);

    // The S-111 datasets at one place, then the fallback.
    private sealed class Series(List<CoverageSamplerEntry<LoadedDataset>> entries, CoverageSampleQuery query, IRouteCurrentSeries? fallback) : IRouteCurrentSeries
    {
        public RouteCurrentSourceInfo Source { get; } =
            new(entries[0].Source.Id.Value, null, RouteCurrentOrigin.S111, null);

        public RouteCurrent? At(DateTimeOffset time) => Read(time)?.Current;

        public RouteCurrentReading? Read(DateTimeOffset time)
        {
            if (CoverageSampling.SampleAt(entries, query, time) is CoverageSampleOutcome<LoadedDataset>.Sampled sampled)
            {
                (double Knots, double Direction)? value = sampled.Value switch
                {
                    SurfaceCurrentSample grid => (grid.SpeedKnots, grid.DirectionDegreesTrue),
                    SurfaceCurrentStationSample station => (station.SpeedKnots, station.DirectionDegreesTrue),
                    _ => null,
                };
                if (value is var (knots, direction) && double.IsFinite(knots) && double.IsFinite(direction))
                {
                    var info = new RouteCurrentSourceInfo(sampled.Source.Id.Value, null, RouteCurrentOrigin.S111, null);
                    return new RouteCurrentReading(new RouteCurrent(knots, direction), info);
                }
            }

            return fallback?.Read(time);
        }
    }
}
