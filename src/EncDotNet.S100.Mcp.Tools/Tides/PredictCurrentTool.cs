using System.ComponentModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Mcp.Tools.Tides;

/// <summary>What predict_current returns.</summary>
public enum CurrentPredictionMode
{
    /// <summary>Speeds and directions at a regular step.</summary>
    Series,

    /// <summary>Slacks and maximum and minimum floods and ebbs.</summary>
    Events,

    /// <summary>Both.</summary>
    Both,
}

/// <summary>A predict_current request: a station (by id or name) or a point, and a time window.</summary>
/// <param name="Station">A station id or name; or null to use the nearest station to the point.</param>
/// <param name="Latitude">The point's latitude, when no station is named.</param>
/// <param name="Longitude">The point's longitude, when no station is named.</param>
/// <param name="RadiusMeters">How far from the point a station may be; <see cref="PredictTideTool.DefaultRadiusMeters"/> when null.</param>
/// <param name="From">The first time; now, rounded down to the step, when null.</param>
/// <param name="To">The last time; <see cref="PredictTideTool.DefaultWindow"/> after <paramref name="From"/> when null.</param>
/// <param name="Step">The time between series values; <see cref="PredictTideTool.DefaultStep"/> when null.</param>
/// <param name="Mode">Series, slacks and maxima, or both.</param>
/// <param name="MaxPoints">The most series values allowed.</param>
public sealed record PredictCurrentRequest(
    [property: Description("A current station id (such as PUG1701_33) or name; null to use the nearest station to the point.")] string? Station,
    [property: Description("Latitude of the point, decimal degrees WGS-84, when no station is named.")] double? Latitude,
    [property: Description("Longitude of the point, decimal degrees WGS-84, when no station is named.")] double? Longitude,
    [property: Description("How far from the point the station may be, in metres (default 10000).")] double? RadiusMeters,
    [property: Description("The first time (default: now, rounded down to the step).")] DateTimeOffset? From,
    [property: Description("The last time (default: 24 h after 'from'; at most 366 days).")] DateTimeOffset? To,
    [property: Description("The time between series values (default 30 minutes).")] TimeSpan? Step,
    [property: Description("Series, slacks and maxima (events), or both.")] CurrentPredictionMode Mode,
    [property: Description("The most series values allowed.")] int MaxPoints);

/// <summary>A current station chosen for a prediction, with its predictor.</summary>
/// <param name="Catalogue">The catalogue it comes from.</param>
/// <param name="Station">The station.</param>
/// <param name="Prediction">Its predictor and provenance.</param>
/// <param name="DistanceMeters">Its distance from the point asked about; null when it was named.</param>
/// <param name="FromUtc">The window's first time (UTC).</param>
/// <param name="ToUtc">The window's last time (UTC).</param>
/// <param name="Step">The series step.</param>
public sealed record ResolvedCurrentPrediction(
    TideStationCatalogue Catalogue,
    CurrentStationEntry Station,
    CurrentStationPrediction Prediction,
    double? DistanceMeters,
    DateTime FromUtc,
    DateTime ToUtc,
    TimeSpan Step);

/// <summary>
/// Predicts the tidal current at a station or the nearest station to a point
/// (MCP <c>predict_current</c>, <c>s100 tides currents predict</c>).
/// </summary>
public sealed class PredictCurrentTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "predict_current";

    private readonly ITideCatalogueSource _source;
    private readonly TimeProvider _time;

    /// <param name="source">The station catalogue.</param>
    /// <param name="time">The clock, for the default window.</param>
    public PredictCurrentTool(ITideCatalogueSource source, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);
        _source = source;
        _time = time;
    }

    /// <summary>Predicts.</summary>
    public async Task<ToolResult<PredictCurrentResult>> InvokeAsync(PredictCurrentRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        if (!resolved.TryGetValue(out var r))
        {
            resolved.TryGetError(out var error);
            return ToolResult<PredictCurrentResult>.Err(error!);
        }

        var predictor = r.Prediction.Predictor;
        IReadOnlyList<CurrentValue>? series = null;
        if (request.Mode != CurrentPredictionMode.Events)
        {
            var count = (int)((r.ToUtc - r.FromUtc).Ticks / r.Step.Ticks) + 1;
            if (count > request.MaxPoints)
            {
                return ToolResult<PredictCurrentResult>.Err(new InvalidArgument(
                    "stepMinutes",
                    $"the window at this step is {count} values, more than {request.MaxPoints}; use a longer step, a shorter window or slacks and maxima"));
            }

            var list = new CurrentValue[count];
            for (var i = 0; i < count; i++)
            {
                var time = r.FromUtc + (r.Step * i);
                list[i] = Value(predictor.CurrentAt(time), new DateTimeOffset(time, TimeSpan.Zero));
            }

            series = list;
        }

        IReadOnlyList<CurrentEventDto>? events = request.Mode == CurrentPredictionMode.Series
            ? null
            : [.. predictor.FindEvents(r.FromUtc, r.ToUtc).Select(e => Event(predictor, e))];

        var source = r.Prediction.Source;
        return ToolResult<PredictCurrentResult>.Ok(new PredictCurrentResult(
            new CurrentPredictionStation(
                source.StationId,
                source.StationName,
                "current",
                TideToolText.Kind(source.Kind),
                source.ReferenceStationId,
                source.ReferenceStationName,
                new TidePosition(r.Station.Position.Latitude, r.Station.Position.Longitude),
                r.DistanceMeters is { } d ? Math.Round(d) : null,
                source.DepthMeters,
                predictor.FloodDirectionDegrees,
                predictor.EbbDirectionDegrees,
                source.IsRotary),
            "prediction",
            "kn",
            new DateTimeOffset(r.FromUtc, TimeSpan.Zero),
            new DateTimeOffset(r.ToUtc, TimeSpan.Zero),
            series is null ? null : (int)r.Step.TotalSeconds,
            series,
            events,
            source.ReferenceDescription,
            TideToolText.CurrentCaveat,
            source.CatalogueDate?.ToUniversalTime(),
            source.Provider));
    }

    /// <summary>
    /// Finds the station and its predictor, and settles the time window,
    /// without predicting: the part <c>s100 tides currents predict --format s111</c>
    /// shares.
    /// </summary>
    public async Task<ToolResult<ResolvedCurrentPrediction>> ResolveAsync(PredictCurrentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var step = request.Step ?? PredictTideTool.DefaultStep;
        if (step < PredictTideTool.MinStep)
            return Invalid("stepMinutes", "must be at least one minute");
        if (step.Ticks % TimeSpan.TicksPerSecond != 0)
            return Invalid("stepMinutes", "must be a whole number of seconds");

        var from = request.From?.UtcDateTime ?? FloorTo(_time.GetUtcNow().UtcDateTime, step);
        var to = request.To?.UtcDateTime ?? from + PredictTideTool.DefaultWindow;
        if (to < from)
            return Invalid("to", "is before 'from'");
        if (to - from > PredictTideTool.MaxWindow)
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
        var radius = request.RadiusMeters ?? PredictTideTool.DefaultRadiusMeters;
        if (double.IsNaN(radius) || radius <= 0)
            return Invalid("radiusMeters", "must be more than zero");

        var catalogue = await _source.GetAsync(cancellationToken).ConfigureAwait(false);
        if (catalogue is null)
            return ToolResult<ResolvedCurrentPrediction>.Err(new TideCatalogueNotAvailable(_source.MissingHint));

        if (named)
        {
            var station = FindStation(catalogue, request.Station!.Trim(), out var candidates);
            if (station is null)
                return ToolResult<ResolvedCurrentPrediction>.Err(new CurrentStationNotFound(request.Station!.Trim(), candidates));

            if (catalogue.TryCreateCurrentPrediction(station.Id) is not { } prediction)
                return ToolResult<ResolvedCurrentPrediction>.Err(new CurrentStationNotPredictable(station.Id));

            return ToolResult<ResolvedCurrentPrediction>.Ok(new ResolvedCurrentPrediction(catalogue, station, prediction, null, from, to, step));
        }

        var point = new GeoPosition(request.Latitude!.Value, request.Longitude!.Value);
        foreach (var match in Nearest(catalogue, point, radius))
        {
            if (catalogue.TryCreateCurrentPrediction(match.Station.Id) is { } prediction)
                return ToolResult<ResolvedCurrentPrediction>.Ok(new ResolvedCurrentPrediction(catalogue, match.Station, prediction, match.DistanceMeters, from, to, step));
        }

        return ToolResult<ResolvedCurrentPrediction>.Err(new NoCurrentStationNear(point.Latitude, point.Longitude, radius));
    }

    /// <summary>
    /// The current stations within a radius of a point, nearest first; of the
    /// depth bins at one place, the shallowest first, as the current nearest
    /// the surface.
    /// </summary>
    internal static IEnumerable<CurrentStationMatch> Nearest(TideStationCatalogue catalogue, GeoPosition point, double radiusMeters) =>
        catalogue.FindNearestCurrentStations(point, int.MaxValue, radiusMeters)
            .OrderBy(m => Math.Round(m.DistanceMeters))
            .ThenBy(m => m.Station.DepthMeters ?? double.MaxValue);

    /// <summary>
    /// Finds a current station by id, then by NOAA station id without its
    /// depth bin, then by whole name, then by a unique part of its name,
    /// ignoring case. Where the matches are depth bins of one station, the
    /// shallowest is taken.
    /// </summary>
    internal static CurrentStationEntry? FindStation(TideStationCatalogue catalogue, string text, out IReadOnlyList<string> candidates)
    {
        candidates = [];
        if (catalogue.FindCurrentStation(text) is { } byId)
            return byId;

        var bins = catalogue.CurrentStations
            .Where(s => string.Equals(BaseId(s.Id), text, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (bins.Count > 0)
            return Shallowest(bins);

        var exact = catalogue.CurrentStations.Where(s => string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase)).ToList();
        var matches = exact.Count > 0
            ? exact
            : [.. catalogue.CurrentStations.Where(s => s.Name.Contains(text, StringComparison.OrdinalIgnoreCase))];
        if (matches.Count > 0 && matches.Select(s => BaseId(s.Id)).Distinct(StringComparer.Ordinal).Count() == 1)
            return Shallowest(matches);

        candidates = [.. matches.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id, StringComparer.Ordinal).Take(10).Select(s => $"{s.Id} {s.Name}")];
        return null;
    }

    private static string BaseId(string id) => id.IndexOf('_', StringComparison.Ordinal) is var i and > 0 ? id[..i] : id;

    private static CurrentStationEntry Shallowest(List<CurrentStationEntry> stations) =>
        stations.OrderBy(s => s.DepthMeters ?? double.MaxValue).ThenBy(s => s.Id, StringComparer.Ordinal).First();

    /// <summary>A predicted current as the tools return it.</summary>
    internal static CurrentValue Value(TidalCurrent current, DateTimeOffset time) => new(
        time,
        Math.Round(current.SpeedKnots, 2),
        Math.Round(current.DirectionDegrees, 1) % 360,
        current.IsFlood ? "flood" : "ebb");

    private static CurrentEventDto Event(ICurrentPredictor predictor, CurrentEvent e)
    {
        double? direction = e.IsSlack ? null : Math.Round(predictor.CurrentAt(e.Time).DirectionDegrees, 1) % 360;
        return new CurrentEventDto(
            new DateTimeOffset(RoundToMinute(e.Time), TimeSpan.Zero),
            TideToolText.EventKind(e.Kind),
            e.IsSlack ? 0 : Math.Round(e.SpeedMetersPerSecond / TidalCurrent.MetersPerSecondPerKnot, 2),
            direction);
    }

    private static DateTime RoundToMinute(DateTime utc) =>
        new((utc.Ticks + (TimeSpan.TicksPerMinute / 2)) / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    private static DateTime FloorTo(DateTime utc, TimeSpan step) =>
        new(utc.Ticks - (utc.Ticks % step.Ticks), DateTimeKind.Utc);

    private static ToolResult<ResolvedCurrentPrediction> Invalid(string parameter, string reason) =>
        ToolResult<ResolvedCurrentPrediction>.Err(new InvalidArgument(parameter, reason));
}
