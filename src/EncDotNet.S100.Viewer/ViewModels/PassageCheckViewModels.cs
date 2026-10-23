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
/// The Routes panel in Check route mode (#901, #959): the verdict card with
/// its summary rows, the Legs / Findings switch and the check-mode leg rows,
/// projected from <see cref="PassageCheckService"/> and
/// <see cref="RouteFindingsService"/>.
/// </summary>
/// <remarks>
/// One verdict covers the clearance check and every route check, worst
/// group first (<see cref="RouteCheckVerdict"/>). Without a draft (#953) the
/// clearance check doesn't run: the Clearance row reads "Not checked · +
/// Add draft", and there are no leg chips.
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
        RouteFindingsService? findings = null,
        Action? addDraft = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(routes);
        _service = service;
        _routes = routes;
        _depthUnit = depthUnit;
        _timeFormat = timeFormat;
        _findings = findings;
        Legs = new ObservableCollection<PassageCheckLegRowViewModel>();
        SummaryRows = new ObservableCollection<CheckSummaryRowViewModel>();
        Window = new DepartureWindowViewModel(service, timeFormat);
        HoverLegCommand = new RelayCommand<PassageCheckLegRowViewModel>(row =>
        {
            if (row is not null)
                _service.SetCursor(row.MidDistanceMeters);
        });
        AddDraftCommand = new RelayCommand(() => addDraft?.Invoke(), () => addDraft is not null);
        SelectSummaryCommand = new RelayCommand<CheckSummaryRowViewModel>(row =>
        {
            if (row is null)
                return;
            if (Findings is not null)
                Findings.Filter = row.Filter;
            ShowFindings = true;
        });
        if (findings is not null)
        {
            Findings = new RouteFindingsViewModel(findings);
            Findings.CountChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(FindingsLabel));
                OnPropertyChanged(nameof(FindingsCount));
            };
            findings.Changed += (_, _) => Refresh();
            // #959: a Warning card's "Crosses route" row opens its finding here.
            findings.OpenRequested += (_, _) => ShowFindings = true;
        }
        _service.Changed += (_, _) => Refresh();
        Window.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DepartureWindowViewModel.IsVisible))
                OnPropertyChanged(nameof(ShowWindowNote));
        };
        Refresh();
    }

    /// <summary>The verdict card's departure-window line and <b>Depart {HH:MM}</b> (#903).</summary>
    public DepartureWindowViewModel Window { get; }

    /// <summary>The Findings tab's list (#905), or <c>null</c> without a findings service.</summary>
    public RouteFindingsViewModel? Findings { get; }

    /// <summary>The check-mode leg rows (C5).</summary>
    public ObservableCollection<PassageCheckLegRowViewModel> Legs { get; }

    /// <summary>The verdict card's summary rows (B3): Clearance, Route and, when warnings were checked, Warnings.</summary>
    public ObservableCollection<CheckSummaryRowViewModel> SummaryRows { get; }

    /// <summary>Moves the profile cursor and ghost to the middle of a leg (C5 hover).</summary>
    public ICommand HoverLegCommand { get; }

    /// <summary>"+ Add draft" (A5): expands the Passage card and focuses Draft.</summary>
    public ICommand AddDraftCommand { get; }

    /// <summary>A summary row's click (B3): filters the findings list to that row's checks and shows it.</summary>
    public ICommand SelectSummaryCommand { get; }

    private bool _isActive;
    /// <summary>Whether Check route is on.</summary>
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
    /// Whether the checked route has no draft (#953): the Clearance row says
    /// "Not checked · + Add draft" and the legs have no clearance chips.
    /// </summary>
    public bool NeedsDraft
    {
        get => _needsDraft;
        private set
        {
            if (!SetProperty(ref _needsDraft, value))
                return;
            OnPropertyChanged(nameof(ShowLegsNeedsDraft));
            OnPropertyChanged(nameof(ShowWindowNote));
        }
    }

    /// <summary>Whether the Legs tab shows "Add a draft to check clearance" above the leg rows, which have no chips.</summary>
    public bool ShowLegsNeedsDraft => NeedsDraft && ShowLegs;

    private bool _isStale;
    /// <summary>Whether the shown verdict is being recomputed (B6, #887 C10: the whole card dims).</summary>
    public bool IsStale
    {
        get => _isStale;
        private set
        {
            if (SetProperty(ref _isStale, value))
                OnPropertyChanged(nameof(VerdictOpacity));
        }
    }

    /// <summary>Opacity of the verdict card, rows included: dimmed while stale.</summary>
    public double VerdictOpacity => IsStale ? 0.45 : 1.0;

    private string? _statusText;
    /// <summary>"Checking the passage…" or an error, shown when there is no verdict yet.</summary>
    public string? StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private RouteCheckVerdictKind _kind = RouteCheckVerdictKind.Clear;
    /// <summary>The worst group (decision 3).</summary>
    public RouteCheckVerdictKind Kind
    {
        get => _kind;
        private set => SetProperty(ref _kind, value);
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
    /// <summary>The verdict title, naming the worst item (B1).</summary>
    public string? Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    private string? _subtitle;
    /// <summary>The line under the title (B2), e.g. "Leg 6 → 7 · Calshot Spit · and 2 more to review".</summary>
    public string? Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    private string? _confidenceText;
    /// <summary>
    /// The verdict's secondary line on confidence (#939): "Charted depth from
    /// S-101 on 1,5 NM" and/or "Vertical datums unconfirmed: …", one per line;
    /// <c>null</c> at full confidence or without a draft.
    /// </summary>
    public string? ConfidenceText
    {
        get => _confidenceText;
        private set => SetProperty(ref _confidenceText, value);
    }

    private bool _hasRouteFindings;
    /// <summary>Whether to add "Route-check findings don't change with departure." under the window line (B4).</summary>
    public bool ShowWindowNote => _hasRouteFindings && !NeedsDraft && Window.IsVisible;

    private string? _footerText;
    /// <summary>The one "Not for navigation" line (B5).</summary>
    public string? FooterText
    {
        get => _footerText;
        private set => SetProperty(ref _footerText, value);
    }

    private string? _headerMeta;
    /// <summary>The panel header sub-line in check mode (C2, A4).</summary>
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
            OnPropertyChanged(nameof(SelectedTabIndex));
        }
    }

    /// <summary>The selected tab, Legs (0) or Findings (1), for the tab control.</summary>
    public int SelectedTabIndex
    {
        get => ShowFindings ? 1 : 0;
        set => ShowFindings = value == 1;
    }

    public bool ShowLegs => !ShowFindings;

    /// <summary>The Findings tab label, "Findings {N}" (C4).</summary>
    public string FindingsLabel => string.Format(CultureInfo.CurrentCulture, Strings.Check_TabFindingsFormat, Findings?.Count ?? 0);

    /// <summary>The Findings tab's count, shown in the secondary colour after its label.</summary>
    public string FindingsCount => (Findings?.Count ?? 0).ToString(CultureInfo.CurrentCulture);

    /// <summary>Re-reads the service state.</summary>
    public void Refresh()
    {
        IsActive = _service.IsActive;
        var input = _service.IsActive ? _service.Input : null;
        NeedsDraft = input is { HasDraft: false };
        var current = input is { HasDraft: true } ? _service.Current : null;
        IsStale = (current is not null && _service.IsStale) || (input is not null && (_findings?.IsStale ?? false));
        HeaderMeta = BuildHeaderMeta(input, current);

        var verdict = RouteCheckVerdict.For(_service, _findings, _depthUnit(), _timeFormat());
        if (verdict is null)
        {
            HasVerdict = false;
            Legs.Clear();
            SummaryRows.Clear();
            StatusText = !_service.IsActive ? null
                : _service.Error is { } error ? string.Format(CultureInfo.CurrentCulture, Strings.Check_ErrorFormat, error)
                : _service.IsComputing ? Strings.Check_Computing
                : null;
            return;
        }

        StatusText = null;
        HasVerdict = true;
        Kind = verdict.Kind;
        Tone = verdict.Tone;
        Title = verdict.Title;
        Subtitle = verdict.Subtitle;
        ConfidenceText = current is null ? null : ConfidenceLine(current.Result);
        FooterText = verdict.Footer;
        _hasRouteFindings = verdict.HasRouteFindings;
        OnPropertyChanged(nameof(ShowWindowNote));
        UpdateRows(verdict.Rows);
        if (current is not null)
            RebuildLegs(current);
        else if (input is not null)
            RebuildLegs(input);
        else
            Legs.Clear();
        UpdateBadges(_findings?.Findings ?? []);
    }

    // D4: each leg's worst route-check group and how many findings it has.
    private void UpdateBadges(IReadOnlyList<RouteFinding> findings)
    {
        var route = findings
            .Where(f => f.LegIndex is not null && f.Group != RouteFindingGroup.Info
                && RouteCheckVerdict.FilterOf(f) == FindingsFilter.Route)
            .ToLookup(f => f.LegIndex!.Value);
        foreach (var leg in Legs)
        {
            var onLeg = route[leg.LegIndex].ToList();
            leg.SetBadge(onLeg.Count, onLeg.Count == 0 ? null : onLeg.Min(f => f.Group));
        }
    }

    // D4 without a draft: a row per leg with its times and the badge, no chip.
    private void RebuildLegs(PassageCheckService.CheckInput input)
    {
        var schedule = PassageSchedule.Compute(input.Legs, input.Departure);
        while (Legs.Count > schedule.Legs.Count)
            Legs.RemoveAt(Legs.Count - 1);
        for (var i = 0; i < schedule.Legs.Count; i++)
        {
            var row = i < Legs.Count ? Legs[i] : null;
            if (row is null)
            {
                row = new PassageCheckLegRowViewModel(i);
                Legs.Add(row);
            }
            row.Update(schedule.Legs[i], input.Departure, _timeFormat());
        }
    }

    private void UpdateRows(IReadOnlyList<RouteCheckSummaryRow> rows)
    {
        while (SummaryRows.Count > rows.Count)
            SummaryRows.RemoveAt(SummaryRows.Count - 1);
        for (var i = 0; i < rows.Count; i++)
        {
            if (i < SummaryRows.Count && SummaryRows[i].Filter == rows[i].Filter)
            {
                SummaryRows[i].Update(rows[i]);
                continue;
            }

            var row = new CheckSummaryRowViewModel(rows[i].Filter);
            row.Update(rows[i]);
            if (i < SummaryRows.Count)
                SummaryRows[i] = row;
            else
                SummaryRows.Add(row);
        }
    }

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

    // C2 with a draft: vessel · draft · dep · arr. A4 without: "No draft" · speed · dep · arr,
    // the times only where the route sets them (no departure, no speed: no times).
    private string? BuildHeaderMeta(PassageCheckService.CheckInput? input, PassageCheckSnapshot? current)
    {
        if (input is null || _routes.Routes.ActiveRoute is not { } route)
            return null;

        var parts = new List<string>();
        if (input.HasDraft)
        {
            if (route.Info.Vessel?.Name is { Length: > 0 } vessel)
                parts.Add(vessel);
            if (route.Passage.DraftMeters is { } draft)
                parts.Add(PassageFormatting.Format(draft, PassageQuantity.Depth, _depthUnit()));
            if (current is not null)
                AddTimes(parts, current.Result.Departure, current.Result.Arrival);
        }
        else
        {
            parts.Add(Strings.Check_NoDraftHeader);
            if (route.Passage.SpeedKnots is { } speed)
                parts.Add(PassageFormatting.Format(speed, PassageQuantity.Speed, _depthUnit()));
            if (route.Passage.Departure is not null)
                AddTimes(parts, input.Departure, PassageSchedule.Compute(input.Legs, input.Departure).Arrival);
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private void AddTimes(List<string> parts, DateTimeOffset departure, DateTimeOffset? arrival)
    {
        parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Check_DepartureFormat,
            PassageFormatting.FormatEta(departure, departure, _timeFormat())));
        if (arrival is { } a)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Check_ArrivalFormat,
                PassageFormatting.FormatEta(a, departure, _timeFormat())));
        }
    }

    /// <summary>The clearance verdict's title in the #887 handoff's words (C3).</summary>
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
}

/// <summary>
/// A verdict-card summary row (B3): a 24 px button with a label column, the
/// value and an optional chip. Clicking it filters the findings list.
/// </summary>
internal sealed class CheckSummaryRowViewModel : ViewModelBase
{
    public CheckSummaryRowViewModel(FindingsFilter filter)
    {
        Filter = filter;
    }

    /// <summary>The filter the row sets.</summary>
    public FindingsFilter Filter { get; }

    /// <summary>A stable AutomationId suffix: Clearance, Route or Warnings.</summary>
    public string Key => Filter.ToString();

    private string _label = string.Empty;
    /// <summary>"Clearance", "Route" or "Warnings".</summary>
    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    private string _text = string.Empty;
    /// <summary>The value, e.g. "min 0,4 m · required 1,0 m".</summary>
    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    private string? _chip;
    /// <summary>The chip text, or <c>null</c>.</summary>
    public string? Chip
    {
        get => _chip;
        private set
        {
            if (SetProperty(ref _chip, value))
                OnPropertyChanged(nameof(HasChip));
        }
    }

    public bool HasChip => Chip is not null;

    private CheckTone _chipTone = CheckTone.Neutral;
    /// <summary>The chip's tone.</summary>
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

    private bool _addDraft;
    /// <summary>Whether the row ends with "+ Add draft" (no draft).</summary>
    public bool AddDraft
    {
        get => _addDraft;
        private set => SetProperty(ref _addDraft, value);
    }

    internal void Update(RouteCheckSummaryRow row)
    {
        Label = row.Label;
        Text = row.Text;
        Chip = row.Chip;
        ChipTone = row.ChipTone;
        AddDraft = row.AddDraft;
    }
}

/// <summary>A leg row in Check route mode (C5).</summary>
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

    private bool _hasChip;
    /// <summary>Whether the row has a min-UKC chip: only with a draft (D4: no draft, badge only).</summary>
    public bool HasChip
    {
        get => _hasChip;
        private set => SetProperty(ref _hasChip, value);
    }

    private int _badgeCount;
    /// <summary>How many Must review and Check route-check findings are on the leg (D4).</summary>
    public int BadgeCount
    {
        get => _badgeCount;
        private set
        {
            if (!SetProperty(ref _badgeCount, value))
                return;
            OnPropertyChanged(nameof(HasBadge));
            OnPropertyChanged(nameof(BadgeText));
        }
    }

    /// <summary>Whether the leg shows the route-check badge.</summary>
    public bool HasBadge => BadgeCount > 0;

    /// <summary>The badge's count, e.g. "2".</summary>
    public string BadgeText => BadgeCount.ToString(CultureInfo.CurrentCulture);

    private bool _isBadgeMustReview;
    /// <summary>Whether the leg's worst route-check finding is Must review (octagon, destructive); else Check (triangle, warning).</summary>
    public bool IsBadgeMustReview
    {
        get => _isBadgeMustReview;
        private set
        {
            if (SetProperty(ref _isBadgeMustReview, value))
                OnPropertyChanged(nameof(IsBadgeCheck));
        }
    }

    public bool IsBadgeCheck => !IsBadgeMustReview;

    internal void SetBadge(int count, RouteFindingGroup? worst)
    {
        BadgeCount = count;
        IsBadgeMustReview = worst == RouteFindingGroup.MustReview;
    }

    /// <summary>Updates a row without a draft (D4): name and times, no chip.</summary>
    internal void Update(LegSchedule leg, DateTimeOffset departure, TimeFormat timeFormat)
    {
        Name = string.Format(CultureInfo.CurrentCulture, Strings.Check_LegFormat, LegIndex + 1, LegIndex + 2);
        TimesText = leg.Start is { } start && leg.End is { } end
            ? string.Format(CultureInfo.CurrentCulture, Strings.Check_LegTimesFormat,
                PassageFormatting.FormatEta(start, departure, timeFormat),
                PassageFormatting.FormatEta(end, departure, timeFormat),
                leg.DistanceNauticalMiles)
            : string.Format(CultureInfo.CurrentCulture, Strings.Check_LegDistanceFormat, leg.DistanceNauticalMiles);
        MidDistanceMeters = leg.StartDistanceMeters + (leg.DistanceMeters / 2);
        HasChip = false;
        IsLowConfidence = false;
        ChipText = string.Empty;
        ChipTone = CheckTone.Neutral;
    }

    internal void Update(LegUkcResult leg, string? placeName, DateTimeOffset departure, DepthUnit depthUnit, TimeFormat timeFormat)
    {
        HasChip = true;
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
