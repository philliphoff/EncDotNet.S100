using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Tides.Stations;

/// <summary>
/// A set of tide stations, harmonic stations with their constituents and
/// datums and subordinate stations with their reference station and
/// offsets, and of tidal current stations of both kinds. Finds stations by
/// identifier, near a point or in a box, and creates a tide station's
/// predictor on a chosen datum or a current station's predictor.
/// </summary>
/// <remarks>
/// <para>
/// A catalogue comes from <see cref="Noaa.NoaaTideStationClient.DownloadCatalogueAsync"/>,
/// or from a file in the station file format
/// (<see cref="TideStationCatalogueFile"/>), which is both the offline
/// cache and the way to bring your own stations.
/// </para>
/// <para>
/// A catalogue is immutable and safe to use from several threads at once.
/// </para>
/// </remarks>
public sealed class TideStationCatalogue
{
    /// <summary>
    /// How old a catalogue may get before it's taken as stale when there's
    /// no network to check for newer data with: 12 months. With a network,
    /// newer data, not age, makes a catalogue stale (see
    /// <see cref="Noaa.NoaaTideStationClient.CheckForUpdatesAsync"/>).
    /// </summary>
    public static readonly TimeSpan OfflineStaleAfter = TimeSpan.FromDays(365);

    /// <summary>One degree of latitude, in metres, at its shortest (at the equator), to prefilter a distance query.</summary>
    private const double MinimumMetersPerDegreeLatitude = 110_574;

    private readonly Dictionary<string, TideStationEntry> _byId;
    private readonly Dictionary<string, CurrentStationEntry> _currentsById;

    /// <summary>Creates a catalogue of tide stations only.</summary>
    /// <param name="stations">The stations. Identifiers must be unique.</param>
    /// <param name="provider">Who provides the stations, such as <c>NOAA CO-OPS</c>, or <c>null</c>.</param>
    /// <param name="retrieved">When the stations were retrieved from the provider, or <c>null</c> when not known.</param>
    /// <exception cref="ArgumentException">
    /// An identifier is repeated, a harmonic station has an unknown or
    /// repeated constituent, or a subordinate station has no reference
    /// station or offsets.
    /// </exception>
    public TideStationCatalogue(IReadOnlyList<TideStationEntry> stations, string? provider, DateTimeOffset? retrieved)
        : this(stations, [], provider, retrieved)
    {
    }

    /// <summary>Creates a catalogue of tide and tidal current stations.</summary>
    /// <param name="stations">The tide stations. Identifiers must be unique.</param>
    /// <param name="currentStations">The current stations. Identifiers must be unique among them.</param>
    /// <param name="provider">Who provides the stations, such as <c>NOAA CO-OPS</c>, or <c>null</c>.</param>
    /// <param name="retrieved">When the stations were retrieved from the provider, or <c>null</c> when not known.</param>
    /// <exception cref="ArgumentException">
    /// An identifier is repeated, a harmonic station has an unknown or
    /// repeated constituent or a non-finite value, or a subordinate station
    /// has no reference station or offsets.
    /// </exception>
    public TideStationCatalogue(
        IReadOnlyList<TideStationEntry> stations,
        IReadOnlyList<CurrentStationEntry> currentStations,
        string? provider,
        DateTimeOffset? retrieved)
    {
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(currentStations);
        _byId = new Dictionary<string, TideStationEntry>(stations.Count, StringComparer.Ordinal);
        foreach (var station in stations)
        {
            ArgumentNullException.ThrowIfNull(station, nameof(stations));
            Validate(station);
            if (!_byId.TryAdd(station.Id, station))
            {
                throw new ArgumentException($"Station '{station.Id}' appears more than once.", nameof(stations));
            }
        }

        _currentsById = new Dictionary<string, CurrentStationEntry>(currentStations.Count, StringComparer.Ordinal);
        foreach (var station in currentStations)
        {
            ArgumentNullException.ThrowIfNull(station, nameof(currentStations));
            Validate(station);
            if (!_currentsById.TryAdd(station.Id, station))
            {
                throw new ArgumentException($"Current station '{station.Id}' appears more than once.", nameof(currentStations));
            }
        }

        Stations = [.. stations];
        CurrentStations = [.. currentStations];
        Provider = provider;
        Retrieved = retrieved;
        DatumEpoch = stations
            .Select(s => s.Datums?.Epoch)
            .Where(e => !string.IsNullOrEmpty(e))
            .GroupBy(e => e, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    /// <summary>The stations, in the order they were given.</summary>
    public IReadOnlyList<TideStationEntry> Stations { get; }

    /// <summary>The tidal current stations, in the order they were given.</summary>
    public IReadOnlyList<CurrentStationEntry> CurrentStations { get; }

    /// <summary>Who provides the stations, such as <c>NOAA CO-OPS</c>, or <c>null</c>.</summary>
    public string? Provider { get; }

    /// <summary>When the stations were retrieved from the provider, or <c>null</c> when not known.</summary>
    public DateTimeOffset? Retrieved { get; }

    /// <summary>
    /// The tidal datum epoch most of the stations' datums are on, such as
    /// <c>1983-2001</c>, or <c>null</c> when no station gives one.
    /// </summary>
    public string? DatumEpoch { get; }

    /// <summary>Finds a station by its identifier.</summary>
    /// <param name="id">The station identifier.</param>
    /// <returns>The station, or <c>null</c> when the catalogue doesn't have it.</returns>
    public TideStationEntry? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.GetValueOrDefault(id);
    }

    /// <summary>
    /// Whether the catalogue is older than an age: the offline fallback for
    /// staleness (see <see cref="OfflineStaleAfter"/>).
    /// </summary>
    /// <param name="age">The age.</param>
    /// <param name="now">The time now.</param>
    /// <returns><c>true</c> when the catalogue was retrieved more than <paramref name="age"/> before <paramref name="now"/>, or its date isn't known.</returns>
    public bool IsOlderThan(TimeSpan age, DateTimeOffset now) => Retrieved is not { } retrieved || now - retrieved > age;

    /// <summary>Finds the stations nearest a point, nearest first.</summary>
    /// <param name="position">The point.</param>
    /// <param name="maxCount">The most stations to return.</param>
    /// <param name="maxDistanceMeters">The furthest a station may be, in metres; infinite for no limit.</param>
    /// <returns>The stations, nearest first, with their geodesic distances.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCount"/> is negative or <paramref name="maxDistanceMeters"/> is negative or not a number.</exception>
    public IReadOnlyList<TideStationMatch> FindNearest(
        GeoPosition position, int maxCount, double maxDistanceMeters = double.PositiveInfinity)
    {
        var matches = Nearest(Stations, static s => s.Position, static s => s.Id, position, maxCount, maxDistanceMeters);
        return [.. matches.Select(m => new TideStationMatch(m.Station, m.DistanceMeters))];
    }

    /// <summary>Finds the stations in a box, nearest a point first.</summary>
    /// <param name="box">
    /// The box. A box whose west edge is east of its east edge crosses the
    /// antimeridian.
    /// </param>
    /// <param name="rankFrom">The point to rank by distance from; <c>null</c> for the box's centre.</param>
    /// <returns>The stations in the box, nearest first, with their geodesic distances from the point.</returns>
    public IReadOnlyList<TideStationMatch> FindWithin(BoundingBox box, GeoPosition? rankFrom = null)
    {
        var matches = Within(Stations, static s => s.Position, static s => s.Id, box, rankFrom);
        return [.. matches.Select(m => new TideStationMatch(m.Station, m.DistanceMeters))];
    }

    /// <summary>Finds a current station by its identifier.</summary>
    /// <param name="id">The station identifier, such as <c>PUG1701_33</c>.</param>
    /// <returns>The station, or <c>null</c> when the catalogue doesn't have it.</returns>
    public CurrentStationEntry? FindCurrentStation(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _currentsById.GetValueOrDefault(id);
    }

    /// <summary>Finds the current stations nearest a point, nearest first.</summary>
    /// <param name="position">The point.</param>
    /// <param name="maxCount">The most stations to return.</param>
    /// <param name="maxDistanceMeters">The furthest a station may be, in metres; infinite for no limit.</param>
    /// <returns>The stations, nearest first, with their geodesic distances.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCount"/> is negative or <paramref name="maxDistanceMeters"/> is negative or not a number.</exception>
    public IReadOnlyList<CurrentStationMatch> FindNearestCurrentStations(
        GeoPosition position, int maxCount, double maxDistanceMeters = double.PositiveInfinity)
    {
        var matches = Nearest(CurrentStations, static s => s.Position, static s => s.Id, position, maxCount, maxDistanceMeters);
        return [.. matches.Select(m => new CurrentStationMatch(m.Station, m.DistanceMeters))];
    }

    /// <summary>Finds the current stations in a box, nearest a point first.</summary>
    /// <param name="box">
    /// The box. A box whose west edge is east of its east edge crosses the
    /// antimeridian.
    /// </param>
    /// <param name="rankFrom">The point to rank by distance from; <c>null</c> for the box's centre.</param>
    /// <returns>The stations in the box, nearest first, with their geodesic distances from the point.</returns>
    public IReadOnlyList<CurrentStationMatch> FindCurrentStationsWithin(BoundingBox box, GeoPosition? rankFrom = null)
    {
        var matches = Within(CurrentStations, static s => s.Position, static s => s.Id, box, rankFrom);
        return [.. matches.Select(m => new CurrentStationMatch(m.Station, m.DistanceMeters))];
    }

    /// <summary>
    /// Creates a current station's predictor, with where its predictions
    /// come from.
    /// </summary>
    /// <remarks>
    /// A harmonic station is predicted from its constituents. A subordinate
    /// station is predicted from its reference station, which must be a
    /// harmonic current station in the catalogue.
    /// </remarks>
    /// <param name="stationId">The current station identifier, such as <c>PUG1701_33</c>.</param>
    /// <returns>
    /// The prediction, or <c>null</c> when the catalogue doesn't have the
    /// station, a harmonic station has no constituents, or a subordinate
    /// station's reference station isn't a harmonic station with
    /// constituents.
    /// </returns>
    public CurrentStationPrediction? TryCreateCurrentPrediction(string stationId)
    {
        ArgumentNullException.ThrowIfNull(stationId);
        if (FindCurrentStation(stationId) is not { } station)
        {
            return null;
        }

        if (station.Kind == TideStationKind.Harmonic)
        {
            return TryCreateHarmonicPredictor(station) is { } predictor
                ? new CurrentStationPrediction(predictor, SourceFor(station, null))
                : null;
        }

        if (station.ReferenceStationId is not { } referenceId
            || station.Offsets is not { } offsets
            || FindCurrentStation(referenceId) is not { Kind: TideStationKind.Harmonic } reference
            || TryCreateHarmonicPredictor(reference) is not { } referencePredictor)
        {
            return null;
        }

        var subordinate = new SubordinateCurrentPredictor(
            referencePredictor, offsets, station.FloodDirectionDegrees, station.EbbDirectionDegrees);
        return new CurrentStationPrediction(subordinate, SourceFor(station, reference));
    }

    /// <summary>
    /// Creates a station's predictor on a datum, with where its predictions
    /// come from.
    /// </summary>
    /// <remarks>
    /// A harmonic station is predicted from its constituents on any datum
    /// its <see cref="TideStationEntry.Datums"/> give, and always on mean sea
    /// level. A subordinate station is predicted only on its
    /// <see cref="TideStationEntry.OffsetsDatum"/> (MLLW for NOAA), from its
    /// reference station on that datum.
    /// </remarks>
    /// <param name="stationId">The station identifier.</param>
    /// <param name="datum">The vertical datum to give heights on, such as <see cref="VerticalDatum.MeanLowerLowWater"/>.</param>
    /// <returns>
    /// The prediction, or <c>null</c> when the catalogue doesn't have the
    /// station, the station doesn't give the datum, a harmonic station has
    /// no constituents, or a subordinate station's reference station can't
    /// be predicted on the datum.
    /// </returns>
    public TideStationPrediction? TryCreatePrediction(string stationId, VerticalDatum datum)
    {
        ArgumentNullException.ThrowIfNull(stationId);
        if (Find(stationId) is not { } station || TideDatumNames.FromVerticalDatum(datum) is not { } datumName)
        {
            return null;
        }

        if (station.Kind == TideStationKind.Harmonic)
        {
            var predictor = TryCreateHarmonicPredictor(station, datumName);
            return predictor is null ? null : new TideStationPrediction(predictor, SourceFor(station, null, predictor.DatumName, datum));
        }

        if (!string.Equals(station.OffsetsDatum, datumName, StringComparison.OrdinalIgnoreCase)
            || station.ReferenceStationId is not { } referenceId
            || station.Offsets is not { } offsets
            || Find(referenceId) is not { Kind: TideStationKind.Harmonic } reference
            || TryCreateHarmonicPredictor(reference, datumName) is not { } referencePredictor)
        {
            return null;
        }

        var subordinate = new SubordinateTidePredictor(referencePredictor, offsets);
        return new TideStationPrediction(subordinate, SourceFor(station, reference, subordinate.DatumName, datum));
    }

    /// <summary>The datums a station can be predicted on, as S-100 vertical datums.</summary>
    /// <param name="stationId">The station identifier.</param>
    /// <returns>The datums, or an empty list when the catalogue doesn't have the station.</returns>
    public IReadOnlyList<VerticalDatum> GetAvailableDatums(string stationId)
    {
        ArgumentNullException.ThrowIfNull(stationId);
        var result = new List<VerticalDatum>();
        foreach (var name in TideDatumNames.All)
        {
            if (TideDatumNames.ToVerticalDatum(name) is { } datum && TryCreatePrediction(stationId, datum) is not null)
            {
                result.Add(datum);
            }
        }

        return result;
    }

    private static TidePredictor? TryCreateHarmonicPredictor(TideStationEntry station, string datumName)
    {
        var datum = string.Equals(datumName, TideDatumNames.MeanSeaLevel, StringComparison.OrdinalIgnoreCase)
            ? TideDatum.MeanSeaLevel
            : station.Datums?.TryGetTideDatum(datumName);
        return datum is null || station.Constituents.Count == 0
            ? null
            : new TidePredictor(new TideStation(station.Id, station.Name, station.Constituents, datum));
    }

    private static CurrentPredictor? TryCreateHarmonicPredictor(CurrentStationEntry station) =>
        station.Constituents.Count == 0
            ? null
            : new CurrentPredictor(ToCurrentStation(station));

    private static CurrentStation ToCurrentStation(CurrentStationEntry station) => new(
        station.Id,
        station.Name,
        station.Constituents,
        station.MajorAxisDirectionDegrees,
        station.MajorMeanVelocity,
        station.MinorMeanVelocity,
        station.FloodDirectionDegrees,
        station.EbbDirectionDegrees,
        station.IsRotary);

    private CurrentPredictionSource SourceFor(CurrentStationEntry station, CurrentStationEntry? reference) => new(
        station.Id,
        station.Name,
        station.Kind,
        station.DepthMeters,
        station.Kind == TideStationKind.Harmonic && station.IsRotary,
        reference?.Id,
        reference?.Name,
        Provider,
        Retrieved);

    private TidePredictionSource SourceFor(
        TideStationEntry station, TideStationEntry? reference, string datumName, VerticalDatum datum) => new(
            station.Id,
            station.Name,
            station.Kind,
            datumName,
            datum,
            reference?.Id,
            reference?.Name,
            Provider,
            Retrieved);

    private static void Validate(TideStationEntry station)
    {
        if (station.Kind == TideStationKind.Harmonic)
        {
            // TideStation checks the names and values; build one to reuse its checks.
            _ = new TideStation(station.Id, station.Name, station.Constituents, TideDatum.MeanSeaLevel);
        }
        else if (station.ReferenceStationId is null || station.Offsets is null)
        {
            throw new ArgumentException($"Subordinate station '{station.Id}' has no reference station or offsets.", nameof(station));
        }
    }

    private static void Validate(CurrentStationEntry station)
    {
        if (!double.IsFinite(station.FloodDirectionDegrees) || !double.IsFinite(station.EbbDirectionDegrees))
        {
            throw new ArgumentException($"Current station '{station.Id}' has a non-finite flood or ebb direction.", nameof(station));
        }

        if (station.Kind == TideStationKind.Harmonic)
        {
            // CurrentStation checks the names and values; build one to reuse its checks.
            _ = ToCurrentStation(station);
        }
        else if (station.ReferenceStationId is null || station.Offsets is null)
        {
            throw new ArgumentException($"Subordinate current station '{station.Id}' has no reference station or offsets.", nameof(station));
        }
    }

    private static List<(T Station, double DistanceMeters)> Nearest<T>(
        IReadOnlyList<T> stations, Func<T, GeoPosition> positionOf, Func<T, string> idOf, GeoPosition position, int maxCount, double maxDistanceMeters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCount);
        if (double.IsNaN(maxDistanceMeters) || maxDistanceMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistanceMeters), maxDistanceMeters, "The distance must be zero or more.");
        }

        if (maxCount == 0)
        {
            return [];
        }

        // A station further in latitude alone than the limit can't be in
        // range, so skip the geodesic for it.
        var maxLatitudeDegrees = maxDistanceMeters / MinimumMetersPerDegreeLatitude;
        var matches = new List<(T, double)>();
        foreach (var station in stations)
        {
            var at = positionOf(station);
            if (Math.Abs(at.Latitude - position.Latitude) > maxLatitudeDegrees)
            {
                continue;
            }

            var distance = Wgs84Geodesy.GeodesicDistanceMetres(position, at);
            if (distance <= maxDistanceMeters)
            {
                matches.Add((station, distance));
            }
        }

        return Rank(matches, idOf, maxCount);
    }

    private static List<(T Station, double DistanceMeters)> Within<T>(
        IReadOnlyList<T> stations, Func<T, GeoPosition> positionOf, Func<T, string> idOf, BoundingBox box, GeoPosition? rankFrom)
    {
        ArgumentNullException.ThrowIfNull(box);
        var crosses = box.WestLongitude > box.EastLongitude;
        var from = rankFrom ?? Centre(box, crosses);
        var matches = new List<(T, double)>();
        foreach (var station in stations)
        {
            var (latitude, longitude) = positionOf(station);
            var inLongitude = crosses
                ? longitude >= box.WestLongitude || longitude <= box.EastLongitude
                : longitude >= box.WestLongitude && longitude <= box.EastLongitude;
            if (inLongitude && latitude >= box.SouthLatitude && latitude <= box.NorthLatitude)
            {
                matches.Add((station, Wgs84Geodesy.GeodesicDistanceMetres(from, positionOf(station))));
            }
        }

        return Rank(matches, idOf, int.MaxValue);
    }

    private static GeoPosition Centre(BoundingBox box, bool crosses)
    {
        var east = crosses ? box.EastLongitude + 360 : box.EastLongitude;
        var longitude = (box.WestLongitude + east) / 2;
        if (longitude > 180)
        {
            longitude -= 360;
        }

        return new GeoPosition((box.SouthLatitude + box.NorthLatitude) / 2, longitude);
    }

    private static List<(T Station, double DistanceMeters)> Rank<T>(
        List<(T Station, double DistanceMeters)> matches, Func<T, string> idOf, int maxCount)
    {
        matches.Sort((a, b) =>
        {
            var order = a.DistanceMeters.CompareTo(b.DistanceMeters);
            return order != 0 ? order : string.CompareOrdinal(idOf(a.Station), idOf(b.Station));
        });
        if (matches.Count > maxCount)
        {
            matches.RemoveRange(maxCount, matches.Count - maxCount);
        }

        return matches;
    }
}
