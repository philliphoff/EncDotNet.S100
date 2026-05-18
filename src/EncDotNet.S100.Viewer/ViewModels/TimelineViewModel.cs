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
internal sealed class TimelineViewModel : ViewModelBase
{
    private readonly GlobalTimeService _service;
    private readonly ITimeFormatProvider? _timeFormat;

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

    private void OnRangeChanged()
    {
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
        ((RelayCommand)PreviousStepCommand).NotifyCanExecuteChanged();
        ((RelayCommand)NextStepCommand).NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Steps backward to the previous discrete sample. Only
    /// available when <see cref="AreStepButtonsVisible"/> is true.
    /// </summary>
    public ICommand PreviousStepCommand { get; }

    /// <summary>Steps forward to the next discrete sample.</summary>
    public ICommand NextStepCommand { get; }

    /// <summary>
    /// True when discrete sample stops are being painted on the
    /// slider (i.e. the same condition that drives
    /// <see cref="IsSnapToTickEnabled"/>). The view binds
    /// prev/next button visibility to this so step controls only
    /// surface when stepping has well-defined semantics.
    /// </summary>
    public bool AreStepButtonsVisible => IsSnapToTickEnabled;

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
    /// Tick stops painted along the slider. When all loaded
    /// datasets share a small set of timestamps, ticks correspond
    /// 1:1 to real sample times and the slider snaps to them.
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
                foreach (var s in samples) list.Add(s.Ticks);
            }
            else if (_service.MinTime is { } min && _service.MaxTime is { } max && max > min)
            {
                var span = (max - min).Ticks;
                for (var i = 0; i <= EvenlySpacedTickCount; i++)
                    list.Add(min.Ticks + (long)(span * (i / (double)EvenlySpacedTickCount)));
            }
            return list;
        }
    }

    /// <summary>
    /// Spacing between minor ticks (currently mirrors the major
    /// tick stride so the slider only paints the configured stops).
    /// </summary>
    public double TickFrequency
    {
        get
        {
            var samples = _service.AllSamples;
            if (samples.Count == 0) return 0;
            if (samples.Count <= SampleTickThreshold) return 0;
            if (_service.MinTime is { } min && _service.MaxTime is { } max && max > min)
                return (max - min).Ticks / (double)EvenlySpacedTickCount;
            return 0;
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

    public double SliderMinimum =>
        _service.MinTime is { } t ? (double)t.Ticks : 0d;

    public double SliderMaximum =>
        _service.MaxTime is { } t ? (double)t.Ticks : 1d;

    /// <summary>
    /// Two-way slider value as <see cref="DateTime.Ticks"/>. Setter
    /// pushes the new clock value through
    /// <see cref="GlobalTimeService.SetCurrentTime"/>; the loader
    /// debounces and then fans the change out to every registered
    /// dataset.
    /// </summary>
    public double SliderValue
    {
        get => _service.CurrentTime is { } t ? (double)t.Ticks : SliderMinimum;
        set
        {
            var ticks = (long)value;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return;
            var dt = new DateTime(ticks, DateTimeKind.Utc);
            _service.SetCurrentTime(dt);
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
