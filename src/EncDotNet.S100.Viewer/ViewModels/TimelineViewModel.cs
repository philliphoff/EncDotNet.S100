using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    private TimelineAxisMap? _axis;

    public TimelineViewModel(GlobalTimeService service)
        : this(service, timeFormat: null)
    {
    }

    public TimelineViewModel(GlobalTimeService service, ITimeFormatProvider? timeFormat)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _timeFormat = timeFormat;

        PreviousStepCommand = new RelayCommand(StepPrevious, CanStepPrevious);
        NextStepCommand = new RelayCommand(StepNext, CanStepNext);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());

        _service.RangeChanged += OnRangeChanged;
        _service.CurrentTimeChanged += _ =>
        {
            OnPropertyChanged(nameof(SliderValue));
            OnPropertyChanged(nameof(CurrentTimeLabel));
            ((RelayCommand)PreviousStepCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextStepCommand).NotifyCanExecuteChanged();
        };

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
    /// well-defined (it walks <see cref="GlobalTimeService.AllSamples"/>),
    /// and is especially useful for dense, clustered datasets where the
    /// gap-collapsing slider still benefits from exact per-sample nudging.
    /// </summary>
    public bool AreStepButtonsVisible => _service.AllSamples.Count > 0;

    private bool CanStepPrevious()
    {
        if (!IsSnapToTickEnabled) return false;
        var samples = _service.AllSamples;
        return _service.CurrentTime is { } cur && samples.Count > 0 && cur > samples[0];
    }

    private bool CanStepNext()
    {
        if (!IsSnapToTickEnabled) return false;
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
            _service.SetCurrentTime(axis.ToTime(value));
        }
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
