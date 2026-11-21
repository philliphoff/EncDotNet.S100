using System.Collections.Concurrent;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// Water level from harmonic tide predictions: at a point, the tide station
/// nearest by water within the reach, predicted on one datum.
/// </summary>
/// <remarks>
/// <para>
/// A prediction is the astronomical tide only, not a forecast: wind,
/// pressure and river flow aren't in it. Combine it after S-104 with
/// <see cref="WaterLevelSources.WithFallback"/> so S-104 wins wherever it
/// covers a point and time; <see cref="CatalogRouteCheckSources"/> does that
/// when <see cref="RouteCheckSourceOptions.TidePredictions"/> is set.
/// </para>
/// <para>
/// A station applies within <see cref="WaterReach.ReachMeters"/> of it,
/// measured through the loaded charts' water (tide predictions design D19).
/// A subordinate station is predicted from its reference station with
/// NOAA's offsets interpolated between each high and low (D20), and its
/// <see cref="WaterLevelSourceInfo.ReferenceStationId"/> names the reference.
/// </para>
/// <para>
/// Heights are given on <see cref="TidePredictionOptions.Datum"/>, the
/// station's chart datum (MLLW for NOAA). A station that can't be predicted on
/// it is passed over. The datum agreement rules then apply as for S-104: on a
/// different datum from the depths, the height is reported and not added.
/// </para>
/// <para>Safe to call from several threads at once.</para>
/// </remarks>
public sealed class TidePredictionWaterLevelSource : IWaterLevelSource
{
    // Straight-line candidates looked at for the nearest by water.
    private const int Candidates = 8;

    private readonly TideStationCatalogue _catalogue;
    private readonly TidePredictionOptions _options;
    private readonly WaterReach _reach;
    private readonly ConcurrentDictionary<string, Lazy<StationPrediction?>> _predictions = new(StringComparer.Ordinal);
    private readonly Lazy<IReadOnlyList<CoverageArea>> _footprint;

    /// <summary>Creates the source.</summary>
    /// <param name="catalogue">The tide stations.</param>
    /// <param name="reach">How far a station reaches, and through what water.</param>
    /// <param name="options">The datum and the dataset id to report; <c>null</c> for <see cref="TidePredictionOptions.Default"/>.</param>
    /// <param name="footprintAreas">
    /// The areas whose stations <see cref="Footprint"/> lists, such as the
    /// loaded charts' bounds; <c>null</c> or empty for an empty footprint. A
    /// whole catalogue's discs would be too many to draw or clip with.
    /// </param>
    public TidePredictionWaterLevelSource(
        TideStationCatalogue catalogue,
        WaterReach reach,
        TidePredictionOptions? options = null,
        IReadOnlyList<BoundingBox>? footprintAreas = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(reach);
        _catalogue = catalogue;
        _reach = reach;
        _options = options ?? TidePredictionOptions.Default;
        Datasets = [new WaterLevelSourceInfo(_options.DatasetId, WaterLevelSourceKind.Prediction, (int)_options.Datum)];
        var areas = footprintAreas ?? [];
        _footprint = new Lazy<IReadOnlyList<CoverageArea>>(() => BuildFootprint(areas), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The station catalogue.</summary>
    public TideStationCatalogue Catalogue => _catalogue;

    /// <inheritdoc />
    public IReadOnlyList<WaterLevelSourceInfo> Datasets { get; }

    /// <summary>
    /// A disc of the reach round each station that can be predicted on the
    /// datum and reaches into the footprint areas given at construction:
    /// where <see cref="Locate"/> can return a series there. The discs are
    /// straight-line; land isn't cut out of them.
    /// </summary>
    public IReadOnlyList<CoverageArea> Footprint => _footprint.Value;

    /// <inheritdoc />
    public IReadOnlyList<IWaterLevelSeries> Locate(GeoPosition position)
    {
        PredictedSeries? best = null;
        foreach (var match in _catalogue.FindNearest(position, Candidates, _reach.ReachMeters))
        {
            if (best is not null && match.DistanceMeters >= best.Source.DistanceMeters)
            {
                // The distance by water is never shorter than the straight one.
                break;
            }

            if (_reach.DistanceMeters(match.Station.Position, position) is not { } distance
                || (best is not null && distance >= best.Source.DistanceMeters)
                || Prediction(match.Station.Id) is not { } prediction)
            {
                continue;
            }

            best = new PredictedSeries(prediction, _options, distance);
        }

        return best is null ? [] : [best];
    }

    /// <summary>
    /// The series of one station, or <c>null</c> when the catalogue doesn't
    /// have it or can't predict it on <see cref="TidePredictionOptions.Datum"/>.
    /// </summary>
    /// <param name="stationId">The station identifier.</param>
    /// <returns>The series, with no distance.</returns>
    public IWaterLevelSeries? SeriesFor(string stationId)
    {
        ArgumentNullException.ThrowIfNull(stationId);
        return Prediction(stationId) is { } prediction ? new PredictedSeries(prediction, _options, null) : null;
    }

    private List<CoverageArea> BuildFootprint(IReadOnlyList<BoundingBox> areas)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CoverageArea>();
        foreach (var area in areas)
        {
            // Stations up to the reach outside the area reach into it.
            var padLat = _reach.ReachMeters / PassageGeodesy.MetersPerDegreeLatitude;
            var midLat = (area.SouthLatitude + area.NorthLatitude) / 2;
            var padLon = padLat / Math.Max(0.01, Math.Cos(Math.Min(89, Math.Abs(midLat) + padLat) * Math.PI / 180));
            var box = new BoundingBox(
                Math.Max(-90, area.SouthLatitude - padLat),
                area.WestLongitude - padLon,
                Math.Min(90, area.NorthLatitude + padLat),
                area.EastLongitude + padLon);
            foreach (var match in _catalogue.FindWithin(box))
            {
                if (seen.Add(match.Station.Id) && Prediction(match.Station.Id) is not null)
                {
                    result.Add(Disc(match.Station.Position, _reach.ReachMeters));
                }
            }
        }

        return result;
    }

    private static CoverageArea Disc(GeoPosition centre, double radiusMeters)
    {
        const int vertices = 36;
        var ring = new List<GeoPosition>(vertices + 1);
        var cosLat = Math.Max(Math.Cos(centre.Latitude * Math.PI / 180), 1e-6);
        for (var i = 0; i < vertices; i++)
        {
            var bearing = 2 * Math.PI * i / vertices;
            var dLat = radiusMeters * Math.Cos(bearing) / PassageGeodesy.MetersPerDegreeLatitude;
            var dLon = radiusMeters * Math.Sin(bearing) / (PassageGeodesy.MetersPerDegreeLatitude * cosLat);
            ring.Add(new GeoPosition(centre.Latitude + dLat, centre.Longitude + dLon));
        }

        ring.Add(ring[0]);
        return new CoverageArea { ExteriorRing = ring };
    }

    private StationPrediction? Prediction(string stationId) =>
        _predictions.GetOrAdd(
            stationId,
            id => new Lazy<StationPrediction?>(
                () => _catalogue.TryCreatePrediction(id, _options.Datum) is { } p ? new StationPrediction(p) : null,
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>A station's predictor, with the last height asked of it kept: a chart asks every node at one time.</summary>
    private sealed class StationPrediction(TideStationPrediction prediction)
    {
        private LastHeight? _last;

        public TideStationPrediction Prediction { get; } = prediction;

        public double HeightAt(DateTime utc)
        {
            if (Volatile.Read(ref _last) is { } last && last.Time == utc)
            {
                return last.Height;
            }

            var height = Prediction.Predictor.HeightAt(utc);
            Volatile.Write(ref _last, new LastHeight(utc, height));
            return height;
        }

        private sealed record LastHeight(DateTime Time, double Height);
    }

    private sealed class PredictedSeries : IWaterLevelSeries
    {
        private readonly StationPrediction _station;

        public PredictedSeries(StationPrediction station, TidePredictionOptions options, double? distance)
        {
            _station = station;
            var source = station.Prediction.Source;
            Source = new WaterLevelSourceInfo(
                options.DatasetId,
                WaterLevelSourceKind.Prediction,
                (int)options.Datum,
                source.StationId,
                distance)
            {
                StationName = source.StationName,
                ReferenceStationId = source.ReferenceStationId,
                ReferenceStationName = source.ReferenceStationName,
            };
        }

        public WaterLevelSourceInfo Source { get; }

        /// <summary>A prediction has no start.</summary>
        public DateTime Start => TidePredictedSeries.Start;

        /// <summary>A prediction has no end.</summary>
        public DateTime End => TidePredictedSeries.End;

        public WaterLevelReading? ValueAt(DateTime utc)
        {
            utc = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return new WaterLevelReading(_station.HeightAt(utc), utc);
        }

        /// <summary>
        /// The highs and lows inside the period: with the two ends, they hold
        /// its lowest water exactly.
        /// </summary>
        public IReadOnlyList<DateTime> StepsWithin(DateTime start, DateTime end)
        {
            if (end - start > TidePredictedSeries.MaxStepsPeriod)
            {
                throw new ArgumentException($"A predicted series lists its steps over at most {TidePredictedSeries.MaxStepsPeriod.TotalDays:0} days.", nameof(end));
            }

            return end <= start
                ? []
                : _station.Prediction.Predictor.FindExtremes(start, end).Select(e => e.Time).ToList();
        }
    }
}

/// <summary>Constants shared by predicted water-level series.</summary>
public static class TidePredictedSeries
{
    /// <summary>
    /// <see cref="IWaterLevelSeries.Start"/> of a predicted series: a
    /// prediction has no start, so the earliest UTC time.
    /// </summary>
    public static DateTime Start { get; } = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    /// <summary>
    /// <see cref="IWaterLevelSeries.End"/> of a predicted series: a
    /// prediction has no end, so the latest UTC time.
    /// </summary>
    public static DateTime End { get; } = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

    /// <summary>
    /// The longest period whose steps (highs and lows) a predicted series
    /// lists: 400 days. Ask for a window, never <c>Start</c>…<c>End</c>.
    /// </summary>
    public static TimeSpan MaxStepsPeriod { get; } = TimeSpan.FromDays(400);
}

/// <summary>Options for <see cref="TidePredictionWaterLevelSource"/>.</summary>
public sealed record TidePredictionOptions
{
    /// <summary>The default options: MLLW, reported as <see cref="WaterLevelSourceInfo.PredictionDatasetId"/>.</summary>
    public static TidePredictionOptions Default { get; } = new();

    /// <summary>
    /// The datum to predict on: the stations' chart datum. Default
    /// <see cref="VerticalDatum.MeanLowerLowWater"/>, as NOAA's charts use.
    /// </summary>
    public VerticalDatum Datum { get; init; } = VerticalDatum.MeanLowerLowWater;

    /// <summary>The <see cref="WaterLevelSourceInfo.DatasetId"/> predicted values report.</summary>
    public string DatasetId { get; init; } = WaterLevelSourceInfo.PredictionDatasetId;
}
