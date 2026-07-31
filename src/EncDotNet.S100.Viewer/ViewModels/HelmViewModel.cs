using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View-model for the Helm panel — the on-screen bridge to
/// <see cref="IOwnShipHelm"/> that lets the user steer the simulated
/// own-ship (course, speed, rate of turn, hold/resume) and shows a live
/// readout of the resulting fix. The panel is only surfaced while
/// own-vessel tracking is enabled.
/// </summary>
/// <remarks>
/// <para>
/// The editable course/speed/turn-rate fields and the hold toggle are
/// two-way bound. A user edit calls the matching <see cref="IOwnShipHelm"/>
/// method; programmatic refreshes from the live fix are guarded by
/// <see cref="_suppressHelmWrite"/> so re-seeding the fields never issues a
/// helm command (which would otherwise feed back on itself).
/// </para>
/// <para>
/// <see cref="IOwnShipPositionProvider.Updated"/> may fire on any thread;
/// the handler only flips an atomic dirty flag and a UI-thread
/// <see cref="DispatcherTimer"/> pumps <see cref="Refresh"/>, mirroring
/// <see cref="VesselListViewModel"/>. With no Avalonia application running
/// (unit tests) the timer is not created and tests drive
/// <see cref="Refresh"/> explicitly.
/// </para>
/// </remarks>
internal sealed class HelmViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Course step (degrees) applied by the port/starboard nudge commands.</summary>
    private const double CourseNudgeDeg = 5.0;

    private readonly IOwnShipPositionProvider _provider;
    private readonly IOwnShipHelm _helm;
    private readonly IOwnShipHelmState _state;
    private readonly DispatcherTimer? _timer;

    private int _dirty;
    private bool _suppressHelmWrite;
    private bool _disposed;

    private double _courseDeg;
    private double _speedMs;
    private double _turnRateDegPerSec;
    private bool _isHeld;
    private string _positionText = LatLonFormatter.Placeholder;
    private string _courseText = string.Empty;
    private string _speedText = string.Empty;
    private string _headingText = string.Empty;

    public HelmViewModel(
        IOwnShipPositionProvider provider,
        IOwnShipHelm helm,
        IOwnShipHelmState state)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(helm);
        ArgumentNullException.ThrowIfNull(state);

        _provider = provider;
        _helm = helm;
        _state = state;

        HoldResumeCommand = new RelayCommand(() => IsHeld = !IsHeld);
        SteadyCommand = new RelayCommand(() => TurnRateDegPerSec = 0.0);
        PortCommand = new RelayCommand(() => _helm.NudgeCourse(-CourseNudgeDeg));
        StarboardCommand = new RelayCommand(() => _helm.NudgeCourse(CourseNudgeDeg));

        _provider.Updated += OnProviderUpdated;

        if (Application.Current is not null)
        {
            _timer = new DispatcherTimer { Interval = RefreshInterval };
            _timer.Tick += (_, _) =>
            {
                if (Interlocked.Exchange(ref _dirty, 0) == 1)
                {
                    Refresh();
                }
            };
            _timer.Start();
        }

        Refresh();
    }

    /// <summary>Ordered course over ground in degrees true [0, 360).</summary>
    public double CourseDeg
    {
        get => _courseDeg;
        set
        {
            if (SetProperty(ref _courseDeg, value) && !_suppressHelmWrite)
            {
                _helm.SetCourse(value);
            }
        }
    }

    /// <summary>Ordered speed over ground in metres per second (>= 0).</summary>
    public double SpeedMs
    {
        get => _speedMs;
        set
        {
            if (SetProperty(ref _speedMs, value) && !_suppressHelmWrite)
            {
                _helm.SetSpeed(value);
            }
        }
    }

    /// <summary>Ordered rate of turn in degrees per second (negative = port).</summary>
    public double TurnRateDegPerSec
    {
        get => _turnRateDegPerSec;
        set
        {
            if (SetProperty(ref _turnRateDegPerSec, value) && !_suppressHelmWrite)
            {
                _helm.SetTurnRate(value);
            }
        }
    }

    /// <summary>Whether the vessel is currently held (stopped, ready to resume).</summary>
    public bool IsHeld
    {
        get => _isHeld;
        set
        {
            if (SetProperty(ref _isHeld, value) && !_suppressHelmWrite)
            {
                if (value)
                {
                    _helm.Hold();
                }
                else
                {
                    _helm.Resume();
                }
            }
        }
    }

    /// <summary>Live position readout in degrees-decimal-minutes.</summary>
    public string PositionText
    {
        get => _positionText;
        private set => SetProperty(ref _positionText, value);
    }

    /// <summary>Live course-over-ground readout, e.g. <c>"090.0°T"</c>.</summary>
    public string CourseText
    {
        get => _courseText;
        private set => SetProperty(ref _courseText, value);
    }

    /// <summary>Live speed readout, e.g. <c>"9.7 kn"</c>.</summary>
    public string SpeedText
    {
        get => _speedText;
        private set => SetProperty(ref _speedText, value);
    }

    /// <summary>Live heading readout, e.g. <c>"088.0°T"</c>.</summary>
    public string HeadingText
    {
        get => _headingText;
        private set => SetProperty(ref _headingText, value);
    }

    public ICommand HoldResumeCommand { get; }
    public ICommand SteadyCommand { get; }
    public ICommand PortCommand { get; }
    public ICommand StarboardCommand { get; }

    private void OnProviderUpdated(object? sender, OwnShipPosition position)
    {
        Interlocked.Exchange(ref _dirty, 1);

        // No dispatcher timer (headless tests) — refresh inline so the
        // readout and field state still track the provider.
        if (_timer is null)
        {
            Refresh();
        }
    }

    /// <summary>
    /// Re-reads the live fix and helm state into the bound properties.
    /// Runs on the UI thread (timer tick) in the running app. Guards the
    /// editable-field writes so seeding does not re-issue helm commands.
    /// </summary>
    internal void Refresh()
    {
        Interlocked.Exchange(ref _dirty, 0);

        var fix = _provider.Current;
        var commandedSpeed = _state.CommandedSpeedMs;
        var turnRate = _state.TurnRateDegPerSec;
        var held = _state.IsHeld;

        _suppressHelmWrite = true;
        try
        {
            if (fix is not null)
            {
                var cog = fix.CourseOverGround?.TotalDegrees ?? 0.0;
                CourseDeg = Math.Round(cog, 1);
                PositionText = LatLonFormatter.Format(fix.Latitude, fix.Longitude);
                CourseText = FormatBearing(cog);
                SpeedText = FormatSpeed(fix.SpeedOverGround?.TotalMetresPerSecond ?? 0.0);
                HeadingText = FormatBearing(fix.Heading?.TotalDegrees ?? cog);
            }

            SpeedMs = Math.Round(commandedSpeed, 2);
            TurnRateDegPerSec = Math.Round(turnRate, 2);
            IsHeld = held;
        }
        finally
        {
            _suppressHelmWrite = false;
        }
    }

    private static string FormatBearing(double degrees)
        => string.Format(
            CultureInfo.InvariantCulture,
            Strings.Helm_BearingFormat,
            SteerableOwnShipPositionProvider.Normalize360(degrees));

    private static string FormatSpeed(double metresPerSecond)
        => string.Format(
            CultureInfo.InvariantCulture,
            Strings.Helm_SpeedFormat,
            metresPerSecond * 1.943_844_492); // m/s -> knots

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _provider.Updated -= OnProviderUpdated;
        _timer?.Stop();
    }
}
