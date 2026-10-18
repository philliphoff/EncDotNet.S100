using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>The tone of a verdict or a leg chip (handoff C3, C5).</summary>
internal enum CheckTone
{
    /// <summary>Meets your settings.</summary>
    Success,

    /// <summary>Tight.</summary>
    Warning,

    /// <summary>Below your UKC setting.</summary>
    Destructive,

    /// <summary>No data, or data ends before arrival.</summary>
    Neutral,
}

/// <summary>
/// The Routes panel in Check passage mode (#901, handoff C2–C5): the
/// verdict card, the Legs / Findings switch and the check-mode leg rows,
/// projected from <see cref="PassageCheckService"/>.
/// </summary>
/// <remarks>
/// Without a draft (#953) the clearance parts are hidden: the verdict card is
/// titled from the findings summary and says "Add a draft to check
/// clearance", and there are no leg chips.
/// </remarks>
internal sealed class PassageCheckViewModel : ViewModelBase
{
    private readonly PassageCheckService _service;
    private readonly RoutesService _routes;
    private readonly Func<DepthUnit> _depthUnit;
    private readonly Func<TimeFormat> _timeFormat;
    private readonly RouteFindingsService? _findings;

    public PassageCheckViewModel(
        PassageCheckService service,
        RoutesService routes,
        Func<DepthUnit> depthUnit,
        Func<TimeFormat> timeFormat,
        RouteFindingsService? findings = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(routes);
        _service = service;
        _routes = routes;
        _depthUnit = depthUnit;
        _timeFormat = timeFormat;
        _findings = findings;
        Legs = new ObservableCollection<PassageCheckLegRowViewModel>();
        Window = new DepartureWindowViewModel(service, timeFormat);
        ShowLegsCommand = new RelayCommand(() => ShowFindings = false);
        ShowFindingsCommand = new RelayCommand(() => ShowFindings = true);
        HoverLegCommand = new RelayCommand<PassageCheckLegRowViewModel>(row =>
        {
            if (row is not null)
                _service.SetCursor(row.MidDistanceMeters);
        });
        if (findings is not null)
        {
            Findings = new RouteFindingsViewModel(findings);
            Findings.CountChanged += (_, _) => OnPropertyChanged(nameof(FindingsLabel));
            findings.Changed += (_, _) =>
            {
                if (NeedsDraft || _service.Input is { HasDraft: false })
                    Refresh();
            };
        }
        _service.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The verdict card's departure-window line and <b>Depart {HH:MM}</b> (#903).</summary>
    public DepartureWindowViewModel Window { get; }

    /// <summary>The Findings tab's list (#905), or <c>null</c> without a findings service.</summary>
    public RouteFindingsViewModel? Findings { get; }

    /// <summary>The check-mode leg rows (C5).</summary>
    public ObservableCollection<PassageCheckLegRowViewModel> Legs { get; }

    public ICommand ShowLegsCommand { get; }

    public ICommand ShowFindingsCommand { get; }

    /// <summary>Moves the profile cursor and ghost to the middle of a leg (C5 hover).</summary>
    public ICommand HoverLegCommand { get; }

    private bool _isActive;
    /// <summary>Whether Check passage is on.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    private bool _hasVerdict;
    /// <summary>Whether a result is shown (possibly stale).</summary>
    public bool HasVerdict
    {
        get => _hasVerdict;
        private set => SetProperty(ref _hasVerdict, value);
    }

    private bool _needsDraft;
    /// <summary>
    /// Whether the checked route has no draft (#953): the verdict card shows
    /// the no-draft variant and the clearance parts are hidden.
    /// </summary>
    public bool NeedsDraft
    {
        get => _needsDraft;
        private set
        {
            if (!SetProperty(ref _needsDraft, value))
                return;
            OnPropertyChanged(nameof(ShowClearance));
            OnPropertyChanged(nameof(ShowLegsNeedsDraft));
        }
    }

    /// <summary>Whether to show the clearance parts of the verdict card (min UKC); hidden without a draft.</summary>
    public bool ShowClearance => !NeedsDraft;

    /// <summary>Whether the Legs tab shows "Add a draft to check clearance" in place of the leg chips.</summary>
    public bool ShowLegsNeedsDraft => NeedsDraft && ShowLegs;

    private bool _isStale;
    /// <summary>Whether the shown verdict is being recomputed (C10: dimmed).</summary>
    public bool IsStale
    {
        get => _isStale;
        private set
        {
            if (SetProperty(ref _isStale, value))
                OnPropertyChanged(nameof(VerdictOpacity));
        }
    }

    /// <summary>Opacity of the verdict card: dimmed while stale.</summary>
    public double VerdictOpacity => IsStale ? 0.45 : 1.0;

    private string? _statusText;
    /// <summary>"Checking the passage…" or an error, shown when there is no verdict yet.</summary>
    public string? StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private CheckTone _tone = CheckTone.Neutral;
    /// <summary>The verdict's tone.</summary>
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

    private string? _title;
    /// <summary>The verdict title (C3 priority).</summary>
    public string? Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    private string? _confidenceText;
    /// <summary>
    /// The verdict's secondary line on confidence (#939): "Charted depth from
    /// S-101 on 1,5 NM" and/or "Vertical datums unconfirmed: …", one per line;
    /// <c>null</c> at full confidence. The title and tone follow the band.
    /// </summary>
    public string? ConfidenceText
    {
        get => _confidenceText;
        private set => SetProperty(ref _confidenceText, value);
    }

    private string? _minUkcText;
    /// <summary>The minimum UKC, e.g. "0,4 m".</summary>
    public string? MinUkcText
    {
        get => _minUkcText;
        private set => SetProperty(ref _minUkcText, value);
    }

    private string? _minUkcCaption;
    /// <summary>"min UKC · required 1,0 m" or "min UKC · no margin set".</summary>
    public string? MinUkcCaption
    {
        get => _minUkcCaption;
        private set => SetProperty(ref _minUkcCaption, value);
    }

    private string? _whereText;
    /// <summary>Where and when the minimum is: "Leg 6 → 7 · Thorn Channel · 07:52 · 12 min below".</summary>
    public string? WhereText
    {
        get => _whereText;
        private set => SetProperty(ref _whereText, value);
    }

    private string? _footerText;
    /// <summary>The one "Not for navigation" line.</summary>
    public string? FooterText
    {
        get => _footerText;
        private set => SetProperty(ref _footerText, value);
    }

    private string? _headerMeta;
    /// <summary>The panel header sub-line in check mode (C2).</summary>
    public string? HeaderMeta
    {
        get => _headerMeta;
        private set => SetProperty(ref _headerMeta, value);
    }

    private bool _showFindings;
    /// <summary>Whether the Findings tab is selected instead of Legs (C4).</summary>
    public bool ShowFindings
    {
        get => _showFindings;
        set
        {
            if (!SetProperty(ref _showFindings, value))
                return;
            OnPropertyChanged(nameof(ShowLegs));
            OnPropertyChanged(nameof(ShowLegsNeedsDraft));
        }
    }

    public bool ShowLegs => !ShowFindings;

    /// <summary>The Findings tab label, "Findings {N}" (C4).</summary>
    public string FindingsLabel => string.Format(CultureInfo.CurrentCulture, Strings.Check_TabFindingsFormat, Findings?.Count ?? 0);

    /// <summary>Re-reads the service state.</summary>
    public void Refresh()
    {
        IsActive = _service.IsActive;
        if (_service.IsActive && _service.Input is { HasDraft: false })
        {
            RefreshNeedsDraft();
            return;
        }

        NeedsDraft = false;
        var current = _service.IsActive ? _service.Current : null;
        IsStale = current is not null && _service.IsStale;
        HeaderMeta = BuildHeaderMeta(current);

        if (current is null)
        {
            HasVerdict = false;
            Legs.Clear();
            StatusText = !_service.IsActive ? null
                : _service.Error is { } error ? string.Format(CultureInfo.CurrentCulture, Strings.Check_ErrorFormat, error)
                : _service.IsComputing ? Strings.Check_Computing
                : null;
            return;
        }

        StatusText = null;
        HasVerdict = true;
        var result = current.Result;
        Tone = ToneOf(result.Verdict);
        Title = VerdictTitle(result);
        ConfidenceText = ConfidenceLine(result);
        var depthUnit = _depthUnit();
        if (result.Minimum is { } min)
        {
            MinUkcText = PassageFormatting.Format(min.ClearanceMeters, PassageQuantity.Depth, depthUnit);
            MinUkcCaption = Strings.Check_MinUkcLabel + " · " + (result.NoMarginSet
                ? Strings.Check_NoMarginSet
                : string.Format(CultureInfo.CurrentCulture, Strings.Check_RequiredFormat,
                    PassageFormatting.Format(min.RequiredUkcMeters, PassageQuantity.Depth, depthUnit)));
            WhereText = Where(current, min);
        }
        else
        {
            MinUkcText = "—";
            MinUkcCaption = Strings.Check_NoClearance;
            WhereText = null;
        }

        FooterText = Footer(result.Sources);
        RebuildLegs(current);
    }

    // #953: no draft, so no clearance; the title summarises the route checks.
    private void RefreshNeedsDraft()
    {
        NeedsDraft = true;
        HeaderMeta = BuildHeaderMeta(null);
        IsStale = _findings?.IsStale ?? false;
        StatusText = null;
        HasVerdict = true;
        Legs.Clear();
        var findings = _findings?.Findings ?? [];
        Tone = NeedsDraftTone(findings);
        Title = NeedsDraftTitle(findings);
        ConfidenceText = null;
        MinUkcText = null;
        MinUkcCaption = null;
        WhereText = null;
        FooterText = Strings.Check_FooterNoDraft;
    }

    /// <summary>
    /// The no-draft verdict title (#953), from the findings summary: "2 items
    /// to review", or "No findings for your settings".
    /// </summary>
    internal static string NeedsDraftTitle(IReadOnlyList<RouteFinding> findings) => findings.Count switch
    {
        0 => Strings.Check_NoDraftVerdictNone,
        1 => Strings.Check_NoDraftVerdictOne,
        var n => string.Format(CultureInfo.CurrentCulture, Strings.Check_NoDraftVerdictFormat, n),
    };

    /// <summary>The no-draft verdict tone: destructive only for must-review findings (F5).</summary>
    internal static CheckTone NeedsDraftTone(IReadOnlyList<RouteFinding> findings) =>
        findings.Any(f => f.Group == RouteFindingGroup.MustReview) ? CheckTone.Destructive
        : findings.Any(f => f.Group == RouteFindingGroup.Check) ? CheckTone.Warning
        : CheckTone.Neutral;

    private void RebuildLegs(PassageCheckSnapshot current)
    {
        var legs = current.Result.Legs;
        while (Legs.Count > legs.Count)
            Legs.RemoveAt(Legs.Count - 1);
        for (var i = 0; i < legs.Count; i++)
        {
            var row = i < Legs.Count ? Legs[i] : null;
            if (row is null)
            {
                row = new PassageCheckLegRowViewModel(i);
                Legs.Add(row);
            }
            row.Update(legs[i], current.LegPlaceNames[i], current.Result.Departure, _depthUnit(), _timeFormat());
        }
    }

    private string Where(PassageCheckSnapshot current, UkcMinimum min)
    {
        var parts = new List<string>
        {
            string.Format(CultureInfo.CurrentCulture, Strings.Check_LegFormat, min.LegIndex + 1, min.LegIndex + 2),
        };
        if (current.MinimumPlaceName is { } place)
            parts.Add(place);
        if (min.HasEta)
            parts.Add(PassageFormatting.FormatEta(min.Time, current.Result.Departure, _timeFormat()));
        var minutes = (int)Math.Round(current.Result.MinutesBelow);
        if (minutes > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Check_MinutesBelowFormat, minutes));
        return string.Join(" · ", parts);
    }

    private string? BuildHeaderMeta(PassageCheckSnapshot? current)
    {
        if (!_service.IsActive || _routes.Routes.ActiveRoute is not { } route)
            return null;

        var parts = new List<string>();
        if (route.Info.Vessel?.Name is { Length: > 0 } vessel)
            parts.Add(vessel);
        if (route.Passage.DraftMeters is { } draft)
            parts.Add(PassageFormatting.Format(draft, PassageQuantity.Depth, _depthUnit()));
        if (current is not null)
        {
            var departure = current.Result.Departure;
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Check_DepartureFormat,
                PassageFormatting.FormatEta(departure, departure, _timeFormat())));
            if (current.Result.Arrival is { } arrival)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Check_ArrivalFormat,
                    PassageFormatting.FormatEta(arrival, departure, _timeFormat())));
            }
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>The C3 title in the handoff's words.</summary>
    internal static string VerdictTitle(PassageCheckResult result) => result.Verdict switch
    {
        PassageVerdict.WaterLevelEndsBeforeArrival => Strings.Check_VerdictWaterLevelEnds,
        PassageVerdict.BelowSetting => result.PlacesBelow == 1
            ? Strings.Check_VerdictBelowOne
            : string.Format(CultureInfo.CurrentCulture, Strings.Check_VerdictBelowFormat, result.PlacesBelow),
        PassageVerdict.IncompleteData => Strings.Check_VerdictIncomplete,
        PassageVerdict.MeetsSettingsTight => Strings.Check_VerdictTight,
        _ => Strings.Check_VerdictMeets,
    };

    /// <summary>The verdict's confidence line, or <c>null</c> at full confidence.</summary>
    internal static string? ConfidenceLine(PassageCheckResult result)
    {
        var lines = new List<string>();
        if (result.Confidence.HasFlag(ConfidenceReasons.ChartedDepthFromS101))
        {
            var meters = result.LowConfidence
                .Where(s => s.Kind == PassageStretchKind.ChartedDepthFromS101)
                .Sum(s => s.LengthMeters);
            lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.Check_ConfidenceS101Format, meters / PassageGeodesy.MetersPerNauticalMile));
        }

        if (result.Confidence.HasFlag(ConfidenceReasons.DatumUnconfirmed))
            lines.Add(Strings.Check_ConfidenceDatumUnconfirmed);
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    internal static CheckTone ToneOf(PassageVerdict verdict) => verdict switch
    {
        PassageVerdict.MeetsSettings => CheckTone.Success,
        PassageVerdict.MeetsSettingsTight => CheckTone.Warning,
        PassageVerdict.BelowSetting => CheckTone.Destructive,
        _ => CheckTone.Neutral,
    };

    private static string Footer(PassageSources sources)
    {
        var products = new List<string>();
        if (sources.BathymetryDatasets.Count > 0) products.Add("S-102");
        if (sources.FallbackDatasets.Count > 0) products.Add("S-101");
        if (sources.WaterLevelSources.Count > 0) products.Add("S-104");
        return products.Count == 0
            ? Strings.Check_FooterNoData
            : string.Format(CultureInfo.CurrentCulture, Strings.Check_FooterFormat, string.Join(", ", products));
    }
}

/// <summary>A leg row in Check passage mode (C5).</summary>
internal sealed class PassageCheckLegRowViewModel : ViewModelBase
{
    public PassageCheckLegRowViewModel(int legIndex)
    {
        LegIndex = legIndex;
    }

    public int LegIndex { get; }

    /// <summary>The leg number, e.g. "6".</summary>
    public string Number => (LegIndex + 1).ToString(CultureInfo.CurrentCulture);

    private string _name = string.Empty;
    /// <summary>The place name, or "Leg 6 → 7" when nothing named is near.</summary>
    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    private string? _timesText;
    /// <summary><c>{ETA from}–{ETA to} · {NM}</c>, or the distance alone without ETAs.</summary>
    public string? TimesText
    {
        get => _timesText;
        private set => SetProperty(ref _timesText, value);
    }

    private string _chipText = string.Empty;
    /// <summary>The leg's min UKC, or "no tide" / "no data".</summary>
    public string ChipText
    {
        get => _chipText;
        private set => SetProperty(ref _chipText, value);
    }

    private CheckTone _chipTone = CheckTone.Neutral;
    /// <summary>The chip tint: the leg's worst state.</summary>
    public CheckTone ChipTone
    {
        get => _chipTone;
        private set
        {
            if (!SetProperty(ref _chipTone, value))
                return;
            OnPropertyChanged(nameof(IsChipSuccess));
            OnPropertyChanged(nameof(IsChipWarning));
            OnPropertyChanged(nameof(IsChipDestructive));
            OnPropertyChanged(nameof(IsChipNeutral));
        }
    }

    public bool IsChipSuccess => ChipTone == CheckTone.Success;

    public bool IsChipWarning => ChipTone == CheckTone.Warning;

    public bool IsChipDestructive => ChipTone == CheckTone.Destructive;

    public bool IsChipNeutral => ChipTone == CheckTone.Neutral;

    private bool _isLowConfidence;
    /// <summary>
    /// Whether part of the leg is lower confidence: the chip keeps its band
    /// tint and gets a dashed muted border (#939).
    /// </summary>
    public bool IsLowConfidence
    {
        get => _isLowConfidence;
        private set => SetProperty(ref _isLowConfidence, value);
    }

    private UkcState _worstState;
    /// <summary>The worst state on the leg.</summary>
    public UkcState WorstState
    {
        get => _worstState;
        private set => SetProperty(ref _worstState, value);
    }

    /// <summary>Distance from the start of the passage to the middle of the leg.</summary>
    public double MidDistanceMeters { get; private set; }

    internal void Update(LegUkcResult leg, string? placeName, DateTimeOffset departure, DepthUnit depthUnit, TimeFormat timeFormat)
    {
        Name = placeName ?? string.Format(CultureInfo.CurrentCulture, Strings.Check_LegFormat, LegIndex + 1, LegIndex + 2);
        var nm = leg.DistanceMeters / PassageGeodesy.MetersPerNauticalMile;
        TimesText = leg.Start is { } start && leg.End is { } end
            ? string.Format(CultureInfo.CurrentCulture, Strings.Check_LegTimesFormat,
                PassageFormatting.FormatEta(start, departure, timeFormat),
                PassageFormatting.FormatEta(end, departure, timeFormat),
                nm)
            : string.Format(CultureInfo.CurrentCulture, Strings.Check_LegDistanceFormat, nm);
        MidDistanceMeters = leg.StartDistanceMeters + (leg.DistanceMeters / 2);
        WorstState = leg.WorstState;
        IsLowConfidence = leg.HasLowConfidence;

        if (leg.Minimum is { } min)
        {
            ChipText = PassageFormatting.Format(min.ClearanceMeters, PassageQuantity.Depth, depthUnit);
            ChipTone = leg.WorstState switch
            {
                UkcState.BelowSetting => CheckTone.Destructive,
                UkcState.NoData => CheckTone.Neutral,
                UkcState.Tight => CheckTone.Warning,
                _ => CheckTone.Success,
            };
        }
        else
        {
            ChipText = leg.HasNoWaterLevel ? Strings.Check_ChipNoTide : Strings.Check_ChipNoData;
            ChipTone = CheckTone.Neutral;
        }
    }
}
