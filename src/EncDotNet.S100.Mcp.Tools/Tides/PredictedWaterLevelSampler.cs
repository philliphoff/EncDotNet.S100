using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools.Tides;

/// <summary>
/// sample_coverage's S-104 fallback (#1452): the predicted tide at the
/// nearest station by water, used where no S-104 covers the point and time,
/// as the route check and the Depth card use it (T3, #1451).
/// </summary>
/// <remarks>
/// The predictions are the host's <see cref="RouteCheckSourceCache.TidePredictions"/>,
/// so they follow its switch (SoundCharts' "Use tide predictions where there's
/// no S-104", <c>s100 mcp serve --tide-predictions</c>). A station reaches
/// <see cref="WaterReach.DefaultReachMeters"/> through the loaded charts' water.
/// </remarks>
internal sealed class PredictedWaterLevelSampler
{
    /// <summary>The step of a range query's predicted series.</summary>
    public static readonly TimeSpan RangeStep = TimeSpan.FromMinutes(30);

    /// <summary>The most heights one windowed query returns.</summary>
    public const int MaxPoints = 2000;

    // A trend compares the rise over an hour with this, as S-104 files do.
    private const double TrendThresholdMeters = 0.2;

    private readonly IDatasetCatalog _catalog;
    private readonly RouteCheckSourceCache _sources;
    private readonly TimeProvider _time;

    public PredictedWaterLevelSampler(IDatasetCatalog catalog, RouteCheckSourceCache sources, TimeProvider time)
    {
        _catalog = catalog;
        _sources = sources;
        _time = time;
    }

    /// <summary>The predicted sample for the request, or <c>null</c> when predictions are off or no station reaches the point.</summary>
    public ToolResult<SampleCoverageResult>? Sample(SampleCoverageRequest request)
    {
        if (_sources.TidePredictions is not { } catalogue)
            return null;

        var datasets = _catalog.Datasets;
        var mask = _sources.Get(datasets).WaterMask;
        var source = new TidePredictionWaterLevelSource(catalogue, new WaterReach(mask));
        var position = new GeoPosition(request.Latitude, request.Longitude);
        if (source.Locate(position) is not [var series, ..])
            return null;

        var info = series.Source;
        if (info.StationId is not { } id || catalogue.Find(id) is not { } station)
            return null;

        var sampleSource = new SampleSource(
            "prediction",
            new SampleSourceStation(
                id,
                info.StationName,
                "tide",
                info.ReferenceStationId is null ? "harmonic" : "subordinate",
                info.ReferenceStationId,
                info.DistanceMeters is { } d ? Math.Round(d) : null),
            info.VerticalDatum,
            TideToolText.Caveat,
            catalogue.Retrieved?.ToUniversalTime());

        var times = request.Times is TimeQuery.Instant instant ? null : request.Times;
        var requested = request.Times is TimeQuery.Instant i ? i.Value : request.Time;
        if (times is TimeQuery.Range or TimeQuery.Series)
        {
            var (from, to) = times.GetWindow();
            IReadOnlyList<DateTimeOffset> instants;
            if (times is TimeQuery.Series s)
            {
                if (s.EstimatedCount > MaxPoints)
                    return Invalid($"the series is {s.EstimatedCount} instants; a predicted series is at most {MaxPoints}");
                instants = s.Enumerate();
            }
            else
            {
                var count = (int)Math.Min(int.MaxValue, ((to - from).Ticks / RangeStep.Ticks) + 1);
                if (count > MaxPoints)
                    return Invalid($"the range is {count} half-hour steps; a predicted series is at most {MaxPoints}");
                instants = [.. Enumerable.Range(0, Math.Max(1, count)).Select(k => from + (RangeStep * k))];
            }

            var values = instants
                .Select(t => new TimedSampledValue(t.UtcDateTime, t, Value(series, station.Position, t.UtcDateTime, t)))
                .ToList();
            return ToolResult<SampleCoverageResult>.Ok(new SampleCoverageResult(
                new DatasetId(info.DatasetId),
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
            new DatasetId(info.DatasetId),
            request.Latitude,
            request.Longitude,
            Value(series, station.Position, at, requested),
            TimeStatus: requested is null ? null : "in_range",
            Source: sampleSource));
    }

    private static WaterLevelStationSample Value(IWaterLevelSeries series, GeoPosition stationPosition, DateTime utc, DateTimeOffset? requested)
    {
        var height = series.ValueAt(utc)?.HeightMeters ?? 0;
        var rise = (series.ValueAt(utc.AddMinutes(30))?.HeightMeters ?? height) - (series.ValueAt(utc.AddMinutes(-30))?.HeightMeters ?? height);
        var trend = Math.Abs(rise) < TrendThresholdMeters ? "steady" : rise > 0 ? "increasing" : "decreasing";
        return new WaterLevelStationSample(
            series.Source.StationId ?? string.Empty,
            Math.Round(series.Source.DistanceMeters ?? 0),
            Math.Round(height, 3),
            trend,
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            requested,
            stationPosition.Latitude,
            stationPosition.Longitude);
    }

    private static ToolResult<SampleCoverageResult> Invalid(string reason) =>
        ToolResult<SampleCoverageResult>.Err(new InvalidArgument("times", reason));
}
