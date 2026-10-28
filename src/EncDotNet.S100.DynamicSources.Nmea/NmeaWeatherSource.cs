using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Nmea;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Quantities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.DynamicSources.Nmea;

/// <summary>
/// The ship's own wind, water and air temperature and pressure, read from an
/// NMEA 0183 stream, as an <see cref="IDynamicFeatureSource"/>: one point
/// feature at own ship, drawn by <see cref="OwnShipWeatherPortrayal"/> as a
/// wind barb next to the own-ship symbol.
/// </summary>
/// <remarks>
/// <para>
/// <b>Input.</b> The source reads <c>MWV</c>, <c>MWD</c>, <c>MTW</c> and
/// <c>XDR</c> for the weather, and <c>HDT</c>, <c>HDG</c>, <c>VHW</c>,
/// <c>RMC</c> and <c>VTG</c> for the heading, speed, course and position that
/// true wind needs, from any talker. <see cref="Start"/> reads the
/// <see cref="INmeaLineSource"/> it was given in the background, retrying after
/// <see cref="NmeaWeatherSourceOptions.RetryDelay"/> when it fails;
/// <see cref="ReadToEndAsync"/> reads it once (a replay). A host that already
/// reads an NMEA stream, such as an own-ship driver, passes no input and feeds
/// lines to <see cref="ProcessLine"/> instead.
/// </para>
/// <para>
/// <b>True wind,</b> best first (see <see cref="TrueWindSource"/>):
/// </para>
/// <list type="number">
/// <item><description><c>MWD</c>, the instruments' own true direction and speed.</description></item>
/// <item><description>
/// Derived from relative wind (<c>MWV</c> R), the heading and the speed and
/// course over the ground (<see cref="TrueWind.OverGround"/>). Ground wind is
/// preferred because it is what forecasts and buoys report, so the barbs
/// compare like with like.
/// </description></item>
/// <item><description><c>MWV</c> T, the instruments' true wind angle, turned into a direction with the heading.</description></item>
/// <item><description>Derived from relative wind, the heading and the speed through the water (<see cref="TrueWind.ThroughWater"/>).</description></item>
/// </list>
/// <para>
/// When none of these can be had (no heading, or no speed) but relative wind
/// can, the feature carries the relative wind and
/// <see cref="OwnShipWeather.IsRelativeOnly"/>; the barb is drawn from the
/// heading plus the relative angle when the heading is known, and the label
/// says it is relative.
/// </para>
/// <para>
/// <b>Own ship.</b> The heading and motion come from the NMEA stream first,
/// since they come from the same instruments as the wind and so match it in
/// time; the host's own-ship fix (<see cref="UpdateOwnShip"/>) is the
/// fallback. The feature's position is the host's own-ship position first, so
/// the barb sits by the own-ship symbol, and the stream's <c>RMC</c> position
/// otherwise. With neither there is no feature. A heading, speed or course
/// from the stream older than <see cref="NmeaWeatherSourceOptions.MaxMotionAge"/>
/// is not used; the host's fix is used as given, until the host replaces or
/// clears it.
/// </para>
/// <para>
/// <b>Ageing.</b> A value not repeated for
/// <see cref="NmeaWeatherSourceOptions.DropAfter"/> is dropped. Changes are
/// published at most every <see cref="NmeaWeatherSourceOptions.PublishInterval"/>.
/// Once <see cref="Start"/> has been called a timer on the
/// <see cref="TimeProvider"/> publishes pending changes and ages values when
/// the stream goes quiet; otherwise <see cref="Refresh"/> does.
/// </para>
/// <para>
/// <b>Threading.</b> <see cref="CurrentFeatures"/> is an immutable snapshot,
/// safe from any thread. <see cref="Changed"/> is raised on the thread that
/// processed the sentence, the timer's thread, or the caller of
/// <see cref="Refresh"/>.
/// </para>
/// </remarks>
public sealed class NmeaWeatherSource : IDynamicFeatureSource, IDynamicFeatureDescriber, IAsyncDisposable
{
    /// <summary>The <see cref="DynamicFeature.Kind"/> of the feature, and the source's renderer key: <c>weather.observation.ownship</c>.</summary>
    public const string FeatureKind = "weather.observation.ownship";

    /// <summary>The feature's id: <c>ownship-weather</c>.</summary>
    public const string FeatureId = "ownship-weather";

    /// <summary>The attribute holding the feature's <see cref="OwnShipWeather"/>.</summary>
    public const string WeatherAttribute = "weather";

    // Below this speed over the ground the course is noise, so the heading
    // stands in for it.
    private static readonly Speed StationarySpeed = Speed.FromKnots(0.2);

    private readonly INmeaLineSource? _input;
    private readonly NmeaWeatherSourceOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private Stamped<(Angle Angle, Speed Speed)>? _relativeWind;
    private Stamped<(Angle Angle, Speed Speed)>? _mwvTrueWind;
    private Stamped<MwdSentence>? _mwd;
    private Stamped<Angle>? _hdt;
    private Stamped<Angle>? _hdgMagnetic; // sensor heading plus deviation
    private Stamped<Angle>? _vhwHeading;
    private Stamped<Angle>? _variation;
    private Stamped<Speed>? _speedThroughWater;
    private Stamped<(Speed Speed, Angle? Course)>? _groundMotion;
    private Stamped<GeoPosition>? _position;
    private Stamped<double>? _waterTemperature;
    private Stamped<double>? _airTemperature;
    private Stamped<double>? _pressure;
    private OwnShipReference? _host;

    private IReadOnlyList<DynamicFeature> _snapshot = [];
    private DateTimeOffset _lastPublish = DateTimeOffset.MinValue;
    private Task? _loop;
    private ITimer? _timer;
    private long _accepted;
    private long _rejected;
    private int _disposed;

    /// <summary>Creates a source.</summary>
    /// <param name="input">
    /// The NMEA input <see cref="Start"/> and <see cref="ReadToEndAsync"/> read;
    /// <see langword="null"/> when the host feeds <see cref="ProcessLine"/> itself.
    /// </param>
    /// <param name="options">The options; <see langword="null"/> for the defaults.</param>
    /// <param name="timeProvider">The clock; <see langword="null"/> for <see cref="TimeProvider.System"/>.</param>
    /// <param name="logger">The logger; <see langword="null"/> for none.</param>
    public NmeaWeatherSource(
        INmeaLineSource? input = null,
        NmeaWeatherSourceOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<NmeaWeatherSource>? logger = null)
    {
        _options = options ?? new NmeaWeatherSourceOptions();
        _options.Validate();
        _input = input;
        _time = timeProvider ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;

        Id = _options.Id;
        Metadata = new DynamicSourceMetadata
        {
            DisplayName = _options.DisplayName,
            Description = "The ship's own wind, water and air temperature and pressure from its NMEA 0183 instruments. Not for navigation.",
            RendererKey = FeatureKind,
            Plane = S98DisplayPlane.OtherChartOverlays,
            WithinPlanePriority = OwnShipWeatherPortrayal.WithinPlanePriority,
        };
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public DynamicSourceMetadata Metadata { get; }

    /// <summary>The options the source was created with.</summary>
    public NmeaWeatherSourceOptions Options => _options;

    /// <summary>The input, or <see langword="null"/> when the host feeds lines itself.</summary>
    public INmeaLineSource? Input => _input;

    /// <inheritdoc />
    public IReadOnlyList<DynamicFeature> CurrentFeatures
    {
        get { lock (_lock) return _snapshot; }
    }

    /// <inheritdoc />
    public event EventHandler<DynamicFeaturesChanged>? Changed;

    /// <summary>The number of lines accepted as valid sentences so far.</summary>
    public long SentencesAccepted => Interlocked.Read(ref _accepted);

    /// <summary>The number of lines rejected so far: bad checksums, cut-off sentences, noise.</summary>
    public long LinesRejected => Interlocked.Read(ref _rejected);

    /// <summary>The input's last failure, or <see langword="null"/>.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>The weather at own ship now, whether or not there is a position to draw it at; <see langword="null"/> when nothing is known.</summary>
    public OwnShipWeather? Current
    {
        get
        {
            var now = _time.GetUtcNow();
            lock (_lock) return Compute(now);
        }
    }

    /// <summary>The weather a feature of this source carries, or <see langword="null"/>.</summary>
    /// <param name="feature">The feature.</param>
    public static OwnShipWeather? GetWeather(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Attributes.TryGetValue(WeatherAttribute, out var value) ? value as OwnShipWeather : null;
    }

    /// <summary>
    /// Parses and processes one line of NMEA. Lines that aren't valid sentences
    /// (bad checksum, cut off, noise) are counted in <see cref="LinesRejected"/>
    /// and otherwise ignored, as are valid sentences this source doesn't use.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>Whether the line was a valid sentence.</returns>
    public bool ProcessLine(string line)
    {
        ThrowIfDisposed();
        if (!NmeaSentence.TryParse(line, out var sentence, out var error, _options.ParseOptions))
        {
            Interlocked.Increment(ref _rejected);
            if (error != NmeaParseError.Empty)
                _logger.LogDebug("Rejected NMEA line ({Error}): {Line}", error, line);
            return false;
        }

        Interlocked.Increment(ref _accepted);
        if (NmeaDecoder.TryDecode(sentence, out var message))
            Process(message);
        return true;
    }

    /// <summary>Processes one decoded sentence.</summary>
    /// <param name="message">The sentence.</param>
    public void Process(NmeaMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfDisposed();
        var now = _time.GetUtcNow();
        bool used = true;
        lock (_lock)
        {
            switch (message)
            {
                case MwvSentence { IsValid: true, Angle: { } angle, Speed: { } speed, Reference: WindReference.Relative }:
                    _relativeWind = new((angle, speed), now);
                    break;
                case MwvSentence { IsValid: true, Angle: { } angle, Speed: { } speed, Reference: WindReference.True }:
                    _mwvTrueWind = new((angle, speed), now);
                    break;
                case MwdSentence { Speed: not null } mwd when mwd.TrueDirection is not null || mwd.MagneticDirection is not null:
                    _mwd = new(mwd, now);
                    break;
                case MtwSentence { Celsius: { } water }:
                    _waterTemperature = new(water, now);
                    break;
                case XdrSentence xdr:
                    used = false;
                    if (xdr.AirTemperatureCelsius is { } air)
                    {
                        _airTemperature = new(air, now);
                        used = true;
                    }
                    if (xdr.PressureHectopascals is { } hpa)
                    {
                        _pressure = new(hpa, now);
                        used = true;
                    }
                    break;
                case HdtSentence { Heading: { } hdt }:
                    _hdt = new(hdt, now);
                    break;
                case HdgSentence hdg when hdg.MagneticSensorHeading is { } sensor:
                    _hdgMagnetic = new(NmeaAngles.Normalise(sensor + (hdg.Deviation ?? Angle.Zero)), now);
                    if (hdg.Variation is { } v)
                        _variation = new(v, now);
                    break;
                case VhwSentence vhw when vhw.TrueHeading is not null || vhw.SpeedThroughWater is not null:
                    if (vhw.TrueHeading is { } h)
                        _vhwHeading = new(h, now);
                    if (vhw.SpeedThroughWater is { } stw)
                        _speedThroughWater = new(stw, now);
                    break;
                case RmcSentence rmc:
                    used = false;
                    if (rmc.MagneticVariation is { } variation)
                    {
                        _variation = new(variation, now);
                        used = true;
                    }
                    if (rmc.IsValid && rmc.Position is { } position)
                    {
                        _position = new(position, now);
                        used = true;
                    }
                    if (rmc.IsValid && rmc.SpeedOverGround is { } sog)
                    {
                        _groundMotion = new((sog, rmc.CourseOverGround), now);
                        used = true;
                    }
                    break;
                case VtgSentence { IsValid: true, SpeedOverGround: { } vtgSog } vtg:
                    _groundMotion = new((vtgSog, vtg.CourseOverGround), now);
                    break;
                default:
                    used = false;
                    break;
            }

            if (!used)
                return;
        }

        PublishIfDue(now);
    }

    /// <summary>
    /// Sets the host's own-ship fix: the feature is drawn at its position, and
    /// its heading and motion are used for true wind when the stream has none.
    /// <see langword="null"/> clears it.
    /// </summary>
    /// <param name="reference">The fix, or <see langword="null"/>.</param>
    public void UpdateOwnShip(OwnShipReference? reference)
    {
        ThrowIfDisposed();
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (Equals(_host, reference))
                return;
            _host = reference;
        }
        PublishIfDue(now);
    }

    /// <summary>Publishes now, ageing values at the clock's current time, whether or not the publish interval has passed.</summary>
    public void Refresh()
    {
        ThrowIfDisposed();
        Publish(_time.GetUtcNow());
    }

    /// <summary>
    /// Reads the input once to its end (a replay file), processing every line,
    /// then publishes. Doesn't retry: an input failure is thrown.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The source has no input.</exception>
    public async Task ReadToEndAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await ReadInputAsync(cancellationToken).ConfigureAwait(false);
        Refresh();
    }

    /// <summary>
    /// Starts reading the input in the background, if there is one, and the
    /// timer that publishes and ages. Calling it again does nothing.
    /// </summary>
    public void Start()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_timer is not null)
                return;
            var period = _options.PublishInterval > TimeSpan.FromMilliseconds(250) ? _options.PublishInterval : TimeSpan.FromMilliseconds(250);
            _timer = _time.CreateTimer(_ => OnTimer(), null, period, period);
            if (_input is not null)
                _loop = Task.Run(() => RunAsync(_disposeCts.Token));
        }
    }

    /// <inheritdoc />
    public DynamicFeatureDescription? Describe(DynamicFeature feature, IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return GetWeather(feature) is { } weather
            ? NmeaWeatherFormatter.Describe(weather, _time.GetUtcNow(), formatProvider ?? CultureInfo.InvariantCulture)
            : null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _disposeCts.Cancel();
        Task? loop;
        ITimer? timer;
        lock (_lock)
        {
            loop = _loop;
            timer = _timer;
            _timer = null;
        }
        if (timer is not null)
            await timer.DisposeAsync().ConfigureAwait(false);
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _disposeCts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ReadInputAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("NMEA input {Input} ended.", _input!.Description);
                Publish(_time.GetUtcNow());
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LastError = ex;
                _logger.LogWarning(ex, "NMEA input {Input} failed; retrying in {Delay}.", _input!.Description, _options.RetryDelay);
            }

            try
            {
                await Task.Delay(_options.RetryDelay, _time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReadInputAsync(CancellationToken cancellationToken)
    {
        if (_input is null)
            throw new InvalidOperationException("The source has no input; feed it with ProcessLine.");

        await foreach (var line in _input.ReadLinesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            ProcessLine(line);
        }
    }

    private void OnTimer()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        try
        {
            Publish(_time.GetUtcNow());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Publishing own-ship weather failed.");
        }
    }

    private void PublishIfDue(DateTimeOffset now)
    {
        bool due;
        lock (_lock)
            due = now - _lastPublish >= _options.PublishInterval;
        if (due)
            Publish(now);
    }

    private void Publish(DateTimeOffset now)
    {
        DynamicFeaturesChanged? change = null;
        lock (_lock)
        {
            var weather = Compute(now);
            GeoPosition? position = _host?.Position ?? (Fresh(_position, now, _options.DropAfter) ? _position!.Value.Value : null);
            IReadOnlyList<DynamicFeature> features = weather is not null && position is { } p
                ? [Project(weather, p)]
                : [];

            var old = _snapshot.Count > 0 ? _snapshot[0] : null;
            var next = features.Count > 0 ? features[0] : null;
            if (old is null && next is null)
                return;
            if (old is not null && next is not null
                && old.Coordinates[0] == next.Coordinates[0]
                && Equals(GetWeather(old), GetWeather(next)))
            {
                return;
            }

            _snapshot = features;
            _lastPublish = now;
            change = new DynamicFeaturesChanged
            {
                Kind = old is null ? DynamicSourceChangeKind.Added
                    : next is null ? DynamicSourceChangeKind.Removed
                    : DynamicSourceChangeKind.Updated,
                ChangedIds = [FeatureId],
            };
        }

        Changed?.Invoke(this, change);
    }

    private static DynamicFeature Project(OwnShipWeather weather, GeoPosition position) => new()
    {
        Id = FeatureId,
        Kind = FeatureKind,
        GeometryType = GeometryType.Point,
        Coordinates = [position],
        Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [WeatherAttribute] = weather,
        },
        LastUpdated = weather.Time,
    };

    // Must be called under _lock.
    private OwnShipWeather? Compute(DateTimeOffset now)
    {
        var drop = _options.DropAfter;
        var motionAge = _options.MaxMotionAge;

        var relative = Fresh(_relativeWind, now, drop) ? _relativeWind : null;
        var mwvTrue = Fresh(_mwvTrueWind, now, drop) ? _mwvTrueWind : null;
        var mwd = Fresh(_mwd, now, drop) ? _mwd : null;
        var water = Fresh(_waterTemperature, now, drop) ? _waterTemperature : null;
        var air = Fresh(_airTemperature, now, drop) ? _airTemperature : null;
        var pressure = Fresh(_pressure, now, drop) ? _pressure : null;
        Angle? variation = Fresh(_variation, now, drop) ? _variation!.Value.Value : null;

        Angle? heading = Fresh(_hdt, now, motionAge) ? _hdt!.Value.Value
            : Fresh(_hdgMagnetic, now, motionAge) && variation is { } v ? NmeaAngles.Normalise(_hdgMagnetic!.Value.Value + v)
            : Fresh(_vhwHeading, now, motionAge) ? _vhwHeading!.Value.Value
            : _host?.Heading;

        (Speed Speed, Angle? Course)? ground = Fresh(_groundMotion, now, motionAge) ? _groundMotion!.Value.Value
            : _host?.SpeedOverGround is { } hostSog ? (hostSog, _host.CourseOverGround)
            : null;
        Speed? stw = Fresh(_speedThroughWater, now, motionAge) ? _speedThroughWater!.Value.Value : null;

        WindVector? trueWind = null;
        var source = TrueWindSource.None;

        if (mwd is { Value: var m } && m.Speed is { } mwdSpeed
            && (m.TrueDirection ?? (m.MagneticDirection is { } mag && variation is { } var1 ? NmeaAngles.Normalise(mag + var1) : null)) is { } mwdFrom)
        {
            trueWind = new WindVector(mwdFrom, mwdSpeed);
            source = TrueWindSource.Mwd;
        }
        else if (relative is { Value: var r } && heading is { } h1 && ground is { } g
            && (g.Course ?? (g.Speed < StationarySpeed ? h1 : null)) is { } course)
        {
            trueWind = TrueWind.OverGround(r.Angle, r.Speed, h1, course, g.Speed);
            source = TrueWindSource.DerivedOverGround;
        }
        else if (mwvTrue is { Value: var t } && heading is { } h2)
        {
            trueWind = new WindVector(NmeaAngles.Normalise(h2 + t.Angle), t.Speed);
            source = TrueWindSource.MwvTrue;
        }
        else if (relative is { Value: var r2 } && heading is { } h3 && stw is { } s)
        {
            trueWind = TrueWind.ThroughWater(r2.Angle, r2.Speed, h3, s);
            source = TrueWindSource.DerivedThroughWater;
        }

        var times = new[] { relative?.At, mwvTrue?.At, mwd?.At, water?.At, air?.At, pressure?.At }
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .ToArray();
        if (times.Length == 0)
            return null;

        var weather = new OwnShipWeather
        {
            Time = times.Max(),
            TrueWind = trueWind,
            TrueWindSource = source,
            RelativeWindAngle = relative?.Value.Angle,
            RelativeWindSpeed = relative?.Value.Speed,
            RelativeWindFromDirection = relative is { } rel && heading is { } h4 ? NmeaAngles.Normalise(h4 + rel.Value.Angle) : null,
            Heading = heading,
            WaterTemperatureCelsius = water?.Value,
            AirTemperatureCelsius = air?.Value,
            PressureHectopascals = pressure?.Value,
        };
        return weather.HasAnyValue ? weather : null;
    }

    private static bool Fresh<T>(Stamped<T>? value, DateTimeOffset now, TimeSpan maxAge) =>
        value is { } v && now - v.At <= maxAge;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private readonly record struct Stamped<T>(T Value, DateTimeOffset At);
}
