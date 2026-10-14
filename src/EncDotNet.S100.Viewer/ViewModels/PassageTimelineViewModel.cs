using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Timeline's passage lanes and Follow passage (#904, handoff E1–E3),
/// projected from <see cref="PassageCheckService"/> onto the Timeline's axis.
/// </summary>
/// <remarks>
/// The lanes show only while Check passage is on and the passage has ETAs
/// (every leg has a speed): that is when departure, arrival and the window
/// mean something, and it keeps the Timeline uncluttered while routes are
/// edited. Positions are fractions of the Timeline's (gap-collapsing) axis,
/// clamped to its ends.
/// </remarks>
internal sealed class PassageTimelineViewModel : ViewModelBase
{
    private readonly PassageCheckService _check;
    private readonly FollowPassageService? _follow;
    private readonly Func<TimelineAxisMap?> _axis;
    private readonly Func<TimeFormat> _timeFormat;
    private readonly DepartureDrag _drag;
    private DepartureBand? _band;
    private TimelineAxisMap? _segmentsAxis;

    public PassageTimelineViewModel(
        PassageCheckService check,
        FollowPassageService? follow,
        Func<TimelineAxisMap?> axis,
        Func<TimeFormat> timeFormat)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(axis);
        ArgumentNullException.ThrowIfNull(timeFormat);
        _check = check;
        _follow = follow;
        _axis = axis;
        _timeFormat = timeFormat;
        _drag = new DepartureDrag(check);
        ToggleFollowCommand = new RelayCommand(() => _follow?.SetOn(!IsFollowing), () => IsFollowing || CanFollow);
        _check.Changed += (_, _) => Refresh();
        if (_follow is not null)
            _follow.Changed += (_, _) => RefreshFollow();
        Refresh();
    }

    // ── Passage lane (E1) ───────────────────────────────────────────

    private bool _hasLanes;
    /// <summary>Whether the passage lanes show: Check passage is on and the passage has ETAs.</summary>
    public bool HasLanes
    {
        get => _hasLanes;
        private set => SetProperty(ref _hasLanes, value);
    }

    private bool _isStale;
    /// <summary>Whether the lanes are being recomputed (dimmed).</summary>
    public bool IsStale
    {
        get => _isStale;
        private set
        {
            if (SetProperty(ref _isStale, value))
                OnPropertyChanged(nameof(Opacity));
        }
    }

    public double Opacity => IsStale ? 0.45 : 1.0;

    private string? _passageSub;
    /// <summary>"dep 07:10 · arr 08:46".</summary>
    public string? PassageSub
    {
        get => _passageSub;
        private set => SetProperty(ref _passageSub, value);
    }

    private double _passageStart = double.NaN;
    /// <summary>Departure on the axis, 0–1.</summary>
    public double PassageStart
    {
        get => _passageStart;
        private set => SetProperty(ref _passageStart, value);
    }

    private double _passageEnd = double.NaN;
    /// <summary>Arrival on the axis, 0–1.</summary>
    public double PassageEnd
    {
        get => _passageEnd;
        private set => SetProperty(ref _passageEnd, value);
    }

    private double _minimumPosition = double.NaN;
    /// <summary>The min-UKC time on the axis, 0–1; NaN without one.</summary>
    public double MinimumPosition
    {
        get => _minimumPosition;
        private set => SetProperty(ref _minimumPosition, value);
    }

    private CheckTone _tone = CheckTone.Neutral;
    /// <summary>The verdict's tone, for the min-UKC tick.</summary>
    public CheckTone Tone
    {
        get => _tone;
        private set
        {
            if (!SetProperty(ref _tone, value))
                return;
            OnPropertyChanged(nameof(IsSuccess));
            OnPropertyChanged(nameof(IsWarning));
            OnPropertyChanged(nameof(IsDestructive));
            OnPropertyChanged(nameof(IsNeutral));
        }
    }

    public bool IsSuccess => Tone == CheckTone.Success;

    public bool IsWarning => Tone == CheckTone.Warning;

    public bool IsDestructive => Tone == CheckTone.Destructive;

    public bool IsNeutral => Tone == CheckTone.Neutral;

    /// <summary>The checked departure, or <c>null</c> without lanes.</summary>
    public DateTimeOffset? Departure { get; private set; }

    /// <summary>The arrival, or <c>null</c> without lanes.</summary>
    public DateTimeOffset? Arrival { get; private set; }

    /// <summary>The ETA of the minimum UKC, or <c>null</c>.</summary>
    public DateTimeOffset? MinimumTime { get; private set; }

    /// <summary>The verdict, or <c>null</c> without lanes.</summary>
    public Navigation.PassageVerdict? Verdict { get; private set; }

    private string? _passageTooltip;
    /// <summary>The passage lane's tooltip: the times and the verdict.</summary>
    public string? PassageTooltip
    {
        get => _passageTooltip;
        private set => SetProperty(ref _passageTooltip, value);
    }

    // ── Departure window lane (E2) ──────────────────────────────────

    private bool _hasWindow;
    /// <summary>Whether the window lane shows (the window has been swept).</summary>
    public bool HasWindow
    {
        get => _hasWindow;
        private set => SetProperty(ref _hasWindow, value);
    }

    private IReadOnlyList<DepartureBandSegment> _windowSegments = [];
    /// <summary>The window's runs, as positions on the Timeline's axis.</summary>
    public IReadOnlyList<DepartureBandSegment> WindowSegments
    {
        get => _windowSegments;
        private set => SetProperty(ref _windowSegments, value);
    }

    private double? _departurePosition;
    /// <summary>The departure tick on the axis, 0–1.</summary>
    public double? DeparturePosition
    {
        get => _departurePosition;
        private set => SetProperty(ref _departurePosition, value);
    }

    private string? _windowSub;
    /// <summary>The window's runs, e.g. "06:00–06:40, 08:25–10:20", or "None meet your settings".</summary>
    public string? WindowSub
    {
        get => _windowSub;
        private set => SetProperty(ref _windowSub, value);
    }

    /// <summary>The band model, or <c>null</c> without a window.</summary>
    public DepartureBand? Band => _band;

    // ── Follow passage (E3) ─────────────────────────────────────────

    /// <summary>Turns Follow passage on or off.</summary>
    public ICommand ToggleFollowCommand { get; }

    private bool _isFollowing;
    /// <summary>Whether Follow passage is on.</summary>
    public bool IsFollowing
    {
        get => _isFollowing;
        private set => SetProperty(ref _isFollowing, value);
    }

    private bool _canFollow;
    /// <summary>Whether Follow passage can be turned on.</summary>
    public bool CanFollow
    {
        get => _canFollow;
        private set => SetProperty(ref _canFollow, value);
    }

    /// <summary>Whether the Follow passage toggle shows (the lanes do).</summary>
    public bool ShowFollow => HasLanes && _follow is not null;

    // ── Dragging the window lane ────────────────────────────────────

    /// <summary>Starts a departure drag at an axis position: one undo step until <see cref="EndDrag"/>.</summary>
    public void BeginDrag(double position)
    {
        if (_band is { } band && TimeAt(position) is { } t)
            _drag.Begin(band, t);
    }

    /// <summary>Moves the departure to the swept departure nearest an axis position.</summary>
    public void DragTo(double position)
    {
        if (_band is { } band && TimeAt(position) is { } t)
            _drag.MoveTo(band, t);
    }

    /// <summary>Ends the drag.</summary>
    public void EndDrag() => _drag.End();

    /// <summary>Moves the departure by a number of swept departures.</summary>
    public void Nudge(int steps)
    {
        if (_band is { } band)
            _drag.Nudge(band, steps);
    }

    private DateTimeOffset? TimeAt(double position) =>
        _axis() is { } axis && double.IsFinite(position)
            ? new DateTimeOffset(DateTime.SpecifyKind(axis.ToTime(Math.Clamp(position, 0, 1)), DateTimeKind.Utc))
            : null;

    /// <summary>Re-reads the check and the axis.</summary>
    public void Refresh()
    {
        var current = _check.IsActive ? _check.Current : null;
        var axis = _axis();
        if (current is not { Result.Arrival: { } arrival } || axis is null)
        {
            HasLanes = false;
            HasWindow = false;
            Departure = Arrival = MinimumTime = null;
            Verdict = null;
            _band = null;
            _segmentsAxis = null;
            WindowSegments = [];
            DeparturePosition = null;
            OnPropertyChanged(nameof(ShowFollow));
            RefreshFollow();
            return;
        }

        var result = current.Result;
        var format = _timeFormat();
        var departure = _check.Departure ?? result.Departure;
        HasLanes = true;
        IsStale = _check.IsStale;
        Departure = result.Departure;
        Arrival = arrival;
        MinimumTime = result.Minimum is { HasEta: true } minimum ? minimum.Time : null;
        Verdict = result.Verdict;
        Tone = PassageCheckViewModel.ToneOf(result.Verdict);
        PassageStart = Position(axis, result.Departure);
        PassageEnd = Position(axis, arrival);
        MinimumPosition = result.Minimum is { HasEta: true } min ? Position(axis, min.Time) : double.NaN;
        var dep = string.Format(CultureInfo.CurrentCulture, Strings.Check_DepartureFormat, PassageFormatting.FormatEta(result.Departure, result.Departure, format));
        var arr = string.Format(CultureInfo.CurrentCulture, Strings.Check_ArrivalFormat, PassageFormatting.FormatEta(arrival, result.Departure, format));
        PassageSub = dep + " · " + arr;
        PassageTooltip = PassageSub + "\n" + PassageCheckViewModel.VerdictTitle(result);

        var window = _check.Window is { } w && w.RouteId == current.RouteId ? w : null;
        HasWindow = window is not null;
        if (window is null)
        {
            _band = null;
            _segmentsAxis = null;
            WindowSegments = [];
            DeparturePosition = null;
            WindowSub = null;
        }
        else
        {
            // The runs move only with a new sweep or a new axis, not with the departure.
            if (_band?.Window != window || !ReferenceEquals(axis, _segmentsAxis))
            {
                _band = _band?.Window == window ? _band : DepartureBand.Build(window);
                _segmentsAxis = axis;
                var half = TimeSpan.FromTicks(_band.Step.Ticks / 2);
                WindowSegments = [.. _band.Segments.Select(s => s with
                {
                    Start = Position(axis, s.First - half),
                    End = Position(axis, s.Last + half),
                })];
            }

            DeparturePosition = Position(axis, departure);
            WindowSub = window.Windows.Count == 0
                ? Strings.Timeline_PassageWindowNone
                : DepartureWindowViewModel.Ranges(window, result.Departure, format);
        }

        OnPropertyChanged(nameof(ShowFollow));
        RefreshFollow();
    }

    private void RefreshFollow()
    {
        IsFollowing = _follow?.IsOn ?? false;
        CanFollow = _follow?.CanFollow ?? false;
        ((RelayCommand)ToggleFollowCommand).NotifyCanExecuteChanged();
    }

    private static double Position(TimelineAxisMap axis, DateTimeOffset time) =>
        Math.Clamp(axis.ToPosition(time.UtcDateTime), 0, 1);
}
