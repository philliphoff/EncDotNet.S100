using System.Collections.Concurrent;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// Currents from harmonic current-station predictions, for the timing check
/// (#1561): at a point, the current station nearest by water within the
/// reach; by id or name, any station in the catalogue.
/// </summary>
/// <remarks>
/// <para>
/// A prediction is the astronomical tidal current only: wind and river flow
/// aren't in it, and the strongest stream in a pass can be away from the
/// station. Of the depth bins at one place the shallowest is used, as the
/// current nearest the surface, as the <c>sample</c> tool's fallback does.
/// </para>
/// <para>Safe to call from several threads at once.</para>
/// </remarks>
public sealed class TidePredictionCurrentSource : IRouteCurrentSource
{
    // Straight-line candidates looked at for the nearest by water: NOAA has
    // several depth bins at one place.
    private const int Candidates = 32;

    private readonly TideStationCatalogue _catalogue;
    private readonly WaterReach _reach;
    private readonly ConcurrentDictionary<string, Lazy<Station?>> _stations = new(StringComparer.Ordinal);

    /// <summary>Creates the source.</summary>
    /// <param name="catalogue">The stations.</param>
    /// <param name="reach">How far a station reaches, and through what water.</param>
    public TidePredictionCurrentSource(TideStationCatalogue catalogue, WaterReach reach)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(reach);
        _catalogue = catalogue;
        _reach = reach;
    }

    /// <inheritdoc/>
    public IRouteCurrentSeries? Locate(GeoPosition position)
    {
        (CurrentStationEntry Entry, double Distance)? best = null;
        foreach (var match in _catalogue.FindNearestCurrentStations(position, Candidates, _reach.ReachMeters))
        {
            if (best is { } b && match.DistanceMeters > b.Distance + 1)
            {
                // The distance by water is never shorter than the straight one.
                break;
            }

            if (_reach.DistanceMeters(match.Station.Position, position) is not { } distance
                || (best is { } current && !Better(match.Station, distance, current.Entry, current.Distance))
                || StationFor(match.Station.Id) is null)
            {
                continue;
            }

            best = (match.Station, distance);
        }

        return best is { } found ? StationFor(found.Entry.Id)!.At(found.Distance) : null;
    }

    /// <inheritdoc/>
    public IRouteCurrentStation? FindStation(string idOrName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrName);
        var text = idOrName.Trim();
        var entry = _catalogue.FindCurrentStation(text);
        if (entry is null)
        {
            var exact = _catalogue.CurrentStations.Where(s => string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase)).ToList();
            var matches = exact.Count > 0
                ? exact
                : [.. _catalogue.CurrentStations.Where(s => s.Name.Contains(text, StringComparison.OrdinalIgnoreCase))];

            // Several bins at one place share a name: the shallowest stands for them.
            var places = matches.GroupBy(s => (Math.Round(s.Position.Latitude, 4), Math.Round(s.Position.Longitude, 4))).ToList();
            if (places.Count != 1)
            {
                return null;
            }

            entry = places[0].OrderBy(s => s.DepthMeters ?? double.MaxValue).First();
        }

        return StationFor(entry.Id);
    }

    private Station? StationFor(string id) =>
        _stations.GetOrAdd(id, key => new Lazy<Station?>(
            () => _catalogue.FindCurrentStation(key) is { } entry && _catalogue.TryCreateCurrentPrediction(key) is { } prediction
                ? new Station(entry, prediction, null)
                : null,
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    // Nearer wins; at one place (within a metre), the shallower bin.
    private static bool Better(CurrentStationEntry station, double distance, CurrentStationEntry best, double bestDistance) =>
        Math.Abs(distance - bestDistance) <= 1
            ? (station.DepthMeters ?? double.MaxValue) < (best.DepthMeters ?? double.MaxValue)
            : distance < bestDistance;

    private sealed class Station(CurrentStationEntry entry, CurrentStationPrediction prediction, double? distance) : IRouteCurrentStation
    {
        public RouteCurrentSourceInfo Source { get; } =
            new(entry.Id, entry.Name, RouteCurrentOrigin.Prediction, distance);

        public GeoPosition Position => entry.Position;

        public Station At(double distanceMeters) => new(entry, prediction, Math.Round(distanceMeters));

        public RouteCurrent? At(DateTimeOffset time)
        {
            var current = prediction.Predictor.CurrentAt(time.UtcDateTime);
            return new RouteCurrent(current.SpeedKnots, current.DirectionDegrees % 360);
        }

        public IReadOnlyList<DateTimeOffset> SlacksBetween(DateTimeOffset from, DateTimeOffset to) =>
            [.. prediction.Predictor.FindEvents(from.UtcDateTime, to.UtcDateTime)
                .Where(e => e.IsSlack)
                .Select(e => new DateTimeOffset(DateTime.SpecifyKind(e.Time, DateTimeKind.Utc)))];
    }
}
