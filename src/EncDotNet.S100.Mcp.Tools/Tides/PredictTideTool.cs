using System.ComponentModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Mcp.Tools.Tides;

/// <summary>What predict_tide returns.</summary>
public enum TidePredictionMode
{
    /// <summary>Heights at a regular step.</summary>
    Series,

    /// <summary>Highs and lows.</summary>
    Extremes,

    /// <summary>Both.</summary>
    Both,
}

/// <summary>A predict_tide request: a station (by id or name) or a point, and a time window.</summary>
/// <param name="Station">A station id or name; or null to use the nearest station to the point.</param>
/// <param name="Latitude">The point's latitude, when no station is named.</param>
/// <param name="Longitude">The point's longitude, when no station is named.</param>
/// <param name="RadiusMeters">How far from the point a station may be; <see cref="PredictTideTool.DefaultRadiusMeters"/> when null.</param>
/// <param name="From">The first time; now, rounded down to the step, when null.</param>
/// <param name="To">The last time; <see cref="PredictTideTool.DefaultWindow"/> after <paramref name="From"/> when null.</param>
/// <param name="Step">The time between series heights; <see cref="PredictTideTool.DefaultStep"/> when null.</param>
/// <param name="Mode">Series, highs and lows, or both.</param>
/// <param name="Datum">The datum's name (MLLW, LAT, MSL, …); MLLW when null.</param>
/// <param name="MaxPoints">The most series heights allowed.</param>
public sealed record PredictTideRequest(
    [property: Description("A station id or name; null to use the nearest station to the point.")] string? Station,
    [property: Description("Latitude of the point, decimal degrees WGS-84, when no station is named.")] double? Latitude,
    [property: Description("Longitude of the point, decimal degrees WGS-84, when no station is named.")] double? Longitude,
    [property: Description("How far from the point the station may be, in metres (default 10000).")] double? RadiusMeters,
    [property: Description("The first time (default: now, rounded down to the step).")] DateTimeOffset? From,
    [property: Description("The last time (default: 24 h after 'from'; at most 366 days).")] DateTimeOffset? To,
    [property: Description("The time between series heights (default 30 minutes).")] TimeSpan? Step,
    [property: Description("Series, highs and lows (extremes), or both.")] TidePredictionMode Mode,
    [property: Description("The datum: MLLW (default), MLW, MSL, MHW, MHHW, LAT or HAT.")] string? Datum,
    [property: Description("The most series heights allowed.")] int MaxPoints);

/// <summary>A station chosen for a prediction, with its predictor on the datum asked for.</summary>
/// <param name="Catalogue">The catalogue it comes from.</param>
/// <param name="Station">The station.</param>
/// <param name="Prediction">Its predictor and provenance.</param>
/// <param name="DistanceMeters">Its distance from the point asked about; null when it was named.</param>
/// <param name="FromUtc">The window's first time (UTC).</param>
/// <param name="ToUtc">The window's last time (UTC).</param>
/// <param name="Step">The series step.</param>
public sealed record ResolvedTidePrediction(
    TideStationCatalogue Catalogue,
    TideStationEntry Station,
    TideStationPrediction Prediction,
    double? DistanceMeters,
    DateTime FromUtc,
    DateTime ToUtc,
    TimeSpan Step);

/// <summary>
/// Predicts the tide at a station or the nearest station to a point
/// (MCP <c>predict_tide</c>, <c>s100 tides predict</c>).
/// </summary>
public sealed class PredictTideTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "predict_tide";

    /// <summary>How far a station may be from a point when no radius is given: 10 km, the reach of a station.</summary>
    public const double DefaultRadiusMeters = 10_000;

    /// <summary>The window when no end is given: 24 hours.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    /// <summary>The series step when none is given: 30 minutes.</summary>
    public static readonly TimeSpan DefaultStep = TimeSpan.FromMinutes(30);

    /// <summary>The longest window one call predicts: 366 days.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(366);

    /// <summary>The shortest step: one minute.</summary>
    public static readonly TimeSpan MinStep = TimeSpan.FromMinutes(1);

    private readonly ITideCatalogueSource _source;
    private readonly TimeProvider _time;

    /// <param name="source">The station catalogue.</param>
    /// <param name="time">The clock, for the default window.</param>
    public PredictTideTool(ITideCatalogueSource source, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);
        _source = source;
        _time = time;
    }

    /// <summary>Predicts.</summary>
    public async Task<ToolResult<PredictTideResult>> InvokeAsync(PredictTideRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        if (!resolved.TryGetValue(out var r))
        {
            resolved.TryGetError(out var error);
            return ToolResult<PredictTideResult>.Err(error!);
        }

        var predictor = r.Prediction.Predictor;
        IReadOnlyList<TideHeight>? series = null;
        if (request.Mode != TidePredictionMode.Extremes)
        {
            var count = (int)((r.ToUtc - r.FromUtc).Ticks / r.Step.Ticks) + 1;
            if (count > request.MaxPoints)
            {
                return ToolResult<PredictTideResult>.Err(new InvalidArgument(
                    "stepMinutes",
                    $"the window at this step is {count} heights, more than {request.MaxPoints}; use a longer step, a shorter window or highs and lows"));
            }

            var heights = new double[count];
            predictor.Predict(r.FromUtc, r.Step, heights);
            var list = new TideHeight[count];
            for (var i = 0; i < count; i++)
                list[i] = new TideHeight(new DateTimeOffset(r.FromUtc + (r.Step * i), TimeSpan.Zero), Math.Round(heights[i], 3));
            series = list;
        }

        IReadOnlyList<TideExtremeDto>? extremes = request.Mode == TidePredictionMode.Series
            ? null
            : [.. predictor.FindExtremes(r.FromUtc, r.ToUtc).Select(e => new TideExtremeDto(
                new DateTimeOffset(RoundToMinute(e.Time), TimeSpan.Zero),
                Math.Round(e.HeightMeters, 3),
                e.Kind == TideExtremeKind.High ? "high" : "low"))];

        var source = r.Prediction.Source;
        return ToolResult<PredictTideResult>.Ok(new PredictTideResult(
            new TidePredictionStation(
                source.StationId,
                source.StationName,
                "tide",
                TideToolText.Kind(source.Kind),
                source.ReferenceStationId,
                source.ReferenceStationName,
                new TidePosition(r.Station.Position.Latitude, r.Station.Position.Longitude),
                r.DistanceMeters is { } d ? Math.Round(d) : null),
            "prediction",
            source.DatumName,
            source.VerticalDatum is { } datum ? (int)datum : null,
            "m",
            new DateTimeOffset(r.FromUtc, TimeSpan.Zero),
            new DateTimeOffset(r.ToUtc, TimeSpan.Zero),
            series is null ? null : (int)r.Step.TotalSeconds,
            series,
            extremes,
            source.ReferenceDescription,
            TideToolText.Caveat,
            source.CatalogueDate?.ToUniversalTime(),
            source.Provider));
    }

    /// <summary>
    /// Finds the station and its predictor, and settles the time window,
    /// without predicting: the part <c>s100 tides predict --format s104</c>
    /// shares.
    /// </summary>
    public async Task<ToolResult<ResolvedTidePrediction>> ResolveAsync(PredictTideRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var datumName = string.IsNullOrWhiteSpace(request.Datum) ? TideDatumNames.MeanLowerLowWater : request.Datum.Trim().ToUpperInvariant();
        if (TideDatumNames.ToVerticalDatum(datumName) is not { } datum)
            return Invalid("datum", $"'{request.Datum}' isn't one of {string.Join(", ", TideDatumNames.All)}");

        var step = request.Step ?? DefaultStep;
        if (step < MinStep)
            return Invalid("stepMinutes", "must be at least one minute");
        if (step.Ticks % TimeSpan.TicksPerSecond != 0)
            return Invalid("stepMinutes", "must be a whole number of seconds");

        var from = request.From?.UtcDateTime ?? FloorTo(_time.GetUtcNow().UtcDateTime, step);
        var to = request.To?.UtcDateTime ?? from + DefaultWindow;
        if (to < from)
            return Invalid("to", "is before 'from'");
        if (to - from > MaxWindow)
            return Invalid("to", "the window can be at most 366 days");
        from = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        to = DateTime.SpecifyKind(to, DateTimeKind.Utc);

        var named = !string.IsNullOrWhiteSpace(request.Station);
        if (!named && (request.Latitude is null || request.Longitude is null))
            return Invalid("station", "give a station id or name, or a point (latitude, longitude)");
        if (!named && (request.Latitude is < -90 or > 90 || double.IsNaN(request.Latitude!.Value)))
            return Invalid("latitude", "must be between -90 and 90");
        if (!named && (request.Longitude is < -180 or > 180 || double.IsNaN(request.Longitude!.Value)))
            return Invalid("longitude", "must be between -180 and 180");
        var radius = request.RadiusMeters ?? DefaultRadiusMeters;
        if (double.IsNaN(radius) || radius <= 0)
            return Invalid("radiusMeters", "must be more than zero");

        var catalogue = await _source.GetAsync(cancellationToken).ConfigureAwait(false);
        if (catalogue is null)
            return ToolResult<ResolvedTidePrediction>.Err(new TideCatalogueNotAvailable(_source.MissingHint));

        if (named)
        {
            var station = FindStation(catalogue, request.Station!.Trim(), out var candidates);
            if (station is null)
                return ToolResult<ResolvedTidePrediction>.Err(new TideStationNotFound(request.Station!.Trim(), candidates));

            if (catalogue.TryCreatePrediction(station.Id, datum) is not { } prediction)
            {
                return ToolResult<ResolvedTidePrediction>.Err(new TideDatumNotAvailable(
                    station.Id,
                    datumName,
                    [.. catalogue.GetAvailableDatums(station.Id).Select(d => TideDatumNames.FromVerticalDatum(d)!)]));
            }

            return ToolResult<ResolvedTidePrediction>.Ok(new ResolvedTidePrediction(catalogue, station, prediction, null, from, to, step));
        }

        var point = new GeoPosition(request.Latitude!.Value, request.Longitude!.Value);
        foreach (var match in catalogue.FindNearest(point, int.MaxValue, radius))
        {
            if (catalogue.TryCreatePrediction(match.Station.Id, datum) is { } prediction)
                return ToolResult<ResolvedTidePrediction>.Ok(new ResolvedTidePrediction(catalogue, match.Station, prediction, match.DistanceMeters, from, to, step));
        }

        return ToolResult<ResolvedTidePrediction>.Err(new NoTideStationNear(point.Latitude, point.Longitude, radius, datumName));
    }

    /// <summary>
    /// Finds a station by id, then by whole name, then by a unique part of its
    /// name, ignoring case.
    /// </summary>
    internal static TideStationEntry? FindStation(TideStationCatalogue catalogue, string text, out IReadOnlyList<string> candidates)
    {
        candidates = [];
        if (catalogue.Find(text) is { } byId)
            return byId;

        var exact = catalogue.Stations.Where(s => string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
            return exact[0];

        var partial = exact.Count > 1
            ? exact
            : [.. catalogue.Stations.Where(s => s.Name.Contains(text, StringComparison.OrdinalIgnoreCase))];
        if (partial.Count == 1)
            return partial[0];

        candidates = [.. partial.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Take(10).Select(s => $"{s.Id} {s.Name}")];
        return null;
    }

    private static DateTime RoundToMinute(DateTime utc) =>
        new((utc.Ticks + (TimeSpan.TicksPerMinute / 2)) / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    private static DateTime FloorTo(DateTime utc, TimeSpan step) =>
        new(utc.Ticks - (utc.Ticks % step.Ticks), DateTimeKind.Utc);

    private static ToolResult<ResolvedTidePrediction> Invalid(string parameter, string reason) =>
        ToolResult<ResolvedTidePrediction>.Err(new InvalidArgument(parameter, reason));
}
