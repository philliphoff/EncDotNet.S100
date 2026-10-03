using System.Globalization;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>A choice of window for the Timeline's axis (#708, handoff C5).</summary>
internal enum TimelinePreset
{
    /// <summary>Six hours either side of now.</summary>
    NowSixHours,

    /// <summary>Today, midnight to midnight in the user's zone.</summary>
    Today,

    /// <summary>The 48 hours from now.</summary>
    Next48Hours,

    /// <summary>The run (dataset) holding the view time.</summary>
    ThisRun,

    /// <summary>The data of the layers in the map view (#710).</summary>
    InView,

    /// <summary>Everything loaded, and now (the default).</summary>
    AllLoaded,

    /// <summary>A window set by zooming or panning.</summary>
    Custom,
}

/// <summary>A layer the step can follow ("Sample of ▸").</summary>
/// <param name="Name">The dataset name.</param>
/// <param name="Label">The label shown, e.g. "cbofs".</param>
/// <param name="IsSelected">True for the layer the step follows.</param>
internal sealed record TimelineDriverOption(string Name, string Label, bool IsSelected);

/// <summary>A collapsed gap normalized to the axis, with its tooltip.</summary>
/// <param name="Start">Start as a fraction of the axis.</param>
/// <param name="Width">Width as a fraction of the axis.</param>
/// <param name="Tooltip">"6 weeks with no data · 19.01 → 03.03".</param>
internal readonly record struct NormalizedGap(double Start, double Width, string Tooltip);

/// <summary>
/// The Timeline's navigation (#708, handoff Part C): the axis window and
/// its gaps, labels and ticks; the step menu; previous / next data; zoom,
/// pan and presets; and the keyboard.
/// </summary>
internal sealed partial class TimelineViewModel
{
    /// <summary>The fraction of the axis one sample must span for its tick (and snapping) to show.</summary>
    internal const double MinimumTickSpacing = 0.007;

    private static readonly TimeSpan MinimumZoomSpan = TimeSpan.FromHours(1);

    private bool _scrubbing;
    private bool _rebuilding;
    private TimelineStepKind _stepKind = TimelineStepKind.Hour;
    private string? _driverName;
    private (DateTime Start, DateTime End)? _zoom;
    private TimelinePreset _preset = TimelinePreset.AllLoaded;
    private TimelineAxisMap? _overview;
    private AvaloniaList<double> _ticks = [];
    private IReadOnlyList<NormalizedGap> _gaps = [];
    private IReadOnlyList<AxisLabel> _labels = [];

    private void InitializeNavigation()
    {
        PreviousDataCommand = new RelayCommand(() => StepBy(-1, TimelineStepKind.Data), () => CanStep(TimelineStepKind.Data, -1));
        NextDataCommand = new RelayCommand(() => StepBy(+1, TimelineStepKind.Data), () => CanStep(TimelineStepKind.Data, +1));
        SetStepCommand = new RelayCommand<string>(kind =>
        {
            if (Enum.TryParse<TimelineStepKind>(kind, out var parsed))
                StepKind = parsed;
        });
        SetDriverCommand = new RelayCommand<string>(name =>
        {
            _driverName = name;
            StepKind = TimelineStepKind.Sample;
            RebuildAxis();
        });
        ZoomInCommand = new RelayCommand(() => ZoomBy(0.5), () => IsActive);
        ZoomOutCommand = new RelayCommand(() => ZoomBy(2), () => IsActive && IsZoomed);
        ApplyPresetCommand = new RelayCommand<string>(preset =>
        {
            if (Enum.TryParse<TimelinePreset>(preset, out var parsed))
                ApplyPreset(parsed);
        });
    }

    // ── axis ────────────────────────────────────────────────────────────

    /// <summary>
    /// The axis for the visible window, built on first use while data is
    /// loaded. A rebuild raises the axis properties, whose getters come back
    /// here, so building is skipped while one is under way (and while nothing
    /// is loaded, when there is nothing to build).
    /// </summary>
    private TimelineAxisMap? Axis
    {
        get
        {
            if (_axis is null && !_rebuilding && _service.IsActive)
                RebuildAxis();
            return _axis;
        }
    }

    /// <summary>
    /// The loaded range (of the layers in the map view while the In map view
    /// filter is on, #710) widened to include now and the view time.
    /// </summary>
    private (DateTime Start, DateTime End)? FullWindow
    {
        get
        {
            if (AxisRange is not { } range)
                return null;
            var (min, max) = range;
            var now = Now;
            var start = min < now ? min : now;
            var end = max > now ? max : now;
            if (_service.CurrentTime is { } current)
            {
                start = current < start ? current : start;
                end = current > end ? current : end;
            }
            return (start, end);
        }
    }

    /// <summary>
    /// Rebuilds the axis over the visible window (the zoom, or the full
    /// window), keeping now and the view time on stretches drawn to scale,
    /// and the overview over everything.
    /// </summary>
    private void RebuildAxis()
    {
        if (_rebuilding)
            return;
        _rebuilding = true;
        try
        {
            RebuildAxisCore();
        }
        finally
        {
            _rebuilding = false;
        }
        RaiseAxis();
    }

    private void RebuildAxisCore()
    {
        if (FullWindow is not { } full)
        {
            _axis = null;
            _overview = null;
            _ticks = [];
            _gaps = [];
            _labels = [];
            return;
        }

        var (start, end) = _zoom ?? full;
        var focus = new List<DateTime>(2) { Now };
        if (_service.CurrentTime is { } current)
            focus.Add(current);
        _axis = new TimelineAxisMap(start, end, AxisCoverage, focus);

        // The overview spans everything loaded, whatever the filter (handoff C5).
        var overviewStart = Earliest(start, full.Start, _service.MinTime ?? full.Start);
        var overviewEnd = Latest(end, full.End, _service.MaxTime ?? full.End);
        _overview = new TimelineAxisMap(overviewStart, overviewEnd, _service.CoverageSegments);

        var culture = CultureInfo.CurrentCulture;
        _gaps = [.. _axis.Gaps.Select(g => new NormalizedGap(
            g.Start,
            g.Width,
            string.Format(
                culture,
                Strings.TimelinePanel_GapTooltipFormat,
                TimelineAxisLabels.GapLength(g.Length, culture),
                TimeFormatting.Format(g.From, ActiveFormat),
                TimeFormatting.Format(g.To, ActiveFormat))))];
        _labels = TimelineAxisLabels.Layout(_axis, Zone, culture);
        _ticks = DriverTicks(_axis);
    }

    private static DateTime Earliest(params DateTime[] times) => times.Min();

    private static DateTime Latest(params DateTime[] times) => times.Max();

    private void RaiseAxis()
    {
        RebuildLanes();
        OnPropertyChanged(nameof(CoverageBands));
        OnPropertyChanged(nameof(Gaps));
        OnPropertyChanged(nameof(AxisLabels));
        OnPropertyChanged(nameof(Ticks));
        OnPropertyChanged(nameof(IsSnapToTickEnabled));
        OnPropertyChanged(nameof(SliderValue));
        OnPropertyChanged(nameof(NowPosition));
        OnPropertyChanged(nameof(OverviewBands));
        OnPropertyChanged(nameof(OverviewWindowStart));
        OnPropertyChanged(nameof(OverviewWindowWidth));
        OnPropertyChanged(nameof(IsZoomed));
        OnPropertyChanged(nameof(PresetLabel));
        OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(DriverOptions));
    }

    /// <summary>True when <paramref name="time"/> needs the axis rebuilt: off the window, or inside a collapsed gap.</summary>
    private bool NeedsAxisFor(DateTime time) =>
        _axis is not { } axis || time < axis.Start || time > axis.End || axis.IsInCollapsedGap(time);

    /// <summary>Pans a zoomed window so <paramref name="time"/> stays in it.</summary>
    private void KeepInView(DateTime time)
    {
        if (_zoom is not { } zoom || (time >= zoom.Start && time <= zoom.End))
            return;
        var span = zoom.End - zoom.Start;
        _zoom = time > zoom.End
            ? (time - span * 0.8, time + span * 0.2)
            : (time - span * 0.2, time + span * 0.8);
    }

    /// <summary>The user's zone for days: UTC, or the local zone of the clock.</summary>
    private TimeZoneInfo Zone => ActiveFormat == TimeFormat.Utc ? TimeZoneInfo.Utc : _time.LocalTimeZone;

    /// <summary>The collapsed gaps, for the band's hatch and the slider's breaks (handoff C2).</summary>
    public IReadOnlyList<NormalizedGap> Gaps => Axis is null ? [] : _gaps;

    /// <summary>The labels under the axis: gaps, days, 6-hour marks (handoff C3).</summary>
    public IReadOnlyList<AxisLabel> AxisLabels => Axis is null ? [] : _labels;

    // ── ticks (C4) ──────────────────────────────────────────────────────

    /// <summary>
    /// Ticks at the driver layer's samples in view, when they are at least
    /// <see cref="MinimumTickSpacing"/> apart; otherwise none (the band reads
    /// as solid data).
    /// </summary>
    public AvaloniaList<double> Ticks => Axis is null ? [] : _ticks;

    /// <summary>Spacing between decorative ticks: none; every tick is a real sample.</summary>
    public double TickFrequency => 0;

    /// <summary>The slider snaps to samples exactly when it shows their ticks.</summary>
    public bool IsSnapToTickEnabled => Ticks.Count > 0;

    private AvaloniaList<double> DriverTicks(TimelineAxisMap axis)
    {
        var samples = Driver?.Samples ?? [];
        var positions = samples
            .Where(s => s >= axis.Start && s <= axis.End)
            .Select(axis.ToPosition)
            .ToArray();
        if (positions.Length < 2)
            return [];
        var steps = positions.Zip(positions.Skip(1), (a, b) => b - a).Order().ToArray();
        return steps[steps.Length / 2] >= MinimumTickSpacing ? new AvaloniaList<double>(positions) : [];
    }

    // ── step menu (C6, C7) ──────────────────────────────────────────────

    /// <summary>Steps to the start of the previous cluster of data, skipping gaps.</summary>
    public ICommand PreviousDataCommand { get; private set; } = null!;

    /// <summary>Steps to the start of the next cluster of data, skipping gaps.</summary>
    public ICommand NextDataCommand { get; private set; } = null!;

    /// <summary>Chooses the step by its <see cref="TimelineStepKind"/> name.</summary>
    public ICommand SetStepCommand { get; private set; } = null!;

    /// <summary>Makes the step follow the samples of the named layer.</summary>
    public ICommand SetDriverCommand { get; private set; } = null!;

    /// <summary>What ‹ › step by.</summary>
    public TimelineStepKind StepKind
    {
        get => _stepKind;
        set
        {
            if (_stepKind == value)
                return;
            _stepKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StepLabel));
            OnPropertyChanged(nameof(RangeLabel));
            OnPropertyChanged(nameof(DriverOptions));
            RaiseSteps();
        }
    }

    /// <summary>The step menu's label: "1 h", "cbofs", "Boundary", "Data".</summary>
    public string StepLabel => StepKind switch
    {
        TimelineStepKind.TenMinutes => Strings.TimelinePanel_Step10Min,
        TimelineStepKind.Hour => Strings.TimelinePanel_Step1H,
        TimelineStepKind.SixHours => Strings.TimelinePanel_Step6H,
        TimelineStepKind.Day => Strings.TimelinePanel_Step1Day,
        TimelineStepKind.Sample => Driver is { } driver ? DriverLabel(driver) : Strings.TimelinePanel_StepSampleOf,
        TimelineStepKind.Boundary => Strings.TimelinePanel_StepBoundaryShort,
        _ => Strings.TimelinePanel_StepDataShort,
    };

    /// <summary>The layers the step can follow, for "Sample of ▸".</summary>
    public IReadOnlyList<TimelineDriverOption> DriverOptions
    {
        get
        {
            var driver = Driver;
            return [.. _service.TimedDatasets.Select(d => new TimelineDriverOption(
                d.Name, DriverLabel(d), StepKind == TimelineStepKind.Sample && ReferenceEquals(d, driver)))];
        }
    }

    /// <summary>
    /// The layer whose samples "Sample of" follows and whose ticks show: the
    /// one chosen, else the forecast with the coarsest cadence in view
    /// (handoff C6).
    /// </summary>
    internal MapsuiMapTimedDataset? Driver =>
        _service.TimedDatasets.FirstOrDefault(d => d.Name == _driverName)
        ?? TimelineStepper.DefaultDriver(AxisDatasets, IsForecastDataset);

    /// <summary>True for S-111 surface currents and for any dataset of a known forecast run (#720).</summary>
    private bool IsForecastDataset(MapsuiMapTimedDataset dataset) =>
        string.Equals(dataset.ProductSpec, "S-111", StringComparison.OrdinalIgnoreCase)
        || ForecastRunNames.RunOf(dataset, LibraryEntries) is not null;

    /// <summary>"cbofs" for a model's run or tile, else the dataset name.</summary>
    private static string DriverLabel(MapsuiMapTimedDataset dataset) =>
        ForecastRunNames.ModelAndTile(dataset.Name)?.Model
        ?? (ForecastRunNames.Describe(dataset.Name) is { } run ? run.Split(' ')[0] : dataset.Name);

    /// <summary>Where one step of <paramref name="kind"/> in <paramref name="direction"/> lands, or null.</summary>
    internal DateTime? StepTarget(TimelineStepKind kind, int direction)
    {
        if (!IsActive || _service.CurrentTime is not { } current || _service.MinTime is not { } min || _service.MaxTime is not { } max)
            return null;
        var now = Now;
        var limits = (min < now ? min : now, max > now ? max : now);
        return TimelineStepper.Step(kind, current, direction, _service.TimedDatasets, _service.CoverageSegments, Driver, limits, Zone);
    }

    private bool CanStep(TimelineStepKind kind, int direction) => StepTarget(kind, direction) is not null;

    /// <summary>Steps by the chosen step (or <paramref name="kind"/>): a user's choice of time, so it pins.</summary>
    internal void StepBy(int direction, TimelineStepKind? kind = null)
    {
        if (StepTarget(kind ?? StepKind, direction) is { } target)
            _service.SetCurrentTime(target);
    }

    private void RaiseSteps()
    {
        ((RelayCommand)PreviousStepCommand).NotifyCanExecuteChanged();
        ((RelayCommand)NextStepCommand).NotifyCanExecuteChanged();
        ((RelayCommand)PreviousDataCommand).NotifyCanExecuteChanged();
        ((RelayCommand)NextDataCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ZoomInCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ZoomOutCommand).NotifyCanExecuteChanged();
    }

    // ── zoom, pan, presets (C5) ─────────────────────────────────────────

    /// <summary>Halves the visible window around the view time.</summary>
    public ICommand ZoomInCommand { get; private set; } = null!;

    /// <summary>Doubles the visible window, back to All loaded at most.</summary>
    public ICommand ZoomOutCommand { get; private set; } = null!;

    /// <summary>Applies a <see cref="TimelinePreset"/> by name.</summary>
    public ICommand ApplyPresetCommand { get; private set; } = null!;

    /// <summary>True when the axis shows less than everything loaded.</summary>
    public bool IsZoomed => _zoom is not null;

    /// <summary>The preset menu's label: the preset in force, or "Custom" after a zoom or pan.</summary>
    public string PresetLabel => _preset switch
    {
        TimelinePreset.NowSixHours => Strings.TimelinePanel_PresetNowSixHours,
        TimelinePreset.Today => Strings.TimelinePanel_PresetToday,
        TimelinePreset.Next48Hours => Strings.TimelinePanel_PresetNext48Hours,
        TimelinePreset.ThisRun => Strings.TimelinePanel_PresetThisRun,
        TimelinePreset.InView => Strings.TimelinePanel_PresetInView,
        TimelinePreset.AllLoaded => Strings.TimelinePanel_PresetAllLoaded,
        _ => Strings.TimelinePanel_PresetCustom,
    };

    /// <summary>The overview strip's data, over everything loaded and now.</summary>
    public IReadOnlyList<NormalizedCoverageBand> OverviewBands => Axis is null ? [] : _overview?.CoverageBands ?? [];

    /// <summary>The visible window's start on the overview strip (0–1).</summary>
    public double OverviewWindowStart => _overview is { } o && _axis is { } a ? o.ToPosition(a.Start) : 0;

    /// <summary>The visible window's width on the overview strip (0–1).</summary>
    public double OverviewWindowWidth => _overview is { } o && _axis is { } a ? o.ToPosition(a.End) - o.ToPosition(a.Start) : 1;

    /// <summary>Applies <paramref name="preset"/>; false when it has nothing to show (no run, no layer in the map view).</summary>
    internal bool ApplyPreset(TimelinePreset preset)
    {
        var now = Now;
        (DateTime, DateTime)? window = preset switch
        {
            TimelinePreset.NowSixHours => (now.AddHours(-6), now.AddHours(6)),
            TimelinePreset.Today => Today(now),
            TimelinePreset.Next48Hours => (now, now.AddHours(48)),
            TimelinePreset.ThisRun => ThisRun(),
            TimelinePreset.InView => InViewWindow(),
            _ => null,
        };
        if (preset is TimelinePreset.ThisRun or TimelinePreset.InView && window is null)
            return false;
        _zoom = window;
        _preset = window is null ? TimelinePreset.AllLoaded : preset;
        RebuildAxis();
        RaiseSteps();
        return true;
    }

    /// <summary>Zooms by <paramref name="factor"/> (below 1 zooms in) around the view time.</summary>
    internal void ZoomBy(double factor)
    {
        if (Axis is not { } axis || FullWindow is not { } full)
            return;
        var anchor = _service.CurrentTime ?? axis.Start + (axis.End - axis.Start) / 2;
        var span = axis.End - axis.Start;
        var relative = span.Ticks > 0 ? (double)(anchor - axis.Start).Ticks / span.Ticks : 0.5;
        var newSpan = TimeSpan.FromTicks(Math.Max(MinimumZoomSpan.Ticks, (long)(span.Ticks * factor)));
        if (newSpan >= full.End - full.Start)
        {
            _zoom = null;
            _preset = TimelinePreset.AllLoaded;
        }
        else
        {
            var start = anchor - newSpan * relative;
            _zoom = (start, start + newSpan);
            _preset = TimelinePreset.Custom;
        }
        RebuildAxis();
        RaiseSteps();
    }

    /// <summary>The first time on the axis, or null while inactive.</summary>
    internal DateTime? VisibleStart => Axis?.Start;

    /// <summary>The last time on the axis, or null while inactive.</summary>
    internal DateTime? VisibleEnd => Axis?.End;

    /// <summary>The collapsed gaps on the axis, with their real extent.</summary>
    internal IReadOnlyList<AxisGap> AxisGaps => Axis?.Gaps ?? [];

    /// <summary>Shows exactly <paramref name="start"/>..<paramref name="end"/> on the axis (a custom window).</summary>
    internal void SetWindow(DateTime start, DateTime end)
    {
        if (end - start < MinimumZoomSpan)
            end = start + MinimumZoomSpan;
        _zoom = (start, end);
        _preset = TimelinePreset.Custom;
        RebuildAxis();
        RaiseSteps();
    }

    /// <summary>Pans a zoomed window by <paramref name="fraction"/> of its width (positive: later).</summary>
    internal void PanBy(double fraction)
    {
        if (_zoom is not { } zoom || FullWindow is not { } full)
            return;
        var span = zoom.End - zoom.Start;
        var start = zoom.Start + span * fraction;
        if (start < full.Start)
            start = full.Start;
        if (start + span > full.End)
            start = full.End - span;
        _zoom = (start, start + span);
        _preset = TimelinePreset.Custom;
        RebuildAxis();
    }

    private (DateTime, DateTime) Today(DateTime now)
    {
        var zone = Zone;
        var local = TimeZoneInfo.ConvertTimeFromUtc(now, zone).Date;
        var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
        var end = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.AddDays(1), DateTimeKind.Unspecified), zone);
        return (start, end);
    }

    /// <summary>The span of the dataset holding the view time (or the nearest one).</summary>
    private (DateTime, DateTime)? ThisRun()
    {
        if (_service.CurrentTime is not { } t || _service.TimedDatasets.Count == 0)
            return null;
        var run = _service.TimedDatasets.FirstOrDefault(d => t >= d.First && t <= d.Last)
            ?? _service.TimedDatasets.MinBy(d => Math.Min(Math.Abs((d.First - t).Ticks), Math.Abs((d.Last - t).Ticks)))!;
        return run.Last > run.First ? (run.First, run.Last) : (run.First.AddHours(-1), run.Last.AddHours(1));
    }

    // ── keyboard (C8) ───────────────────────────────────────────────────

    /// <summary>
    /// Handles the Timeline's keys: ←/→ step (Shift coarse, ⌥ data), Home/End
    /// the visible range's ends, N go live, +/− zoom, 0 fit. Returns true when
    /// handled. Keys with Ctrl or ⌘ are left alone; Space is reserved.
    /// </summary>
    internal bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (!IsActive || (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            return false;
        var direction = key switch
        {
            Key.Left => -1,
            Key.Right => +1,
            _ => 0,
        };
        if (direction != 0)
        {
            var kind = (modifiers & KeyModifiers.Alt) != 0 ? TimelineStepKind.Data
                : (modifiers & KeyModifiers.Shift) != 0 ? TimelineStepper.Coarse(StepKind)
                : StepKind;
            StepBy(direction, kind);
            return true;
        }
        switch (key)
        {
            case Key.Home when Axis is { } axis:
                _service.SetCurrentTime(axis.Start);
                return true;
            case Key.End when Axis is { } axis:
                _service.SetCurrentTime(axis.End);
                return true;
            case Key.N:
                if (NowCommand.CanExecute(null))
                    NowCommand.Execute(null);
                return true;
            case Key.Add or Key.OemPlus:
                ZoomBy(0.5);
                return true;
            case Key.Subtract or Key.OemMinus:
                ZoomBy(2);
                return true;
            case Key.D0 or Key.NumPad0:
                _ = ApplyPreset(TimelinePreset.AllLoaded);
                return true;
            default:
                return false;
        }
    }
}
