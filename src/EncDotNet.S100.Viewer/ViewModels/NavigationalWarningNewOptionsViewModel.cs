using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Navigational warnings panel's NEW options (#997): the mariner's
/// filters on when the NEW indication shows (S-98 Ed 2.0.0 §12.12.1 item 4,
/// off by default), and "Mark new since" for a watch handover (2019 IHO
/// S-124 workshop). The filters persist in <see cref="ViewerSettings"/>.
/// </summary>
internal sealed class NavigationalWarningNewOptionsViewModel : ViewModelBase
{
    /// <summary>The hours "Mark new since" offers, as watch lengths.</summary>
    internal static readonly IReadOnlyList<int> MarkSinceHours = [1, 4, 8, 12, 24];

    private const double DefaultWithinHours = 12;
    private const double DefaultWithinNm = 50;

    private readonly NavigationalWarningsService _service;
    private readonly ViewerSettings? _settings;
    private readonly TimeProvider _clock;
    private bool _withinHoursEnabled;
    private decimal _withinHours = (decimal)DefaultWithinHours;
    private bool _withinNmEnabled;
    private decimal _withinNm = (decimal)DefaultWithinNm;
    private int _markSinceIndex = 1;
    private string? _markedText;

    public NavigationalWarningNewOptionsViewModel(NavigationalWarningsService service, ViewerSettings? settings = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
        if (settings?.NavigationalWarningsNewWithinHours is { } hours and > 0)
        {
            _withinHoursEnabled = true;
            _withinHours = (decimal)hours;
        }
        if (settings?.NavigationalWarningsNewWithinNm is { } nm and > 0)
        {
            _withinNmEnabled = true;
            _withinNm = (decimal)nm;
        }

        MarkSinceChoices = MarkSinceHours
            .Select(h => string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_MarkSinceHoursFormat, h))
            .ToList();
        MarkNewSinceCommand = new RelayCommand(() => MarkNewSince(TimeSpan.FromHours(MarkSinceHours[_markSinceIndex])));
        Apply(save: false);
    }

    /// <summary>Whether only warnings received within <see cref="WithinHours"/> show NEW.</summary>
    public bool IsWithinHoursEnabled
    {
        get => _withinHoursEnabled;
        set
        {
            if (SetProperty(ref _withinHoursEnabled, value))
                Apply();
        }
    }

    /// <summary>The "received within" limit, in hours.</summary>
    public decimal WithinHours
    {
        get => _withinHours;
        set
        {
            if (value > 0 && SetProperty(ref _withinHours, value) && _withinHoursEnabled)
                Apply();
        }
    }

    /// <summary>Whether only warnings within <see cref="WithinNm"/> of own ship or the active route show NEW.</summary>
    public bool IsWithinNmEnabled
    {
        get => _withinNmEnabled;
        set
        {
            if (SetProperty(ref _withinNmEnabled, value))
                Apply();
        }
    }

    /// <summary>The distance limit, in nautical miles.</summary>
    public decimal WithinNm
    {
        get => _withinNm;
        set
        {
            if (value > 0 && SetProperty(ref _withinNm, value) && _withinNmEnabled)
                Apply();
        }
    }

    /// <summary>Whether a filter is on (the options button shows it).</summary>
    public bool IsFiltering => _withinHoursEnabled || _withinNmEnabled;

    /// <summary>"1 hours ago", "4 hours ago", …: the times "Mark new since" offers.</summary>
    public IReadOnlyList<string> MarkSinceChoices { get; }

    /// <summary>The chosen <see cref="MarkSinceChoices"/> entry; 4 hours by default.</summary>
    public int MarkSinceIndex
    {
        get => _markSinceIndex;
        set => SetProperty(ref _markSinceIndex, Math.Clamp(value, 0, MarkSinceHours.Count - 1));
    }

    /// <summary>Marks the warnings received since the chosen time new again.</summary>
    public ICommand MarkNewSinceCommand { get; }

    /// <summary>"N new after handover." after <see cref="MarkNewSinceCommand"/>; <c>null</c> before.</summary>
    public string? MarkedText
    {
        get => _markedText;
        private set => SetProperty(ref _markedText, value);
    }

    /// <summary>Marks every warning received in the last <paramref name="period"/> new again.</summary>
    /// <returns>How many listed parts show NEW afterwards.</returns>
    public int MarkNewSince(TimeSpan period)
    {
        using var cmd = ViewerObservability.BeginCommand("navwarnings.mark_new_since");
        var count = _service.MarkNewSince(_clock.GetUtcNow() - period);
        MarkedText = string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_MarkedFormat, count);
        return count;
    }

    private void Apply(bool save = true)
    {
        double? hours = _withinHoursEnabled ? (double)_withinHours : null;
        double? nm = _withinNmEnabled ? (double)_withinNm : null;
        _service.NewFilter = new NavigationalWarningNewFilter(hours is { } h ? TimeSpan.FromHours(h) : null, nm);
        OnPropertyChanged(nameof(IsFiltering));
        if (!save || _settings is null)
            return;
        _settings.NavigationalWarningsNewWithinHours = hours;
        _settings.NavigationalWarningsNewWithinNm = nm;
        _settings.Save();
    }
}
