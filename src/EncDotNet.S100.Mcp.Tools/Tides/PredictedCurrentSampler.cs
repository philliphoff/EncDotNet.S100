using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Mcp.Tools.Tides;

/// <summary>
/// sample_coverage's S-111 fallback (#1454): the predicted tidal current at
/// the nearest current station by water, used where no S-111 covers the
/// point and time.
/// </summary>
/// <remarks>
/// The stations come from the host's <see cref="RouteCheckSourceCache.TidePredictions"/>
/// catalogue, so they follow the same switch as the S-104 fallback
/// (<c>s100 mcp serve --tide-predictions</c>). A station reaches
/// <see cref="WaterReach.DefaultReachMeters"/> through the loaded charts'
/// water (design D19); of the depth bins at one place the shallowest is used,
/// as the current nearest the surface. Predicted currents are for display
/// and sampling only: route checks don't use them.
/// </remarks>
internal sealed class PredictedCurrentSampler
{
    /// <summary>The dataset id a predicted current is reported under.</summary>
    public const string DatasetId = "current-predictions";

    // Straight-line candidates looked at for the nearest by water: NOAA has
    // several depth bins at one place, so more than for tide stations.
    private const int Candidates = 32;

    private readonly IDatasetCatalog _catalog;
    private readonly RouteCheckSourceCache _sources;
    private readonly TimeProvider _time;

    public PredictedCurrentSampler(IDatasetCatalog catalog, RouteCheckSourceCache sources, TimeProvider time)
    {
        _catalog = catalog;
        _sources = sources;
        _time = time;
    }

    /// <summary>The predicted sample for the request, or <c>null</c> when predictions are off or no station reaches the point.</summary>
    public ToolResult<SampleCoverageResult>? Sample(SampleCoverageRequest request)
    {
        if (_sources.TidePredictions is not { } catalogue || catalogue.CurrentStations.Count == 0)
            return null;

        var position = new GeoPosition(request.Latitude, request.Longitude);
        var reach = new WaterReach(_sources.Get(_catalog.Datasets).WaterMask);
        if (Locate(catalogue, reach, position) is not var (station, prediction, distance))
            return null;

        var info = prediction.Source;
        var sampleSource = new SampleSource(
            "prediction",
            new SampleSourceStation(
                info.StationId,
                info.StationName,
                "current",
                TideToolText.Kind(info.Kind),
                info.ReferenceStationId,
                Math.Round(distance)),
            null,
            TideToolText.CurrentCaveat,
            catalogue.Retrieved?.ToUniversalTime());

        var requested = request.Times is TimeQuery.Instant i ? i.Value : request.Time;
        if (request.Times is TimeQuery.Range or TimeQuery.Series)
        {
            var times = request.Times;
            var (from, to) = times.GetWindow();
            IReadOnlyList<DateTimeOffset> instants;
            if (times is TimeQuery.Series s)
            {
                if (s.EstimatedCount > PredictedWaterLevelSampler.MaxPoints)
                    return Invalid($"the series is {s.EstimatedCount} instants; a predicted series is at most {PredictedWaterLevelSampler.MaxPoints}");
                instants = s.Enumerate();
            }
            else
            {
                var step = PredictedWaterLevelSampler.RangeStep;
                var count = (int)Math.Min(int.MaxValue, ((to - from).Ticks / step.Ticks) + 1);
                if (count > PredictedWaterLevelSampler.MaxPoints)
                    return Invalid($"the range is {count} half-hour steps; a predicted series is at most {PredictedWaterLevelSampler.MaxPoints}");
                instants = [.. Enumerable.Range(0, Math.Max(1, count)).Select(k => from + (step * k))];
            }

            var values = instants
                .Select(t => new TimedSampledValue(t.UtcDateTime, t, Value(prediction.Predictor, station, distance, t.UtcDateTime, t)))
                .ToList();
            return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
                new DatasetId(DatasetId),
                request.Latitude,
                request.Longitude,
                values[0].Value!,
                values,
                Truncated: false,
                CoveredFrom: from,
                CoveredTo: to,
                Source: sampleSource));
        }

        var at = (requested ?? _time.GetUtcNow()).UtcDateTime;
        return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
            new DatasetId(DatasetId),
            request.Latitude,
            request.Longitude,
            Value(prediction.Predictor, station, distance, at, requested),
            TimeStatus: requested is null ? null : "in_range",
            Source: sampleSource));
    }

    private static (CurrentStationEntry Station, CurrentStationPrediction Prediction, double Distance)? Locate(
        TideStationCatalogue catalogue, WaterReach reach, GeoPosition position)
    {
        (CurrentStationEntry Station, CurrentStationPrediction Prediction, double Distance)? best = null;
        foreach (var match in catalogue.FindNearestCurrentStations(position, Candidates, reach.ReachMeters))
        {
            if (best is { } b && match.DistanceMeters > b.Distance + 1)
            {
                // The distance by water is never shorter than the straight one.
                break;
            }

            if (reach.DistanceMeters(match.Station.Position, position) is not { } distance
                || (best is { } current && !Better(match.Station, distance, current.Station, current.Distance))
                || catalogue.TryCreateCurrentPrediction(match.Station.Id) is not { } prediction)
            {
                continue;
            }

            best = (match.Station, prediction, distance);
        }

        return best;
    }

    // Nearer wins; at one place (within a metre), the shallower bin.
    private static bool Better(CurrentStationEntry station, double distance, CurrentStationEntry best, double bestDistance) =>
        Math.Abs(distance - bestDistance) <= 1
            ? (station.DepthMeters ?? double.MaxValue) < (best.DepthMeters ?? double.MaxValue)
            : distance < bestDistance;

    private static SurfaceCurrentStationSample Value(ICurrentPredictor predictor, CurrentStationEntry station, double distance, DateTime utc, DateTimeOffset? requested)
    {
        var current = predictor.CurrentAt(utc);
        return new SurfaceCurrentStationSample(
            station.Id,
            Math.Round(distance),
            Math.Round(current.SpeedMetersPerSecond, 3),
            Math.Round(current.SpeedKnots, 2),
            Math.Round(current.DirectionDegrees, 1) % 360,
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            requested,
            station.Position.Latitude,
            station.Position.Longitude);
    }

    private static ToolResult<SampleCoverageResult> Invalid(string reason) =>
        ToolResult<SampleCoverageResult>.Err(new InvalidArgument("times", reason));
}
