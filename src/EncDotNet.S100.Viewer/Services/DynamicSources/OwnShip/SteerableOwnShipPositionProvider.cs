using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

/// <summary>
/// Steerable <see cref="IOwnShipPositionProvider"/> that dead-reckons a
/// single vessel and exposes a writable <see cref="IOwnShipHelm"/> so
/// the map gestures, the helm panel, the MCP <c>set_own_ship</c> tool,
/// and the pirate-mode controller can drive it. This is the default
/// own-ship driver.
/// </summary>
/// <remarks>
/// <para>
/// With no helm input the provider integrates the seed course and speed
/// along a great-circle solution and publishes one fix per tick. Helm
/// commands mutate the live kinematic state under a lock and re-base the
/// dead-reckoning clock so the next tick advances from the moment of the
/// correction — this <em>timestamp-baselined</em> advance prevents the
/// over-advance / jitter that a fixed-cadence tick would produce
/// immediately after an out-of-band correction (an MCP call, a helm
/// nudge, or an AIS fix in pirate mode).
/// </para>
/// <para>
/// The timer path measures real elapsed wall-clock time via the
/// injected <see cref="TimeProvider"/>; the <see cref="Tick(TimeSpan)"/>
/// seam advances by an explicit interval for deterministic tests.
/// </para>
/// </remarks>
internal sealed class SteerableOwnShipPositionProvider
    : IOwnShipPositionProvider, IOwnShipHelm, IOwnShipHelmState, IDisposable
{
    /// <summary>WGS-84 mean Earth radius in metres.</summary>
    private const double EarthRadiusMetres = 6_371_008.8;

    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly Timer? _timer;

    private double _lat;
    private double _lon;
    private double _courseDeg;
    private double _speedMs;
    private double _turnRateDegPerSec;
    private double? _headingDeg;
    private double _resumeSpeedMs;
    private OwnShipPosition _current;
    private DateTimeOffset _lastAdvanceUtc;
    private int _disposed;

    /// <summary>
    /// Creates a steerable provider with the supplied initial fix and an
    /// internal timer ticking at <paramref name="cadence"/>.
    /// </summary>
    /// <param name="start">
    /// Initial fix. A <see langword="null"/> course or speed is treated
    /// as zero (a stationary vessel pointing north) — unlike the
    /// synthetic driver, the steerable driver does not require motion at
    /// construction because the helm can supply it later.
    /// </param>
    /// <param name="cadence">
    /// Time between simulated fixes. Defaults to 1 second.
    /// </param>
    /// <param name="time">
    /// Optional time provider; defaults to
    /// <see cref="TimeProvider.System"/>. Tests pass a fake to keep the
    /// wall clock out of the loop.
    /// </param>
    public SteerableOwnShipPositionProvider(
        OwnShipPosition start,
        TimeSpan? cadence = null,
        TimeProvider? time = null)
        : this(start, cadence, time, startTimer: true) { }

    private SteerableOwnShipPositionProvider(
        OwnShipPosition start,
        TimeSpan? cadence,
        TimeProvider? time,
        bool startTimer)
    {
        _time = time ?? TimeProvider.System;
        _lat = start.Latitude;
        _lon = start.Longitude;
        _courseDeg = Normalize360(start.CourseOverGround?.TotalDegrees ?? 0.0);
        _speedMs = Math.Max(0.0, start.SpeedOverGround?.TotalMetresPerSecond ?? 0.0);
        _headingDeg = start.Heading is { } h ? Normalize360(h.TotalDegrees) : null;
        _resumeSpeedMs = _speedMs;
        _current = Snapshot(start.Timestamp);
        _lastAdvanceUtc = start.Timestamp;

        if (startTimer)
        {
            var period = cadence ?? TimeSpan.FromSeconds(1);
            _lastAdvanceUtc = _time.GetUtcNow();
            _timer = new Timer(_ => OnTimer(), state: null, period, period);
        }
    }

    /// <summary>
    /// Factory that creates a timer-less provider for tests — the
    /// simulation is advanced explicitly via <see cref="Tick(TimeSpan)"/>.
    /// </summary>
    public static SteerableOwnShipPositionProvider CreateManual(
        OwnShipPosition start,
        TimeProvider? time = null)
        => new(start, cadence: null, time, startTimer: false);

    /// <inheritdoc />
    public OwnShipPosition? Current
    {
        get { lock (_gate) return _current; }
    }

    /// <inheritdoc />
    public event EventHandler<OwnShipPosition>? Updated;

    // ---- IOwnShipHelmState -------------------------------------------

    /// <inheritdoc />
    public bool IsHeld
    {
        get { lock (_gate) return _speedMs == 0.0 && _resumeSpeedMs > 0.0; }
    }

    /// <inheritdoc />
    public double TurnRateDegPerSec
    {
        get { lock (_gate) return _turnRateDegPerSec; }
    }

    /// <inheritdoc />
    public double CommandedSpeedMs
    {
        get { lock (_gate) return (_speedMs == 0.0 && _resumeSpeedMs > 0.0) ? _resumeSpeedMs : _speedMs; }
    }

    /// <summary>
    /// Advances the simulation by <paramref name="elapsed"/> of
    /// simulated time and publishes the new fix. Used by tests; the
    /// timestamp advances by the supplied interval from the current
    /// fix's timestamp.
    /// </summary>
    public void Tick(TimeSpan elapsed)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (elapsed <= TimeSpan.Zero) return;

        OwnShipPosition next;
        lock (_gate)
        {
            var timestamp = _current.Timestamp + elapsed;
            next = AdvanceLocked(elapsed, timestamp);
        }
        Updated?.Invoke(this, next);
    }

    private void OnTimer()
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        OwnShipPosition next;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var elapsed = now - _lastAdvanceUtc;
            if (elapsed <= TimeSpan.Zero)
            {
                // Clock did not advance (or went backwards); re-base and
                // skip this tick rather than integrating a non-positive
                // interval.
                _lastAdvanceUtc = now;
                return;
            }
            next = AdvanceLocked(elapsed, now);
        }
        Updated?.Invoke(this, next);
    }

    /// <summary>
    /// Integrates one step under the lock: rotates the course by the
    /// turn rate, advances the position along the resulting course, and
    /// updates the cached fix and dead-reckoning clock. Returns the new
    /// fix; the caller raises <see cref="Updated"/> outside the lock.
    /// </summary>
    private OwnShipPosition AdvanceLocked(TimeSpan elapsed, DateTimeOffset timestamp)
    {
        var seconds = elapsed.TotalSeconds;

        if (_turnRateDegPerSec != 0.0)
        {
            _courseDeg = Normalize360(_courseDeg + _turnRateDegPerSec * seconds);
        }

        var distanceMetres = _speedMs * seconds;
        if (distanceMetres > 0.0)
        {
            (_lat, _lon) = GeodeticDestination(_lat, _lon, _courseDeg, distanceMetres);
        }

        _lastAdvanceUtc = timestamp;
        _current = Snapshot(timestamp);
        return _current;
    }

    private OwnShipPosition Snapshot(DateTimeOffset timestamp)
        => new(
            Latitude: _lat,
            Longitude: _lon,
            CourseOverGround: Angle.FromDegrees(_courseDeg),
            SpeedOverGround: Speed.FromMetresPerSecond(_speedMs),
            Timestamp: timestamp,
            Heading: _headingDeg is { } h ? Angle.FromDegrees(h) : null);

    // ---- IOwnShipHelm -------------------------------------------------

    /// <inheritdoc />
    public void SetState(
        double latitude,
        double longitude,
        double? courseOverGroundDeg = null,
        double? speedOverGroundMs = null,
        double? headingDeg = null)
        => MutateAndPublish(() =>
        {
            _lat = latitude;
            _lon = longitude;
            if (courseOverGroundDeg is { } cog) _courseDeg = Normalize360(cog);
            if (speedOverGroundMs is { } sog)
            {
                _speedMs = Math.Max(0.0, sog);
                if (_speedMs > 0.0) _resumeSpeedMs = _speedMs;
            }
            _headingDeg = headingDeg is { } h ? Normalize360(h) : null;
        });

    /// <inheritdoc />
    public void SetCourse(double courseDeg)
        => MutateAndPublish(() => _courseDeg = Normalize360(courseDeg));

    /// <inheritdoc />
    public void NudgeCourse(double deltaDeg)
        => MutateAndPublish(() => _courseDeg = Normalize360(_courseDeg + deltaDeg));

    /// <inheritdoc />
    public void SetSpeed(double speedMs)
        => MutateAndPublish(() =>
        {
            _speedMs = Math.Max(0.0, speedMs);
            if (_speedMs > 0.0) _resumeSpeedMs = _speedMs;
        });

    /// <inheritdoc />
    public void NudgeSpeed(double deltaMs)
        => MutateAndPublish(() =>
        {
            _speedMs = Math.Max(0.0, _speedMs + deltaMs);
            if (_speedMs > 0.0) _resumeSpeedMs = _speedMs;
        });

    /// <inheritdoc />
    public void SetTurnRate(double degreesPerSecond)
        => MutateAndPublish(() => _turnRateDegPerSec = degreesPerSecond);

    /// <inheritdoc />
    public void SteerToward(double latitude, double longitude)
        => MutateAndPublish(() =>
            _courseDeg = InitialBearing(_lat, _lon, latitude, longitude));

    /// <inheritdoc />
    public void Hold()
        => MutateAndPublish(() =>
        {
            if (_speedMs > 0.0)
            {
                _resumeSpeedMs = _speedMs;
                _speedMs = 0.0;
            }
        });

    /// <inheritdoc />
    public void Resume()
        => MutateAndPublish(() =>
        {
            if (_speedMs == 0.0 && _resumeSpeedMs > 0.0)
            {
                _speedMs = _resumeSpeedMs;
            }
        });

    /// <summary>
    /// Applies <paramref name="mutate"/> under the lock, re-bases the
    /// dead-reckoning clock to "now" so the following tick measures
    /// elapsed time from this correction, and publishes a fresh fix
    /// stamped now. Raises <see cref="Updated"/> outside the lock.
    /// </summary>
    private void MutateAndPublish(Action mutate)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        OwnShipPosition next;
        lock (_gate)
        {
            mutate();
            var now = _time.GetUtcNow();
            _lastAdvanceUtc = now;
            _current = Snapshot(now);
            next = _current;
        }
        Updated?.Invoke(this, next);
    }

    // ---- Geodesy ------------------------------------------------------

    /// <summary>
    /// Great-circle destination given a start point (degrees), bearing
    /// (degrees true), and distance (metres).
    /// </summary>
    internal static GeoPosition GeodeticDestination(
        double latDeg, double lonDeg, double bearingDeg, double distanceMetres)
    {
        var δ = distanceMetres / EarthRadiusMetres;
        var θ = bearingDeg * Math.PI / 180.0;
        var φ1 = latDeg * Math.PI / 180.0;
        var λ1 = lonDeg * Math.PI / 180.0;

        var sinφ1 = Math.Sin(φ1);
        var cosφ1 = Math.Cos(φ1);
        var sinδ = Math.Sin(δ);
        var cosδ = Math.Cos(δ);

        var sinφ2 = sinφ1 * cosδ + cosφ1 * sinδ * Math.Cos(θ);
        var φ2 = Math.Asin(sinφ2);
        var y = Math.Sin(θ) * sinδ * cosφ1;
        var x = cosδ - sinφ1 * sinφ2;
        var λ2 = λ1 + Math.Atan2(y, x);

        var latOut = φ2 * 180.0 / Math.PI;
        var lonOut = ((λ2 * 180.0 / Math.PI) + 540.0) % 360.0 - 180.0;
        return new GeoPosition(latOut, lonOut);
    }

    /// <summary>
    /// Initial great-circle bearing (degrees true, in [0, 360)) from one
    /// point to another, both in decimal degrees.
    /// </summary>
    internal static double InitialBearing(
        double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
    {
        var φ1 = lat1Deg * Math.PI / 180.0;
        var φ2 = lat2Deg * Math.PI / 180.0;
        var Δλ = (lon2Deg - lon1Deg) * Math.PI / 180.0;

        var y = Math.Sin(Δλ) * Math.Cos(φ2);
        var x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        var θ = Math.Atan2(y, x);
        return Normalize360(θ * 180.0 / Math.PI);
    }

    /// <summary>Normalises an angle in degrees to [0, 360).</summary>
    internal static double Normalize360(double degrees)
    {
        var r = degrees % 360.0;
        return r < 0.0 ? r + 360.0 : r;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer?.Dispose();
    }
}
