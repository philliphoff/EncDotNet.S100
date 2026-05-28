using System.Threading;
using System.Threading.Tasks;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

/// <summary>
/// Synthetic <see cref="IOwnShipPositionProvider"/> that
/// dead-reckons a single vessel along a fixed course at a fixed
/// speed using a great-circle solution. Used as scaffolding by
/// PR-D2 until a real GPS / NMEA driver lands.
/// </summary>
/// <remarks>
/// <para>
/// Constructor parameters define the initial state. The provider
/// drives itself on a <see cref="Timer"/>-backed cadence and
/// publishes a fresh fix per tick. Tests use the
/// <see cref="Tick(TimeSpan)"/> seam to advance the simulation
/// deterministically without depending on wall-clock timing.
/// </para>
/// <para>
/// The dead-reckoning uses the WGS-84 mean Earth radius
/// (6 371 008.8 m). Accuracy is sufficient for the scaffolding role
/// — a future real driver replaces this implementation rather than
/// improving its numerics.
/// </para>
/// </remarks>
internal sealed class SyntheticOwnShipPositionProvider : IOwnShipPositionProvider, IDisposable
{
    /// <summary>WGS-84 mean Earth radius in metres.</summary>
    private const double EarthRadiusMetres = 6_371_008.8;

    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly Timer? _timer;
    private readonly double _courseDeg;
    private readonly double _speedMs;
    private OwnShipPosition _current;
    private int _disposed;

    /// <summary>
    /// Creates a synthetic provider with the supplied initial fix
    /// and an internal timer ticking at <paramref name="cadence"/>.
    /// </summary>
    /// <param name="start">
    /// Initial fix. <c>CourseOverGroundDeg</c> and
    /// <c>SpeedOverGroundMs</c> must be non-null — the synthetic
    /// driver always has motion. <c>Timestamp</c> seeds the clock.
    /// </param>
    /// <param name="cadence">
    /// Time between simulated fixes. Defaults to 1 second.
    /// </param>
    /// <param name="time">
    /// Optional time provider; defaults to
    /// <see cref="TimeProvider.System"/>. Tests pass a fake to keep
    /// the wall clock out of the loop.
    /// </param>
    public SyntheticOwnShipPositionProvider(
        OwnShipPosition start,
        TimeSpan? cadence = null,
        TimeProvider? time = null)
        : this(start, cadence, time, startTimer: true) { }

    private SyntheticOwnShipPositionProvider(
        OwnShipPosition start,
        TimeSpan? cadence,
        TimeProvider? time,
        bool startTimer)
    {
        if (start.CourseOverGroundDeg is null)
            throw new ArgumentException(
                "Synthetic provider requires an initial course over ground.",
                nameof(start));
        if (start.SpeedOverGroundMs is null)
            throw new ArgumentException(
                "Synthetic provider requires an initial speed over ground.",
                nameof(start));

        _time = time ?? TimeProvider.System;
        _courseDeg = start.CourseOverGroundDeg.Value;
        _speedMs = start.SpeedOverGroundMs.Value;
        _current = start;

        if (startTimer)
        {
            var period = cadence ?? TimeSpan.FromSeconds(1);
            _timer = new Timer(_ => TickInternal(period), state: null, period, period);
        }
    }

    /// <summary>
    /// Factory that creates a manual-tick provider for tests — no
    /// internal timer; tests drive the simulation via
    /// <see cref="Tick(TimeSpan)"/>.
    /// </summary>
    public static SyntheticOwnShipPositionProvider CreateManual(
        OwnShipPosition start,
        TimeProvider? time = null)
        => new(start, cadence: null, time, startTimer: false);

    /// <inheritdoc />
    public OwnShipPosition? Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    /// <inheritdoc />
    public event EventHandler<OwnShipPosition>? Updated;

    /// <summary>
    /// Advances the simulation by <paramref name="elapsed"/> and
    /// raises <see cref="Updated"/> with the new fix. Used by the
    /// internal timer; also callable from tests when the provider
    /// was constructed via <see cref="CreateManual"/>.
    /// </summary>
    public void Tick(TimeSpan elapsed) => TickInternal(elapsed);

    private void TickInternal(TimeSpan elapsed)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (elapsed <= TimeSpan.Zero) return;

        OwnShipPosition next;
        lock (_gate)
        {
            var distanceMetres = _speedMs * elapsed.TotalSeconds;
            var (lat, lon) = GeodeticDestination(
                _current.Latitude, _current.Longitude, _courseDeg, distanceMetres);
            next = new OwnShipPosition(
                Latitude: lat,
                Longitude: lon,
                CourseOverGroundDeg: _courseDeg,
                SpeedOverGroundMs: _speedMs,
                Timestamp: _time.GetUtcNow());
            _current = next;
        }

        Updated?.Invoke(this, next);
    }

    /// <summary>
    /// Great-circle destination given a start point (degrees),
    /// bearing (degrees true), and distance (metres). Same shape
    /// the default renderer uses for its predictor line.
    /// </summary>
    internal static (double Latitude, double Longitude) GeodeticDestination(
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
        return (latOut, lonOut);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer?.Dispose();
    }
}
