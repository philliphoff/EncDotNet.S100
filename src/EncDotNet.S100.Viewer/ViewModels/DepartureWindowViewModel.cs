using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>What the verdict card's window line says.</summary>
/// <param name="Text">The sentence, e.g. "Departures 05:35–06:40 meet your settings.".</param>
/// <param name="IsInside">Whether the checked departure meets your settings and a window exists.</param>
/// <param name="Nearest">The meeting departure <b>Depart {HH:MM}</b> jumps to, or <c>null</c> when it isn't shown.</param>
/// <param name="DepartText">"Depart 06:40", or <c>null</c>.</param>
internal sealed record DepartureWindowLine(string Text, bool IsInside, DateTimeOffset? Nearest, string? DepartText);

/// <summary>
/// The verdict card's window line (#903, handoff C3): which departures meet
/// your settings, whether this one is inside the window, and a
/// <b>Depart {HH:MM}</b> button that jumps to the nearest passing departure
/// when it isn't. Projected from <see cref="PassageCheckService.Window"/>.
/// </summary>
internal sealed class DepartureWindowViewModel : ViewModelBase
{
    /// <summary>Windows listed before the rest are summarised as "+N more".</summary>
    internal const int MaxListedWindows = 4;

    private readonly PassageCheckService _service;
    private readonly Func<TimeFormat> _timeFormat;
    private DateTimeOffset? _nearest;

    public DepartureWindowViewModel(PassageCheckService service, Func<TimeFormat> timeFormat)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(timeFormat);
        _service = service;
        _timeFormat = timeFormat;
        DepartCommand = new RelayCommand(Depart, () => _nearest is not null);
        _service.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Sets the departure to the nearest passing one (C3 <b>Depart {HH:MM}</b>).</summary>
    public ICommand DepartCommand { get; }

    private bool _isVisible;
    /// <summary>Whether there is a window line (a result and a sweep over S-104).</summary>
    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    private string? _text;
    /// <summary>"Departures 05:35–06:40 meet your settings." or "This departure is inside the window. …".</summary>
    public string? Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    private bool _isInside;
    /// <summary>Whether the checked departure meets your settings and a window exists.</summary>
    public bool IsInside
    {
        get => _isInside;
        private set => SetProperty(ref _isInside, value);
    }

    private bool _showDepart;
    /// <summary>Whether the <b>Depart {HH:MM}</b> button shows: the departure is outside and some departure passes.</summary>
    public bool ShowDepart
    {
        get => _showDepart;
        private set => SetProperty(ref _showDepart, value);
    }

    private string? _departText;
    /// <summary>"Depart 06:40".</summary>
    public string? DepartText
    {
        get => _departText;
        private set => SetProperty(ref _departText, value);
    }

    /// <summary>The nearest passing departure the button jumps to, or <c>null</c>.</summary>
    public DateTimeOffset? NearestMeeting => _nearest;

    private bool _isStale;
    /// <summary>Whether the window is being swept again for changed legs (dimmed).</summary>
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

    /// <summary>Re-reads the service.</summary>
    public void Refresh()
    {
        var line = Describe(_service, _timeFormat());
        IsVisible = line is not null;
        IsStale = line is not null && _service.IsWindowComputing;
        IsInside = line?.IsInside ?? false;
        Text = line?.Text;
        _nearest = line?.Nearest;
        ShowDepart = _nearest is not null;
        DepartText = line?.DepartText;
        OnPropertyChanged(nameof(NearestMeeting));
        ((RelayCommand)DepartCommand).NotifyCanExecuteChanged();
    }

    /// <summary>
    /// The window line for the service's current result and window, or
    /// <c>null</c> when there is no line (no result, or no sweep over S-104).
    /// </summary>
    internal static DepartureWindowLine? Describe(PassageCheckService service, TimeFormat format)
    {
        var current = service.IsActive ? service.Current : null;
        var window = current is null ? null : service.Window;
        if (current is null || window is null || window.RouteId != current.RouteId)
            return null;

        var departure = current.Result.Departure;
        var meets = current.Result.Verdict is PassageVerdict.MeetsSettings or PassageVerdict.MeetsSettingsTight;
        var inside = meets && window.Windows.Count > 0;
        var text = string.Format(
            CultureInfo.CurrentCulture,
            inside ? Strings.Check_WindowInsideFormat : Strings.Check_WindowSentenceFormat,
            Summary(window, departure, format));
        var nearest = meets ? null : window.NearestMeeting(departure);
        var departText = nearest is { } n
            ? string.Format(CultureInfo.CurrentCulture, Strings.Check_DepartFormat, PassageFormatting.FormatEta(n, departure, format))
            : null;
        return new DepartureWindowLine(text, inside, nearest, departText);
    }

    private void Depart()
    {
        if (_nearest is { } nearest)
            _service.SetDeparture(nearest);
    }

    /// <summary>
    /// "Departures 05:35–06:40, 09:10–09:45 meet your settings", or "No
    /// departure before 12:00 meets your settings", without the full stop.
    /// </summary>
    internal static string Summary(PassageDepartureWindow window, DateTimeOffset reference, TimeFormat format)
    {
        string Time(DateTimeOffset t) => PassageFormatting.FormatEta(t, reference, format);

        var windows = window.Windows;
        if (windows.Count == 0)
            return string.Format(CultureInfo.CurrentCulture, Strings.Check_WindowNoneFormat, Time(window.To));
        if (windows.Count == 1 && windows[0].First == windows[0].Last)
            return string.Format(CultureInfo.CurrentCulture, Strings.Check_WindowSingleFormat, Time(windows[0].First));

        return string.Format(CultureInfo.CurrentCulture, Strings.Check_WindowFormat, Ranges(window, reference, format));
    }

    /// <summary>The windows alone: "05:35–06:40, 09:10–09:45", with "+N more" past <see cref="MaxListedWindows"/>.</summary>
    internal static string Ranges(PassageDepartureWindow window, DateTimeOffset reference, TimeFormat format)
    {
        string Time(DateTimeOffset t) => PassageFormatting.FormatEta(t, reference, format);

        var windows = window.Windows;
        var ranges = windows
            .Take(MaxListedWindows)
            .Select(w => w.First == w.Last
                ? Time(w.First)
                : string.Format(CultureInfo.CurrentCulture, Strings.Check_WindowRangeFormat, Time(w.First), Time(w.Last)))
            .ToList();
        if (windows.Count > MaxListedWindows)
            ranges.Add(string.Format(CultureInfo.CurrentCulture, Strings.Check_WindowMoreFormat, windows.Count - MaxListedWindows));
        return string.Join(", ", ranges);
    }
}
