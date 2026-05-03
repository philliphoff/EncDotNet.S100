using System;
using System.Globalization;
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

    public TimelineViewModel(GlobalTimeService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;

        _service.RangeChanged += OnRangeChanged;
        _service.CurrentTimeChanged += _ => OnPropertyChanged(nameof(SliderValue));
    }

    private void OnRangeChanged()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(SliderMinimum));
        OnPropertyChanged(nameof(SliderMaximum));
        OnPropertyChanged(nameof(SliderValue));
        OnPropertyChanged(nameof(RangeLabel));
        OnPropertyChanged(nameof(CurrentTimeLabel));
    }

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

    /// <summary>ISO-8601 UTC text for the currently selected time.</summary>
    public string CurrentTimeLabel =>
        _service.CurrentTime is { } t
            ? t.ToString("u", CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>"N steps from T0 to T1"-style summary of the timeline.</summary>
    public string RangeLabel
    {
        get
        {
            var samples = _service.AllSamples;
            if (samples.Count == 0 || _service.MinTime is null || _service.MaxTime is null)
                return Strings.TimelinePanel_NoData;
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.TimelinePanel_Range,
                samples.Count,
                _service.MinTime.Value,
                _service.MaxTime.Value);
        }
    }
}
