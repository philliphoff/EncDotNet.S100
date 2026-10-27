using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.DynamicSources.Ndbc;

/// <summary>
/// NOAA National Data Buoy Center observations as an
/// <see cref="IDynamicFeatureSource"/>: one point feature per station in view,
/// carrying the station's latest standard meteorological observation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fetching.</b> The source reads the station table
/// (<see cref="NdbcObservationSourceOptions.StationTablePath"/>) once a day
/// and, for the stations with meteorological data inside the viewport plus
/// <see cref="NdbcObservationSourceOptions.MarginDegrees"/>, the head of each
/// station's <c>realtime2</c> file. Nothing is fetched until the host sets an
/// area with <see cref="UpdateArea"/>, nor while that area is wider than
/// <see cref="NdbcObservationSourceOptions.MaxViewportSpanDegrees"/>, nor while
/// <see cref="IsActive"/> is false (the host's visibility toggle).
/// </para>
/// <para>
/// <b>Polling.</b> <see cref="Start"/> runs a loop that fetches each station
/// again <see cref="NdbcObservationSourceOptions.PollInterval"/> (10 minutes)
/// after its last success. A station that fails waits the poll interval, then
/// twice that, up to <see cref="NdbcObservationSourceOptions.MaxBackoff"/>; a
/// station table that fails to load backs off the same way from
/// <see cref="NdbcObservationSourceOptions.InitialBackoff"/>. A new area wakes
/// the loop, so stations panned into view are fetched at once. Hosts that
/// drive their own schedule call <see cref="PollAsync"/> instead.
/// </para>
/// <para>
/// <b>Ageing.</b> An observation older than
/// <see cref="NdbcObservationSourceOptions.StaleAfter"/> (3 hours) is marked
/// stale (<see cref="StaleAttribute"/>, <see cref="StaleFeatureKind"/>) and
/// drawn grey; one older than <see cref="NdbcObservationSourceOptions.DropAfter"/>
/// (12 hours) is dropped. A station that leaves the fetch area leaves the
/// snapshot, but its last observation is kept, so panning back shows it again
/// without a fetch.
/// </para>
/// <para>
/// <b>Threading.</b> <see cref="CurrentFeatures"/> is an immutable snapshot,
/// safe from any thread. <see cref="Changed"/> is raised on the polling thread.
/// Time comes from the <see cref="TimeProvider"/> passed in, so tests can run
/// the loop on a fake clock.
/// </para>
/// </remarks>
public sealed class NdbcObservationSource : IDynamicFeatureSource, IDynamicFeatureDescriber, IAsyncDisposable
{
    /// <summary>The <see cref="DynamicFeature.Kind"/> of a station, and the source's renderer key: <c>weather.observation.ndbc</c>.</summary>
    public const string FeatureKind = "weather.observation.ndbc";

    /// <summary>The <see cref="DynamicFeature.Kind"/> of a station whose observation is stale.</summary>
    public const string StaleFeatureKind = FeatureKind + ".stale";

    /// <summary>The attribute holding the station's <see cref="NdbcObservation"/>.</summary>
    public const string ObservationAttribute = "observation";

    /// <summary>The attribute holding whether the observation is stale (a <see cref="bool"/>).</summary>
    public const string StaleAttribute = "stale";

    /// <summary>The attribute holding the station id.</summary>
    public const string StationIdAttribute = "stationId";

    /// <summary>The attribute holding the station name.</summary>
    public const string StationNameAttribute = "stationName";

    /// <summary>The attribute holding the station type (e.g. <c>buoy</c> or <c>fixed</c>), when the table gives one.</summary>
    public const string StationTypeAttribute = "stationType";

    private readonly HttpClient _http;
    private readonly NdbcObservationSourceOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly Dictionary<string, StationState> _states = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _disposeCts = new();

    private IReadOnlyList<DynamicFeature> _snapshot = [];
    private BoundingBox? _area;
    private bool _isActive = true;
    private NdbcStationTable? _table;
    private DateTimeOffset _tableExpires;
    private DateTimeOffset _tableRetryAt;
    private int _tableFailures;
    private CancellationTokenSource _wake = new();
    private Task? _loop;
    private int _disposed;

    /// <summary>Creates a source. It fetches nothing until <see cref="UpdateArea"/> gives it an area.</summary>
    /// <param name="httpClient">
    /// The client to fetch with. The caller owns it: its lifetime, timeout and
    /// <c>User-Agent</c> (NDBC asks automated clients to identify themselves).
    /// </param>
    /// <param name="options">The options; <see langword="null"/> for the defaults.</param>
    /// <param name="timeProvider">The clock; <see langword="null"/> for <see cref="TimeProvider.System"/>.</param>
    /// <param name="logger">The logger; <see langword="null"/> for none.</param>
    public NdbcObservationSource(
        HttpClient httpClient,
        NdbcObservationSourceOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<NdbcObservationSource>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _options = options ?? new NdbcObservationSourceOptions();
        _options.Validate();
        _http = httpClient;
        _time = timeProvider ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;

        Id = _options.Id;
        Metadata = new DynamicSourceMetadata
        {
            DisplayName = _options.DisplayName,
            Description = "Latest wind, wave and weather observations from NOAA NDBC buoys and C-MAN stations. Not for navigation.",
            RendererKey = FeatureKind,
            Plane = S98DisplayPlane.OtherChartOverlays,
            WithinPlanePriority = NdbcStationPortrayal.WithinPlanePriority,
        };
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public DynamicSourceMetadata Metadata { get; }

    /// <summary>The options the source was created with.</summary>
    public NdbcObservationSourceOptions Options => _options;

    /// <inheritdoc />
    public IReadOnlyList<DynamicFeature> CurrentFeatures
    {
        get { lock (_lock) return _snapshot; }
    }

    /// <inheritdoc />
    public event EventHandler<DynamicFeaturesChanged>? Changed;

    /// <summary>The area set by <see cref="UpdateArea"/>, or <see langword="null"/> before one is set.</summary>
    public BoundingBox? Area
    {
        get { lock (_lock) return _area; }
    }

    /// <summary>
    /// Whether the source fetches. A host binds it to its visibility toggle,
    /// so a hidden layer makes no requests. Setting it true wakes the loop.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool IsActive
    {
        get { lock (_lock) return _isActive; }
        set
        {
            bool wake;
            lock (_lock)
            {
                wake = value && !_isActive;
                _isActive = value;
            }
            if (wake)
                Wake();
        }
    }

    /// <summary>The station table, once it has loaded.</summary>
    public NdbcStationTable? StationTable
    {
        get { lock (_lock) return _table; }
    }

    /// <summary>The last fetch error, or <see langword="null"/> when the last poll had none.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Sets the area to fetch stations for, usually the viewport. Stations
    /// within it plus the margin are fetched on the next poll, which this
    /// wakes. Hosts should debounce pans. <see langword="null"/> fetches nothing.
    /// </summary>
    /// <param name="area">The area, west edge first; it may cross the antimeridian.</param>
    public void UpdateArea(BoundingBox? area)
    {
        ThrowIfDisposed();
        lock (_lock) _area = area;
        Wake();
    }

    /// <summary>Starts the polling loop. Calling it again does nothing.</summary>
    public void Start()
    {
        ThrowIfDisposed();
        lock (_lock)
            _loop ??= Task.Run(() => RunAsync(_disposeCts.Token));
    }

    /// <summary>
    /// Runs one poll: loads the station table if it is due, fetches the stations
    /// in the area that are due, then re-ages the snapshot. Does nothing but age
    /// while <see cref="IsActive"/> is false or the area is unset or too wide.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the poll did.</returns>
    public async Task<NdbcPollResult> PollAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await PollCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    /// <summary>
    /// Re-ages the snapshot at the clock's current time: marks observations
    /// older than <see cref="NdbcObservationSourceOptions.StaleAfter"/> stale
    /// and drops those older than <see cref="NdbcObservationSourceOptions.DropAfter"/>,
    /// raising <see cref="Changed"/> when anything changed.
    /// </summary>
    public void Age()
    {
        ThrowIfDisposed();
        Publish(_time.GetUtcNow());
    }

    /// <summary>The feature id of station <paramref name="stationId"/>: <c>ndbc:{id}</c>.</summary>
    /// <param name="stationId">The station id.</param>
    public static string FeatureIdFor(string stationId) => "ndbc:" + stationId;

    /// <summary>The observation a feature of this source carries, or <see langword="null"/>.</summary>
    /// <param name="feature">The feature.</param>
    public static NdbcObservation? GetObservation(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Attributes.TryGetValue(ObservationAttribute, out var value) ? value as NdbcObservation : null;
    }

    /// <summary>Whether a feature of this source is stale.</summary>
    /// <param name="feature">The feature.</param>
    public static bool IsStale(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Attributes.TryGetValue(StaleAttribute, out var value) && value is true;
    }

    /// <inheritdoc />
    public DynamicFeatureDescription? Describe(DynamicFeature feature, IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (GetObservation(feature) is not { } observation)
            return null;

        feature.Attributes.TryGetValue(StationNameAttribute, out var name);
        feature.Attributes.TryGetValue(StationTypeAttribute, out var type);
        return NdbcObservationFormatter.Describe(
            observation,
            name as string,
            type as string,
            IsStale(feature),
            _time.GetUtcNow(),
            formatProvider ?? CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _disposeCts.Cancel();
        Task? loop;
        lock (_lock) loop = _loop;
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _disposeCts.Dispose();
        lock (_lock) _wake.Dispose();
        _pollGate.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Take a fresh wake token before polling, so an area change during
            // the poll cuts the following wait short.
            CancellationToken wake;
            lock (_lock)
            {
                if (_wake.IsCancellationRequested)
                {
                    _wake.Dispose();
                    _wake = new CancellationTokenSource();
                }
                wake = _wake.Token;
            }

            try
            {
                await PollAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LastError = ex;
                _logger.LogWarning(ex, "NDBC poll failed.");
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, wake);
            try
            {
                await Task.Delay(NextDelay(), _time, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Woken by an area change or activation.
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Wake()
    {
        lock (_lock)
        {
            if (Volatile.Read(ref _disposed) == 0)
                _wake.Cancel();
        }
    }

    // The time until the next thing the loop has to do: a station falls due,
    // the table can be retried, or an observation turns stale or drops. Never
    // more than the poll interval, never under a second.
    private TimeSpan NextDelay()
    {
        var now = _time.GetUtcNow();
        var next = now + _options.PollInterval;
        lock (_lock)
        {
            var fetchArea = FetchArea(_area);
            foreach (var (id, state) in _states)
            {
                if (fetchArea is not null && state.Station is { } s
                    && NdbcStationTable.Contains(fetchArea, s.Latitude, s.Longitude)
                    && state.NextDue < next)
                {
                    next = state.NextDue;
                }

                if (state.Observation is { } o)
                {
                    foreach (var edge in (DateTimeOffset[])[o.Time + _options.StaleAfter, o.Time + _options.DropAfter])
                    {
                        if (edge > now && edge < next)
                            next = edge;
                    }
                }
            }

            if (_table is null && _tableRetryAt > now && _tableRetryAt < next)
                next = _tableRetryAt;
        }

        var delay = next - now;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
    }

    private async Task<NdbcPollResult> PollCoreAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        BoundingBox? area;
        bool active;
        lock (_lock)
        {
            area = _area;
            active = _isActive;
        }

        var fetchArea = FetchArea(area);
        if (!active || fetchArea is null || !WithinSpanGate(area!))
        {
            Publish(now);
            return new NdbcPollResult(NdbcPollOutcome.Gated, 0, 0);
        }

        var table = await EnsureStationTableAsync(now, cancellationToken).ConfigureAwait(false);
        if (table is null)
        {
            Publish(now);
            return new NdbcPollResult(NdbcPollOutcome.NoStationTable, 0, 0);
        }

        double centreLat = (area!.SouthLatitude + area.NorthLatitude) / 2;
        double centreLon = CentreLongitude(area);
        List<NdbcStation> due;
        lock (_lock)
        {
            due = table.MeteorologicalStationsWithin(fetchArea)
                .Where(s => !_states.TryGetValue(s.Id, out var st) || st.NextDue <= now)
                .OrderBy(s => SquaredDistance(s, centreLat, centreLon))
                .Take(_options.MaxStationsPerPoll)
                .ToList();
        }

        int fetched = 0, failed = 0;
        Exception? lastError = null;
        using var throttle = new SemaphoreSlim(_options.MaxConcurrentRequests);
        var tasks = due.Select(async station =>
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var observation = await FetchStationAsync(station, now, cancellationToken).ConfigureAwait(false);
                lock (_lock)
                {
                    var state = State(station);
                    state.Failures = 0;
                    state.NextDue = now + _options.PollInterval;
                    if (observation is not null && (state.Observation is null || observation.Time >= state.Observation.Time))
                        state.Observation = observation;
                }
                Interlocked.Increment(ref fetched);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                lock (_lock)
                {
                    var state = State(station);
                    state.Failures++;
                    state.NextDue = now + Backoff(_options.PollInterval, state.Failures);
                    lastError = ex;
                }
                Interlocked.Increment(ref failed);
                _logger.LogDebug(ex, "NDBC station {Station} failed.", station.Id);
            }
            finally
            {
                throttle.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        LastError = lastError;
        if (failed > 0)
            _logger.LogInformation("NDBC poll: {Fetched} stations fetched, {Failed} failed.", fetched, failed);

        Publish(_time.GetUtcNow());
        return new NdbcPollResult(NdbcPollOutcome.Polled, fetched, failed);
    }

    private async Task<NdbcStationTable?> EnsureStationTableAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_table is not null && now < _tableExpires)
                return _table;
            if (now < _tableRetryAt)
                return _table;
        }

        try
        {
            var uri = new Uri(_options.BaseAddress, _options.StationTablePath);
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var table = NdbcStationTable.Parse(stream);
            lock (_lock)
            {
                _table = table;
                _tableExpires = now + _options.StationTableRefreshInterval;
                _tableFailures = 0;
                foreach (var station in table.Stations)
                {
                    if (_states.TryGetValue(station.Id, out var state))
                        state.Station = station;
                }
                return table;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            lock (_lock)
            {
                _tableFailures++;
                _tableRetryAt = now + Backoff(_options.InitialBackoff, _tableFailures);
                LastError = ex;
                _logger.LogWarning(ex, "NDBC station table failed to load; retrying at {RetryAt}.", _tableRetryAt);
                return _table;
            }
        }
    }

    private async Task<NdbcObservation?> FetchStationAsync(NdbcStation station, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            _options.BaseAddress,
            string.Format(CultureInfo.InvariantCulture, _options.ObservationPathFormat, Uri.EscapeDataString(station.Id)));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (_options.RangeBytes is { } range)
            request.Headers.Range = new RangeHeaderValue(0, range - 1);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            return null; // An empty file.
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var rows = NdbcRealtimeParser.Parse(reader, station.Id, notBefore: now - _options.DropAfter);
        return NdbcObservation.Combine(rows, _options.CombineWindow) is { HasAnyValue: true } observation
            ? observation
            : null;
    }

    // Rebuilds the snapshot from the cached observations in the fetch area at
    // `now`, and raises Changed when it differs from the last one.
    private void Publish(DateTimeOffset now)
    {
        List<string> changed = [];
        lock (_lock)
        {
            var fetchArea = FetchArea(_area);
            var features = new List<DynamicFeature>();
            foreach (var state in _states.Values)
            {
                if (state.Observation is not { } observation || state.Station is not { } station)
                    continue;
                var age = now - observation.Time;
                if (age > _options.DropAfter)
                {
                    state.Observation = null;
                    continue;
                }
                if (fetchArea is not null && !NdbcStationTable.Contains(fetchArea, station.Latitude, station.Longitude))
                    continue;

                features.Add(Project(station, observation, stale: age > _options.StaleAfter));
            }

            features.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));

            var previous = _snapshot.ToDictionary(f => f.Id, StringComparer.Ordinal);
            foreach (var feature in features)
            {
                if (!previous.Remove(feature.Id, out var old)
                    || old.LastUpdated != feature.LastUpdated
                    || IsStale(old) != IsStale(feature)
                    || !Equals(GetObservation(old), GetObservation(feature)))
                {
                    changed.Add(feature.Id);
                }
            }
            changed.AddRange(previous.Keys);

            if (changed.Count == 0)
                return;
            _snapshot = features;
        }

        Changed?.Invoke(this, new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset, ChangedIds = changed });
    }

    private static DynamicFeature Project(NdbcStation station, NdbcObservation observation, bool stale)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [StationIdAttribute] = station.Id,
            [StationNameAttribute] = station.Name,
            [ObservationAttribute] = observation,
            [StaleAttribute] = stale,
        };
        if (station.Type is { } type)
            attributes[StationTypeAttribute] = type;

        return new DynamicFeature
        {
            Id = FeatureIdFor(station.Id),
            Kind = stale ? StaleFeatureKind : FeatureKind,
            GeometryType = GeometryType.Point,
            Coordinates = [new GeoPosition(station.Latitude, station.Longitude)],
            Attributes = attributes,
            LastUpdated = observation.Time,
        };
    }

    private StationState State(NdbcStation station)
    {
        if (!_states.TryGetValue(station.Id, out var state))
        {
            state = new StationState();
            _states[station.Id] = state;
        }
        state.Station = station;
        return state;
    }

    private TimeSpan Backoff(TimeSpan initial, int failures)
    {
        double factor = Math.Pow(2, Math.Min(failures - 1, 20));
        var delay = TimeSpan.FromTicks((long)Math.Min(initial.Ticks * factor, _options.MaxBackoff.Ticks));
        return delay < initial ? initial : delay;
    }

    private BoundingBox? FetchArea(BoundingBox? area)
    {
        if (area is null)
            return null;

        double m = _options.MarginDegrees;
        double south = Math.Max(-90, area.SouthLatitude - m);
        double north = Math.Min(90, area.NorthLatitude + m);
        double span = LongitudeSpan(area) + 2 * m;
        if (span >= 360)
            return new BoundingBox(south, -180, north, 180);

        return new BoundingBox(south, WrapLongitude(area.WestLongitude - m), north, WrapLongitude(area.EastLongitude + m));
    }

    private bool WithinSpanGate(BoundingBox area) =>
        _options.MaxViewportSpanDegrees is not { } max
        || (area.NorthLatitude - area.SouthLatitude <= max && LongitudeSpan(area) <= max);

    private static double LongitudeSpan(BoundingBox area) =>
        area.WestLongitude <= area.EastLongitude
            ? area.EastLongitude - area.WestLongitude
            : 360 - (area.WestLongitude - area.EastLongitude);

    private static double CentreLongitude(BoundingBox area) =>
        WrapLongitude(area.WestLongitude + LongitudeSpan(area) / 2);

    private static double WrapLongitude(double longitude)
    {
        double wrapped = (longitude + 180) % 360;
        if (wrapped < 0)
            wrapped += 360;
        return wrapped - 180;
    }

    private static double SquaredDistance(NdbcStation station, double latitude, double longitude)
    {
        double dLat = station.Latitude - latitude;
        double dLon = Math.Abs(station.Longitude - longitude);
        if (dLon > 180)
            dLon = 360 - dLon;
        dLon *= Math.Cos(latitude * Math.PI / 180);
        return dLat * dLat + dLon * dLon;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed class StationState
    {
        public NdbcStation? Station { get; set; }
        public NdbcObservation? Observation { get; set; }
        public DateTimeOffset NextDue { get; set; }
        public int Failures { get; set; }
    }
}

/// <summary>What an <see cref="NdbcObservationSource.PollAsync"/> did.</summary>
/// <param name="Outcome">Whether stations were polled, and if not why.</param>
/// <param name="Fetched">The stations fetched successfully.</param>
/// <param name="Failed">The stations whose fetch failed.</param>
public readonly record struct NdbcPollResult(NdbcPollOutcome Outcome, int Fetched, int Failed);

/// <summary>The outcome of an <see cref="NdbcObservationSource.PollAsync"/>.</summary>
public enum NdbcPollOutcome
{
    /// <summary>The due stations in the area were fetched (possibly none).</summary>
    Polled,

    /// <summary>Nothing was fetched: the source is inactive, or the area is unset or wider than the span gate.</summary>
    Gated,

    /// <summary>Nothing was fetched: the station table hasn't loaded and isn't due for a retry.</summary>
    NoStationTable,
}
