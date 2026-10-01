using System.Globalization;
using System.Windows.Input;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View-model backing the bottom timeline panel. Exposes
/// slider-friendly bindings (long-tick representations of
/// <see cref="DateTime"/>) over the underlying
/// <see cref="GlobalTimeService"/> and forwards user scrubs back to
/// the service via <see cref="GlobalTimeService.SetCurrentTime"/>.
/// </summary>
internal sealed class TimelineViewModel : ViewModelBase, EncDotNet.S100.Viewer.ViewModels.Activities.IActivityTabContentSignal
{
    private readonly GlobalTimeService _service;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly TimeProvider _time;
    private readonly ITimer? _clock;
    private TimelineAxisMap? _axis;
    private DateTime? _lastCurrent;

    public TimelineViewModel(GlobalTimeService service)
        : this(service, timeFormat: null)
    {
    }

    public TimelineViewModel(GlobalTimeService service, ITimeFormatProvider? timeFormat)
        : this(service, timeFormat, TimeProvider.System)
    {
    }

    public TimelineViewModel(GlobalTimeService service, ITimeFormatProvider? timeFormat, TimeProvider time)
        : this(service, timeFormat, time, PostToUiThread)
    {
    }

    internal TimelineViewModel(GlobalTimeService service, ITimeFormatProvider? timeFormat, TimeProvider time, Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(dispatch);
        _service = service;
        _timeFormat = timeFormat;
        _time = time;

        PreviousStepCommand = new RelayCommand(StepPrevious, CanStepPrevious);
        NextStepCommand = new RelayCommand(StepNext, CanStepNext);
        NowCommand = new RelayCommand(GoToNow, () => IsNowInCoverage);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());

        _service.RangeChanged += OnRangeChanged;
        _service.CurrentTimeChanged += _ =>
        {
            _lastCurrent = _service.CurrentTime;
            OnPropertyChanged(nameof(SliderValue));
            OnPropertyChanged(nameof(CurrentTimeLabel));
            ((RelayCommand)PreviousStepCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextStepCommand).NotifyCanExecuteChanged();
        };

        // A forecast ages by the minute: the Now marker, the readout's
        // "forecast ended" and the Now button follow the clock (#685), and so
        // does the view time while it follows now (#706).
        _clock = _time.CreateTimer(_ => dispatch(OnClockTick), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        if (_timeFormat is not null)
        {
            _timeFormat.TimeFormatChanged += _ =>
            {
                OnPropertyChanged(nameof(CurrentTimeLabel));
                OnPropertyChanged(nameof(RangeLabel));
            };
        }
    }

    /// <summary>
    /// Raised when the user activates <see cref="CloseCommand"/>.
    /// <see cref="MainViewModel"/> subscribes to this and clears its
    /// <c>IsTimelineVisible</c> flag so the user can re-open the
    /// panel from the View menu.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Closes the timeline panel via <see cref="CloseRequested"/>.
    /// </summary>
    public ICommand CloseCommand { get; }

    private bool _wasActive;

    private void OnRangeChanged()
    {
        var previous = _lastCurrent;
        // Rebuild the gap-collapsing axis from the new aggregate range and
        // coverage segments before notifying slider/band bindings.
        _axis = _service.MinTime is { } min && _service.MaxTime is { } max
            ? new TimelineAxisMap(min, max, _service.CoverageSegments)
            : null;

        var nowActive = _service.IsActive;
        var becameActive = nowActive && !_wasActive;
        _wasActive = nowActive;

        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(SliderMinimum));
        OnPropertyChanged(nameof(SliderMaximum));
        OnPropertyChanged(nameof(SliderValue));
        OnPropertyChanged(nameof(RangeLabel));
        OnPropertyChanged(nameof(CurrentTimeLabel));
        OnPropertyChanged(nameof(Ticks));
        OnPropertyChanged(nameof(IsSnapToTickEnabled));
        OnPropertyChanged(nameof(TickFrequency));
        OnPropertyChanged(nameof(AreStepButtonsVisible));
        OnPropertyChanged(nameof(CoverageBands));
        ((RelayCommand)PreviousStepCommand).NotifyCanExecuteChanged();
        ((RelayCommand)NextStepCommand).NotifyCanExecuteChanged();
        RaiseNow();

        // A forecast starts at Now, not at its first step (D6: step one of a
        // 12:00Z run is already hours old); when a run is replaced, the chosen
        // time is kept if the new run covers it, else the clock moves to Now (D5).
        // While following now, a new or replaced run keeps following (#706).
        if (IsForecastTimeline && IsNowInCoverage
            && (becameActive || _service.IsFollowingNow || (previous is { } kept && !IsCovered(kept))))
        {
            GoToNow();
        }

        _lastCurrent = _service.CurrentTime;

        if (becameActive)
        {
            // false→true transition: signal that the Timeline dock should
            // auto-open (PR-M4). Re-arming happens automatically because
            // we only fire when crossing the boundary.
            ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public event EventHandler? ContentBecameAvailable;

    /// <summary>
    /// Steps backward to the previous discrete sample. Only
    /// available when <see cref="AreStepButtonsVisible"/> is true.
    /// </summary>
    public ICommand PreviousStepCommand { get; }

    /// <summary>Steps forward to the next discrete sample.</summary>
    public ICommand NextStepCommand { get; }

    /// <summary>
    /// True when discrete prev/next step controls should be shown — i.e.
    /// whenever the timeline has at least one sample. Stepping is always
    /// well-defined (it walks <see cref="GlobalTimeService.AllSamples"/>,
    /// however many there are), and is especially useful for dense,
    /// clustered datasets where the free-running slider cannot land on an
    /// exact sample.
    /// </summary>
    public bool AreStepButtonsVisible => _service.AllSamples.Count > 0;

    private bool CanStepPrevious()
    {
        var samples = _service.AllSamples;
        return _service.CurrentTime is { } cur && samples.Count > 0 && cur > samples[0];
    }

    private bool CanStepNext()
    {
        var samples = _service.AllSamples;
        return _service.CurrentTime is { } cur && samples.Count > 0 && cur < samples[^1];
    }

    private void StepPrevious()
    {
        var samples = _service.AllSamples;
        if (_service.CurrentTime is not { } cur || samples.Count == 0) return;
        // Largest sample strictly less than current.
        DateTime? target = null;
        foreach (var s in samples)
            if (s < cur && (target is null || s > target.Value)) target = s;
        if (target is { } t) _service.SetCurrentTime(t);
    }

    private void StepNext()
    {
        var samples = _service.AllSamples;
        if (_service.CurrentTime is not { } cur || samples.Count == 0) return;
        // Smallest sample strictly greater than current.
        DateTime? target = null;
        foreach (var s in samples)
            if (s > cur && (target is null || s < target.Value)) target = s;
        if (target is { } t) _service.SetCurrentTime(t);
    }

    /// <summary>
    /// Maximum number of distinct samples for which we still render
    /// one tick per real sample. Beyond this threshold we fall back
    /// to <see cref="EvenlySpacedTickCount"/> evenly distributed
    /// stoppers between <see cref="SliderMinimum"/> and
    /// <see cref="SliderMaximum"/>.
    /// </summary>
    private const int SampleTickThreshold = 50;

    /// <summary>
    /// Number of evenly-spaced ticks rendered when the dataset
    /// timelines are dense and/or unaligned.
    /// </summary>
    private const int EvenlySpacedTickCount = 10;

    /// <summary>
    /// Tick stops painted along the slider, in normalized <c>[0,1]</c>
    /// axis positions. When all loaded datasets share a small set of
    /// timestamps, ticks correspond 1:1 to real sample times (mapped
    /// through the gap-collapsing axis) and the slider snaps to them.
    /// Otherwise, ticks are evenly spaced visual landmarks and the
    /// slider runs free (each adapter still snaps the value to its
    /// nearest real sample at render time).
    /// </summary>
    public AvaloniaList<double> Ticks
    {
        get
        {
            var samples = _service.AllSamples;
            var list = new AvaloniaList<double>();
            if (samples.Count == 0) return list;

            if (samples.Count <= SampleTickThreshold)
            {
                var axis = Axis;
                if (axis is not null)
                    foreach (var s in samples) list.Add(axis.ToPosition(s));
            }
            else
            {
                for (var i = 0; i <= EvenlySpacedTickCount; i++)
                    list.Add(i / (double)EvenlySpacedTickCount);
            }
            return list;
        }
    }

    /// <summary>
    /// Spacing between minor ticks in normalized axis units. Mirrors the
    /// even-spacing stride when the timeline is dense; <c>0</c> when the
    /// slider snaps to the explicit per-sample <see cref="Ticks"/>.
    /// </summary>
    public double TickFrequency
    {
        get
        {
            var samples = _service.AllSamples;
            if (samples.Count == 0) return 0;
            if (samples.Count <= SampleTickThreshold) return 0;
            return 1.0 / EvenlySpacedTickCount;
        }
    }

    /// <summary>
    /// Snap the slider value to a tick only when ticks correspond
    /// to real samples; otherwise let the user scrub freely and
    /// rely on per-dataset adapters to snap at render time.
    /// </summary>
    public bool IsSnapToTickEnabled =>
        _service.AllSamples.Count is > 0 and <= SampleTickThreshold;

    /// <summary>True when the timeline panel should be visible.</summary>
    public bool IsActive => _service.IsActive;

    /// <summary>
    /// The gap-collapsing axis map for the current aggregate range, built
    /// lazily so property getters invoked before the first
    /// <see cref="OnRangeChanged"/> still resolve correctly.
    /// </summary>
    private TimelineAxisMap? Axis
    {
        get
        {
            if (_axis is null && _service.MinTime is { } min && _service.MaxTime is { } max)
                _axis = new TimelineAxisMap(min, max, _service.CoverageSegments);
            return _axis;
        }
    }

    /// <summary>
    /// Data-coverage ranges expressed as fractions of the slider extent
    /// (<c>[0,1]</c> on the gap-collapsing axis). The view paints each as a
    /// filled band so the user can see which parts of the timeline have data
    /// and which are empty (the compressed gaps). Empty when the range is
    /// degenerate or no dataset is loaded.
    /// </summary>
    public IReadOnlyList<NormalizedCoverageBand> CoverageBands =>
        Axis?.CoverageBands ?? Array.Empty<NormalizedCoverageBand>();

    /// <summary>Minimum slider value — the normalized axis always starts at 0.</summary>
    public double SliderMinimum => 0d;

    /// <summary>Maximum slider value — the normalized axis always ends at 1.</summary>
    public double SliderMaximum => 1d;

    /// <summary>
    /// Two-way slider value as a normalized <c>[0,1]</c> position on the
    /// gap-collapsing axis. The getter maps <see cref="GlobalTimeService.CurrentTime"/>
    /// through the axis; the setter maps the position back to a wall-clock
    /// time and pushes it through <see cref="GlobalTimeService.SetCurrentTime"/>,
    /// after which the loader debounces and fans the change out to every
    /// registered dataset.
    /// </summary>
    public double SliderValue
    {
        get => _service.CurrentTime is { } t && Axis is { } axis ? axis.ToPosition(t) : 0d;
        set
        {
            if (Axis is not { } axis) return;
            // The slider echoing the position it was given is not a user's
            // choice of time and must not stop following now.
            if (_service.CurrentTime is { } current && axis.ToPosition(current) == value) return;
            _service.SetCurrentTime(axis.ToTime(value));
        }
    }

    /// <summary>
    /// Jumps to the step nearest the current time (or, when the slider runs
    /// free, to the current time itself), then follows now as the clock
    /// advances until the user picks a time (#706). Available while now lies
    /// inside a loaded window (D2).
    /// </summary>
    public ICommand NowCommand { get; }

    /// <summary>
    /// True when the timeline spans forecast runs (a loaded dataset's name
    /// carries a run time, e.g. <c>111US00_CBOFS_20260930T18Z_…</c>): the Now
    /// marker, the Now button and run names are shown (#685).
    /// </summary>
    public bool IsForecastTimeline => Runs.Count > 0;

    /// <summary>The forecast runs loaded, by model, e.g. "cbofs 12:00Z".</summary>
    public IReadOnlyList<string> Runs => _service.TimedDatasets
        .Select(d => ForecastRunNames.Describe(d.Name))
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>True when now lies inside a loaded window (a coverage segment).</summary>
    public bool IsNowInCoverage => IsForecastTimeline && IsCovered(Now);

    private bool IsCovered(DateTime time) =>
        _service.CoverageSegments.Count > 0
            ? _service.CoverageSegments.Any(s => time >= s.Start && time <= s.End)
            : _service.MinTime is { } min && _service.MaxTime is { } max && time >= min && time <= max;

    /// <summary>True when the Now marker is drawn on the axis (now lies within the range).</summary>
    public bool IsNowInRange =>
        IsForecastTimeline && _service.MinTime is { } min && _service.MaxTime is { } max && Now >= min && Now <= max;

    /// <summary>The Now marker's position on the axis (0–1); NaN when not drawn.</summary>
    public double NowPosition => IsNowInRange && Axis is { } axis ? axis.ToPosition(Now) : double.NaN;

    /// <summary>True when now lies after every loaded window: every loaded forecast has ended (D3).</summary>
    public bool IsForecastEnded => IsForecastTimeline && _service.MaxTime is { } max && Now > max;

    /// <summary>True when now lies before the loaded range (data from the future, e.g. a run not yet valid).</summary>
    public bool IsNowBeforeRange => IsForecastTimeline && _service.MinTime is { } min && Now < min;

    /// <summary>"NOW 3 H LATER ›" (past the range) or "‹ NOW 2 H EARLIER" (before it); empty when in range.</summary>
    public string NowEdgeLabel =>
        IsForecastEnded && _service.MaxTime is { } max
            ? string.Format(CultureInfo.CurrentCulture, Strings.TimelinePanel_NowLaterFormat, Duration(Now - max)).ToUpper(CultureInfo.CurrentCulture)
        : IsNowBeforeRange && _service.MinTime is { } min
            ? string.Format(CultureInfo.CurrentCulture, Strings.TimelinePanel_NowEarlierFormat, Duration(min - Now)).ToUpper(CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>"forecast ended 3 h ago", after the readout, once every loaded run has ended.</summary>
    public string ForecastEndedText => IsForecastEnded && _service.MaxTime is { } max
        ? string.Format(CultureInfo.CurrentCulture, Strings.TimelinePanel_ForecastEndedFormat, Duration(Now - max))
        : string.Empty;

    /// <summary>The coverage band's opacity: dimmed once every loaded forecast has ended.</summary>
    public double BandOpacity => IsForecastEnded ? 0.45 : 1.0;

    /// <summary>
    /// Moves the view time to now (the step nearest it while the slider snaps
    /// to steps) and follows now from then on, until the user picks a time.
    /// </summary>
    private void GoToNow()
    {
        var now = Now;
        var samples = _service.AllSamples;
        if (IsSnapToTickEnabled && samples.Count > 0)
            now = samples.MinBy(s => Math.Abs((s - now).Ticks));
        _service.FollowNow(now);
    }

    private void OnClockTick()
    {
        if (_service.IsFollowingNow && IsNowInCoverage)
            GoToNow();
        RaiseNow();
    }

    private void RaiseNow()
    {
        OnPropertyChanged(nameof(IsForecastTimeline));
        OnPropertyChanged(nameof(IsNowInRange));
        OnPropertyChanged(nameof(NowPosition));
        OnPropertyChanged(nameof(IsForecastEnded));
        OnPropertyChanged(nameof(IsNowBeforeRange));
        OnPropertyChanged(nameof(NowEdgeLabel));
        OnPropertyChanged(nameof(ForecastEndedText));
        OnPropertyChanged(nameof(BandOpacity));
        OnPropertyChanged(nameof(RangeLabel));
        ((RelayCommand)NowCommand).NotifyCanExecuteChanged();
    }

    /// <summary>"3 h", "2 d", "45 min".</summary>
    private static string Duration(TimeSpan span)
    {
        var c = CultureInfo.CurrentCulture;
        return span.TotalHours >= 72 ? string.Format(c, Strings.TimelinePanel_DaysFormat, (int)span.TotalDays)
            : span.TotalHours >= 1 ? string.Format(c, Strings.TimelinePanel_HoursFormat, (int)span.TotalHours)
            : string.Format(c, Strings.TimelinePanel_MinutesFormat, Math.Max(1, (int)span.TotalMinutes));
    }

    /// <summary>"hourly", "every 3 h", "every 10 min": the most common step between samples.</summary>
    private string? StepText()
    {
        var samples = _service.AllSamples;
        if (samples.Count < 2)
            return null;
        var step = samples.Zip(samples.Skip(1), (a, b) => b - a)
            .GroupBy(d => d).OrderByDescending(g => g.Count()).First().Key;
        var c = CultureInfo.CurrentCulture;
        return step == TimeSpan.FromHours(1) ? Strings.TimelinePanel_Hourly
            : step.TotalHours >= 1 && step.TotalHours == Math.Floor(step.TotalHours)
                ? string.Format(c, Strings.TimelinePanel_EveryHoursFormat, (int)step.TotalHours)
            : string.Format(c, Strings.TimelinePanel_EveryMinutesFormat, Math.Max(1, (int)step.TotalMinutes));
    }

    private static void PostToUiThread(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            action();
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    /// <summary>Display text for the currently selected time, formatted via <see cref="TimeFormatting"/>.</summary>
    public string CurrentTimeLabel =>
        _service.CurrentTime is { } t
            ? TimeFormatting.Format(t, ActiveFormat)
            : string.Empty;

    /// <summary>"N steps from T0 to T1"-style summary of the timeline.</summary>
    public string RangeLabel
    {
        get
        {
            var samples = _service.AllSamples;
            if (samples.Count == 0 || _service.MinTime is null || _service.MaxTime is null)
                return Strings.TimelinePanel_NoData;
            var fmt = ActiveFormat;
            if (IsForecastTimeline)
            {
                // D4: "30.09 12:00 → 02.10 18:00 UTC · 55 h · cbofs 12:00Z, nyofs 18:00Z · hourly".
                var runs = Runs;
                var parts = new List<string>(5)
                {
                    $"{TimeFormatting.Format(_service.MinTime.Value, fmt)} → {TimeFormatting.Format(_service.MaxTime.Value, fmt)}",
                    Duration(_service.MaxTime.Value - _service.MinTime.Value),
                    runs.Count <= 3
                        ? string.Join(", ", runs)
                        : string.Format(CultureInfo.CurrentCulture, Strings.Library_AndMoreFormat, string.Join(", ", runs.Take(2)), runs.Count - 2),
                };
                if (StepText() is { } step)
                    parts.Add(step);
                if (IsForecastEnded)
                    parts.Add(Strings.TimelinePanel_RefreshForRuns);
                return string.Join(" · ", parts);
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.TimelinePanel_Range,
                samples.Count,
                TimeFormatting.Format(_service.MinTime.Value, fmt),
                TimeFormatting.Format(_service.MaxTime.Value, fmt));
        }
    }

    private TimeFormat ActiveFormat => _timeFormat?.Current ?? TimeFormat.Local;
}

/// <summary>
/// A data-coverage band normalized to the slider extent: <see cref="Start"/>
/// and <see cref="Width"/> are fractions in <c>[0,1]</c> of
/// <see cref="TimelineViewModel.SliderMinimum"/>..<see cref="TimelineViewModel.SliderMaximum"/>.
/// </summary>
internal readonly record struct NormalizedCoverageBand(double Start, double Width);
