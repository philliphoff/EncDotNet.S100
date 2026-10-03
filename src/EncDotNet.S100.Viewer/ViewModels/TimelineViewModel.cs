using System.Globalization;
using System.Windows.Input;
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
internal sealed partial class TimelineViewModel : ViewModelBase, EncDotNet.S100.Viewer.ViewModels.Activities.IActivityTabContentSignal
{
    private readonly GlobalTimeService _service;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly TimeProvider _time;
    private readonly ITimer? _clock;
    private readonly IForecastRunRefresher? _refresher;
    private readonly TimeRefreshProgress? _progress;
    private TimelineAxisMap? _axis;

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

    public TimelineViewModel(GlobalTimeService service, ITimeFormatProvider? timeFormat, TimeProvider time, IForecastRunRefresher refresher)
        : this(service, timeFormat, time, PostToUiThread, refresher)
    {
    }

    public TimelineViewModel(GlobalTimeService service, ITimeFormatProvider? timeFormat, TimeProvider time, IForecastRunRefresher refresher, TimeRefreshProgress progress)
        : this(service, timeFormat, time, PostToUiThread, refresher, progress)
    {
    }

    public TimelineViewModel(GlobalTimeService service, ITimeFormatProvider? timeFormat, TimeProvider time, IForecastRunRefresher refresher, TimeRefreshProgress progress, ITimelineMapScope scope)
        : this(service, timeFormat, time, PostToUiThread, refresher, progress, scope)
    {
    }

    public TimelineViewModel(GlobalTimeService service, ITimeFormatProvider? timeFormat, TimeProvider time, IForecastRunRefresher refresher, TimeRefreshProgress progress, ITimelineMapScope scope, ILibraryTimeSource library)
        : this(service, timeFormat, time, PostToUiThread, refresher, progress, scope, library)
    {
    }

    internal TimelineViewModel(
        GlobalTimeService service,
        ITimeFormatProvider? timeFormat,
        TimeProvider time,
        Action<Action> dispatch,
        IForecastRunRefresher? refresher = null,
        TimeRefreshProgress? progress = null,
        ITimelineMapScope? scope = null,
        ILibraryTimeSource? library = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(dispatch);
        _service = service;
        _timeFormat = timeFormat;
        _time = time;
        _refresher = refresher;
        _progress = progress;

        PreviousStepCommand = new RelayCommand(() => StepBy(-1), () => CanStep(StepKind, -1));
        NextStepCommand = new RelayCommand(() => StepBy(+1), () => CanStep(StepKind, +1));
        InitializeNavigation();
        InitializeLanes(scope, library);
        NowCommand = new RelayCommand(GoLive, () => IsActive && !IsLive);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());
        CheckForNewRunsCommand = new RelayCommand(() => _refresher?.RefreshForecastSources(), () => _refresher?.HasForecastSources == true);
        JumpToDataCommand = new RelayCommand(JumpToData, () => NearestData() is not null);
        CancelDrawingCommand = new RelayCommand(
            () =>
            {
                if (_progress?.Drawn is { } drawn)
                    _service.SetCurrentTime(drawn);
            },
            () => IsDrawing && _progress?.Drawn is not null);
        ShowLayersCommand = new RelayCommand(
            () =>
            {
                if (FirstEmptyDatasetId() is { } id)
                    ShowLayerRequested?.Invoke(id);
            },
            () => FirstEmptyDatasetId() is not null);
        if (_progress is not null)
            _progress.Changed += () => dispatch(RaiseNow);

        _service.RangeChanged += OnRangeChanged;
        _service.CurrentTimeChanged += _ =>
        {
            // A time outside the window or inside a collapsed gap rebuilds the
            // axis around it (#713, #708), but not under a scrub, which would
            // move the axis under the pointer.
            if (!_scrubbing && _service.CurrentTime is { } current && NeedsAxisFor(current))
            {
                KeepInView(current);
                RebuildAxis();
            }
            OnPropertyChanged(nameof(SliderValue));
            OnPropertyChanged(nameof(CurrentTimeLabel));
            RaiseSteps();
            RaiseNow();
        };
        _service.ModeChanged += _ => RaiseNow();

        // Now moves by the minute: the NOW line, the offset, "every forecast
        // ended" and a Live view time follow the clock (#685, #706, #713).
        _clock = _time.CreateTimer(_ => dispatch(OnClockTick), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        if (_timeFormat is not null)
        {
            _timeFormat.TimeFormatChanged += _ =>
            {
                OnPropertyChanged(nameof(CurrentTimeLabel));
                OnPropertyChanged(nameof(RangeLabel));
                OnPropertyChanged(nameof(StampText));
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
        // Rebuild the gap-collapsing axis from the new aggregate range and
        // coverage segments before notifying slider/band bindings.
        RebuildAxis();

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
        OnPropertyChanged(nameof(ShowLanes));
        OnPropertyChanged(nameof(IsInMapView));
        ((RelayCommand)PreviousStepCommand).NotifyCanExecuteChanged();
        ((RelayCommand)NextStepCommand).NotifyCanExecuteChanged();
        RaiseNow();

        // Data that covers now starts Live (D6: step one of a 12:00Z run is
        // already hours old); loading or replacing a run keeps the mode, and a
        // pinned time stays put even when the new data does not cover it (#713).
        if ((becameActive && IsNowInCoverage) || (nowActive && IsLive))
        {
            // The axis was laid out around the loaded view time; going live
            // moves it, so lay it out again around now.
            GoLive();
            RebuildAxis();
        }

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

    /// <summary>Steps back by the chosen step (handoff C6).</summary>
    public ICommand PreviousStepCommand { get; }

    /// <summary>Steps forward by the chosen step.</summary>
    public ICommand NextStepCommand { get; }

    /// <summary>True when the step controls are shown: whenever the timeline is active.</summary>
    public bool AreStepButtonsVisible => IsActive;

    /// <summary>True when the timeline panel should be visible.</summary>
    public bool IsActive => _service.IsActive;

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
            // choice of time and must not leave Live.
            if (_service.CurrentTime is { } current && axis.ToPosition(current) == value) return;
            _scrubbing = true;
            try
            {
                _service.SetCurrentTime(axis.ToTime(value));
            }
            finally
            {
                _scrubbing = false;
            }
        }
    }

    /// <summary>
    /// Go live: moves the view time to now and follows now from then on, even
    /// past every loaded window, until the user picks a time (#706, #713).
    /// Available while the timeline is pinned.
    /// </summary>
    public ICommand NowCommand { get; }

    /// <summary>Refreshes the Library's forecast sources ("Check for new runs").</summary>
    public ICommand CheckForNewRunsCommand { get; }

    /// <summary>Pins the view time at the nearest data of the layers that have none now ("Next data ›" / "‹ Previous data").</summary>
    public ICommand JumpToDataCommand { get; }

    /// <summary>True while the view time follows now.</summary>
    public bool IsLive => _service.Mode == TimeMode.Live;

    /// <summary>"LIVE" or "PINNED", for the mode pill.</summary>
    public string ModeLabel => IsLive ? Strings.TimelinePanel_Live : Strings.TimelinePanel_Pinned;

    /// <summary>
    /// True when the timeline spans forecasts: S-111 surface currents (always
    /// model forecasts, however their files are named; Library tiles carry no
    /// run in their name) or a dataset whose name carries a run time, e.g.
    /// <c>111US00_CBOFS_20260930T18Z_…</c>. "Every forecast ended" is shown
    /// for them (#685, #713).
    /// </summary>
    public bool IsForecastTimeline =>
        Runs.Count > 0
        || _service.TimedDatasets.Any(d => string.Equals(d.ProductSpec, "S-111", StringComparison.OrdinalIgnoreCase));

    /// <summary>The forecast runs loaded, by model, e.g. "cbofs 12:00Z".</summary>
    public IReadOnlyList<string> Runs => _service.TimedDatasets
        .Select(d => ForecastRunNames.Describe(d.Name))
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>True when now lies inside a loaded window (a coverage segment).</summary>
    public bool IsNowInCoverage => IsActive && IsCovered(Now);

    private bool IsCovered(DateTime time) =>
        _service.CoverageSegments.Count > 0
            ? _service.CoverageSegments.Any(s => time >= s.Start && time <= s.End)
            : _service.MinTime is { } min && _service.MaxTime is { } max && time >= min && time <= max;

    /// <summary>True when the NOW line is drawn: on every active timeline, whose window always includes now (#713).</summary>
    public bool IsNowInRange => IsActive;

    /// <summary>The NOW line's position on the axis (0–1); NaN when not drawn.</summary>
    public double NowPosition => IsNowInRange && Axis is { } axis ? axis.ToPosition(Now) : double.NaN;

    /// <summary>True when now lies after every loaded window of a forecast timeline: every loaded forecast has ended (D3).</summary>
    public bool IsForecastEnded => IsForecastTimeline && _service.MaxTime is { } max && Now > max;

    /// <summary>The coverage band's opacity: dimmed once every loaded forecast has ended.</summary>
    public double BandOpacity => IsForecastEnded ? 0.45 : 1.0;

    /// <summary>The view time's offset from now: "now", "in 11 h 30", "5 h ago".</summary>
    public string OffsetText => _service.CurrentTime is { } t ? Offset(t - Now) : string.Empty;

    /// <summary>
    /// The map stamp: "LIVE · 01.10.2026 20:30" or "02.10.2026 08:00 · in 11 h 30"
    /// (top left of the map while the Timeline is open; #713 B4).
    /// </summary>
    public string StampText => _service.CurrentTime is { } t
        ? IsDrawing && _progress?.Drawn is { } drawn && _progress.Target is { } target
            ? string.Format(CultureInfo.CurrentCulture, Strings.TimelinePanel_StampDrawingFormat, TimeFormatting.Format(drawn, ActiveFormat), TimeFormatting.Format(target, ActiveFormat))
        : IsLive
            ? $"{Strings.TimelinePanel_Live} · {TimeFormatting.Format(t, ActiveFormat)}"
            : $"{TimeFormatting.Format(t, ActiveFormat)} · {OffsetText}"
        : string.Empty;

    /// <summary>
    /// The status line's message, one at a time by priority: every forecast
    /// ended (while Live), then layers without data at the view time; empty
    /// when all is well (#713 B3).
    /// </summary>
    public string StatusMessage
    {
        get
        {
            if (IsDrawing && _progress?.Target is { } target)
                return string.Format(CultureInfo.CurrentCulture, Strings.TimelinePanel_DrawingFormat, TimeFormatting.Format(target, ActiveFormat), _progress.Ready, _progress.Total);
            if (IsLive && IsForecastEnded && _service.MaxTime is { } max)
                return string.Format(CultureInfo.CurrentCulture, Strings.TimelinePanel_EveryForecastEndedFormat, Duration(Now - max));
            var (empty, total) = EmptyLayerCount();
            if (empty == 0)
                return string.Empty;
            return string.Format(
                CultureInfo.CurrentCulture,
                IsLive ? Strings.TimelinePanel_NoDataNowFormat : Strings.TimelinePanel_NoDataAtTimeFormat,
                empty,
                total);
        }
    }

    /// <summary>True when <see cref="StatusMessage"/> is shown.</summary>
    public bool HasStatusMessage => StatusMessage.Length > 0;

    /// <summary>
    /// True while the map is drawing a new view time and some layer is still
    /// drawing it (#709 B3): the status line says how far it has got.
    /// </summary>
    public bool IsDrawing => _progress?.IsDrawing == true;

    /// <summary>Goes back to the view time the map last finished drawing (the loading message's Cancel).</summary>
    public ICommand CancelDrawingCommand { get; }

    /// <summary>Opens the layer list on the first layer without data (clicking "No data … for N of M layers", handoff D4).</summary>
    public ICommand ShowLayersCommand { get; }

    /// <summary>Raised with a dataset id when the layer list should show that layer.</summary>
    public event Action<string>? ShowLayerRequested;

    /// <summary>True when the message is "every forecast ended" (shown in red).</summary>
    public bool IsStatusError => !IsDrawing && IsLive && IsForecastEnded;

    /// <summary>True when the message is about layers without data (shown in amber).</summary>
    public bool IsStatusWarning => HasStatusMessage && !IsStatusError && !IsDrawing;

    /// <summary>The message's action: "Check for new runs", "Next data ›" or "‹ Previous data"; empty for none.</summary>
    public string StatusActionText =>
        IsDrawing
            ? CancelDrawingCommand.CanExecute(null) ? Strings.TimelinePanel_Cancel : string.Empty
        : IsStatusError
            ? CheckForNewRunsCommand.CanExecute(null) ? Strings.TimelinePanel_CheckForNewRuns : string.Empty
        : IsStatusWarning && NearestData() is { } target && _service.CurrentTime is { } t
            ? target > t ? Strings.TimelinePanel_NextData : Strings.TimelinePanel_PreviousData
        : string.Empty;

    /// <summary>True when <see cref="StatusActionText"/> is shown.</summary>
    public bool HasStatusAction => StatusActionText.Length > 0;

    /// <summary>The command behind <see cref="StatusActionText"/>.</summary>
    public ICommand? StatusActionCommand =>
        IsDrawing ? CancelDrawingCommand : IsStatusError ? CheckForNewRunsCommand : IsStatusWarning ? JumpToDataCommand : null;

    /// <summary>How many time-aware layers have no data within their tolerance of the view time, of how many.</summary>
    private (int Empty, int Total) EmptyLayerCount()
    {
        var datasets = _service.TimedDatasets;
        if (_service.CurrentTime is not { } t || datasets.Count == 0)
            return (0, datasets.Count);
        return (datasets.Count(d => !Covers(d, t)), datasets.Count);
    }

    /// <summary>True when the dataset draws something at <paramref name="time"/>, by the renderer's own time rule.</summary>
    private static bool Covers(EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimedDataset dataset, DateTime time) =>
        dataset.Samples.Count > 0 ? dataset.SampleAt(time) is not null
        : dataset.Coverage.Count > 0 ? dataset.Covers(time)
        : time >= dataset.First && time <= dataset.Last;

    /// <summary>The id of the first layer without data at the view time, or null.</summary>
    private string? FirstEmptyDatasetId() =>
        _service.CurrentTime is { } t
            ? _service.TimedDatasets.FirstOrDefault(d => d.DatasetId is not null && !Covers(d, t))?.DatasetId
            : null;

    /// <summary>
    /// The loaded sample nearest the view time, before or after it, at which
    /// a layer without data now has data; <see langword="null"/> when there is none.
    /// </summary>
    private DateTime? NearestData()
    {
        if (_service.CurrentTime is not { } t)
            return null;
        var empty = _service.TimedDatasets.Where(d => !Covers(d, t)).ToArray();
        if (empty.Length == 0)
            return null;
        DateTime? previous = null;
        DateTime? next = null;
        foreach (var sample in _service.AllSamples)
        {
            if (!empty.Any(d => Covers(d, sample)))
                continue;
            if (sample < t)
                previous = sample;
            else if (sample > t && next is null)
                next = sample;
        }
        return (previous, next) switch
        {
            ({ } p, { } n) => t - p <= n - t ? p : n,
            ({ } p, null) => p,
            (null, { } n) => n,
            _ => null,
        };
    }

    private void JumpToData()
    {
        if (NearestData() is { } target)
            _service.SetCurrentTime(target);
    }

    /// <summary>Enters Live: the view time becomes now and follows it.</summary>
    private void GoLive() => _service.GoLive(Now);

    private void OnClockTick()
    {
        if (IsLive)
            GoLive();
        KeepInView(Now);
        RebuildAxis();
        RaiseSteps();
        RaiseNow();
    }

    private void RaiseNow()
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(IsForecastTimeline));
        OnPropertyChanged(nameof(IsNowInRange));
        OnPropertyChanged(nameof(NowPosition));
        OnPropertyChanged(nameof(IsForecastEnded));
        OnPropertyChanged(nameof(BandOpacity));
        OnPropertyChanged(nameof(RangeLabel));
        OnPropertyChanged(nameof(OffsetText));
        OnPropertyChanged(nameof(StampText));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasStatusMessage));
        OnPropertyChanged(nameof(IsStatusError));
        OnPropertyChanged(nameof(IsStatusWarning));
        OnPropertyChanged(nameof(StatusActionText));
        OnPropertyChanged(nameof(HasStatusAction));
        OnPropertyChanged(nameof(StatusActionCommand));
        OnPropertyChanged(nameof(IsDrawing));
        ((RelayCommand)CancelDrawingCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ShowLayersCommand).NotifyCanExecuteChanged();
        ((RelayCommand)NowCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CheckForNewRunsCommand).NotifyCanExecuteChanged();
        ((RelayCommand)JumpToDataCommand).NotifyCanExecuteChanged();
        UpdateLaneTimes();
    }

    /// <summary>"now", "in 11 h 30", "5 h ago", "in 2 d 4 h", "25 min ago".</summary>
    internal static string Offset(TimeSpan delta)
    {
        if (delta.Duration() < TimeSpan.FromMinutes(1))
            return Strings.TimelinePanel_OffsetNow;
        var span = delta.Duration();
        var c = CultureInfo.CurrentCulture;
        var text = span.TotalDays >= 14
            ? TimelineAxisLabels.GapLength(span, c)
            : span.TotalHours < 1
            ? string.Format(c, Strings.TimelinePanel_MinutesFormat, (int)span.TotalMinutes)
            : span.TotalHours < 48
                ? span.Minutes == 0
                    ? string.Format(c, Strings.TimelinePanel_HoursFormat, (int)span.TotalHours)
                    : string.Format(c, Strings.TimelinePanel_HoursMinutesFormat, (int)span.TotalHours, span.Minutes)
                : span.Hours == 0
                    ? string.Format(c, Strings.TimelinePanel_DaysFormat, (int)span.TotalDays)
                    : string.Format(c, Strings.TimelinePanel_DaysHoursFormat, (int)span.TotalDays, span.Hours);
        return string.Format(c, delta > TimeSpan.Zero ? Strings.TimelinePanel_InFormat : Strings.TimelinePanel_AgoFormat, text);
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
                var parts = new List<string>(6)
                {
                    $"{TimeFormatting.Format(_service.MinTime.Value, fmt)} → {TimeFormatting.Format(_service.MaxTime.Value, fmt)}",
                    Duration(_service.MaxTime.Value - _service.MinTime.Value),
                };
                if (runs.Count > 0)
                {
                    parts.Add(runs.Count <= 3
                        ? string.Join(", ", runs)
                        : string.Format(CultureInfo.CurrentCulture, Strings.Library_AndMoreFormat, string.Join(", ", runs.Take(2)), runs.Count - 2));
                }
                if (StepText() is { } step)
                    parts.Add(step);
                if (InMapViewCount is { } inView)
                    parts.Insert(0, inView);
                if (StepKind == TimelineStepKind.Sample && Driver is { } driver)
                    parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.TimelinePanel_StepFollowsFormat, DriverLabel(driver), Duration(TimelineStepper.Cadence(driver.Samples))));
                return string.Join(" · ", parts);
            }

            var range = string.Format(
                CultureInfo.CurrentCulture,
                Strings.TimelinePanel_Range,
                samples.Count,
                TimeFormatting.Format(_service.MinTime.Value, fmt),
                TimeFormatting.Format(_service.MaxTime.Value, fmt));
            return InMapViewCount is { } count ? $"{count} · {range}" : range;
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
