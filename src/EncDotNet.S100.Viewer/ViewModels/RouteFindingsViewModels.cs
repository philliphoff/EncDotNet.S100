using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// One entry in a findings group (#959, D2): a single finding, or the Check
/// or Info findings that share a code, merged into one row with a count.
/// </summary>
/// <param name="Id">The finding's id, or for a merged row <c>merged.{group}.{code}</c>.</param>
/// <param name="Group">The group the entry is in.</param>
/// <param name="Members">The findings, in list order; more than one for a merged row.</param>
internal sealed record FindingsEntry(string Id, RouteFindingGroup Group, IReadOnlyList<RouteFinding> Members)
{
    /// <summary>Whether the entry merges several findings.</summary>
    public bool IsMerged => Members.Count > 1;

    /// <summary>
    /// Lays out one group's findings (D2): in Check and Info, findings with
    /// the same code merge into one entry where the first of them is; Must
    /// review never merges.
    /// </summary>
    public static IReadOnlyList<FindingsEntry> For(RouteFindingGroup group, IReadOnlyList<RouteFinding> findings)
    {
        if (group == RouteFindingGroup.MustReview)
            return findings.Select(f => new FindingsEntry(f.Id, group, [f])).ToList();

        var byCode = findings.GroupBy(f => f.Code).ToDictionary(g => g.Key, g => g.ToList());
        var entries = new List<FindingsEntry>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            var members = byCode[finding.Code];
            if (members.Count == 1)
                entries.Add(new FindingsEntry(finding.Id, group, members));
            else if (placed.Add(finding.Code))
                entries.Add(new FindingsEntry(MergedId(group, finding.Code), group, members));
        }

        return entries;
    }

    /// <summary>A merged entry's id, e.g. <c>merged.check.confidence.charted_depth_from_s101</c>.</summary>
    public static string MergedId(RouteFindingGroup group, string code) => $"merged.{GroupCode(group)}.{code}";

    /// <summary>The MCP name of a group: must_review, check or info.</summary>
    public static string GroupCode(RouteFindingGroup group) => group switch
    {
        RouteFindingGroup.MustReview => "must_review",
        RouteFindingGroup.Check => "check",
        _ => "info",
    };

    /// <summary>
    /// A merged row's meta line: the legs it spans and how many stretches
    /// or places, e.g. "Legs 7 → 9 · 9 stretches".
    /// </summary>
    public static string MergedMeta(IReadOnlyList<RouteFinding> members)
    {
        var parts = new List<string>();
        var legs = members.Where(m => m.LegIndex is not null).Select(m => m.LegIndex!.Value).ToList();
        if (legs.Count > 0)
        {
            var (first, last) = (legs.Min(), legs.Max());
            parts.Add(first == last
                ? string.Format(CultureInfo.CurrentCulture, Strings.Check_LegFormat, first + 1, first + 2)
                : string.Format(CultureInfo.CurrentCulture, Strings.Findings_LegsFormat, first + 1, last + 2));
        }

        var spans = members.All(m => m.Location is { IsPoint: false });
        parts.Add(string.Format(CultureInfo.CurrentCulture,
            spans ? Strings.Findings_MergedStretchesFormat : Strings.Findings_MergedPlacesFormat, members.Count));
        return string.Join(" · ", parts);
    }
}

/// <summary>
/// The Findings tab in Check route mode (#905, #959, handoff D1–D3, F1–F5):
/// the checked route's findings grouped MUST REVIEW / CHECK / INFO under the
/// source filter chips, with same-code Check and Info findings merged and
/// INFO folded, projected from <see cref="RouteFindingsService"/>, which also
/// keeps the view (filter, fold, expanded row) so MCP sees the same list.
/// </summary>
internal sealed class RouteFindingsViewModel : ViewModelBase
{
    private readonly RouteFindingsService _service;
    private IReadOnlyList<RouteFinding> _shown = [];
    private List<(RouteFindingGroup Group, bool Open, string Id, bool Expanded, IReadOnlyList<RouteFinding> Members)> _layout = [];

    public RouteFindingsViewModel(RouteFindingsService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        Groups = new ObservableCollection<RouteFindingGroupViewModel>();
        FilterChips = new ObservableCollection<FindingsFilterChipViewModel>(
            Enum.GetValues<FindingsFilter>().Select(f => new FindingsFilterChipViewModel(f)));
        SelectCommand = new RelayCommand<RouteFindingRowViewModel>(row =>
        {
            if (row is null)
                return;
            if (row.IsMerged)
                _service.SetView(expandedId: row.IsExpanded ? null : row.Id, collapse: row.IsExpanded);
            else
                _service.Select(row.Finding);
        });
        SetFilterCommand = new RelayCommand<FindingsFilterChipViewModel>(chip =>
        {
            if (chip is not null)
                Filter = chip.Filter;
        });
        ToggleInfoCommand = new RelayCommand(() => _service.SetView(infoOpen: !_service.InfoOpen));
        CloseWarningCardCommand = new RelayCommand(() =>
        {
            if (_service.Selected?.Warning is not null)
                _service.Select(null, zoom: false);
        });
        OpenInWarningsCommand = new RelayCommand(() =>
        {
            if (_card is not null && WarningEntry(_service.Selected) is { } entry)
                _service.WarningNavigator!.Reveal(entry);
        });
        _service.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The non-empty groups after the filter, in MUST REVIEW, CHECK, INFO order.</summary>
    public ObservableCollection<RouteFindingGroupViewModel> Groups { get; }

    /// <summary>The source filter chips (D1): All · Clearance · Route · Warnings, each with its count.</summary>
    public ObservableCollection<FindingsFilterChipViewModel> FilterChips { get; }

    /// <summary>
    /// A row's click: selects a finding (zooming the map to it and moving the
    /// profile cursor there, F3), or expands or collapses a merged row (D2).
    /// </summary>
    public ICommand SelectCommand { get; }

    /// <summary>Selects a filter chip.</summary>
    public ICommand SetFilterCommand { get; }

    /// <summary>Folds or unfolds the INFO group (D3).</summary>
    public ICommand ToggleInfoCommand { get; }

    /// <summary>Esc in the list: closes the inline Warning card by clearing the selection (E1).</summary>
    public ICommand CloseWarningCardCommand { get; }

    /// <summary>
    /// The inline card's "Open in Navigational warnings" (E1): selects the
    /// warning in that panel and brings the panel forward.
    /// </summary>
    public ICommand OpenInWarningsCommand { get; }

    /// <summary>Raised by the inline Warning card's Copy with the text to put on the clipboard.</summary>
    public event EventHandler<string>? CopyRequested;

    private NavigationalWarningCardViewModel? _card;
    private string? _cardId;

    /// <summary>
    /// The selected S-124 finding's Warning card, shown inline under its row
    /// (#959, E1) with its status at the ETA (E2); <c>null</c> when no S-124
    /// finding is selected. One at a time: selecting another finding, or Esc,
    /// closes it.
    /// </summary>
    public NavigationalWarningCardViewModel? WarningCard => _card;

    /// <summary>The id of the finding whose Warning card is open inline, or <c>null</c>.</summary>
    public string? WarningCardId => _cardId;

    /// <summary>Which findings the list shows (D1); the verdict card's summary rows set it too (B3).</summary>
    public FindingsFilter Filter
    {
        get => _service.Filter;
        set
        {
            if (value != _service.Filter)
                _service.SetView(filter: value);
        }
    }

    private int _count;
    /// <summary>How many findings there are in all; the Findings segment shows it.</summary>
    public int Count
    {
        get => _count;
        private set
        {
            if (SetProperty(ref _count, value))
                OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Whether there are no findings.</summary>
    public bool IsEmpty => Count == 0;

    private bool _isFilteredEmpty;
    /// <summary>Whether there are findings but none passes the filter.</summary>
    public bool IsFilteredEmpty
    {
        get => _isFilteredEmpty;
        private set => SetProperty(ref _isFilteredEmpty, value);
    }

    /// <summary>The findings the list shows, after the filter, in list order.</summary>
    public IReadOnlyList<RouteFinding> Shown => _shown;

    /// <summary>Raised after <see cref="Count"/> may have changed.</summary>
    public event EventHandler? CountChanged;

    /// <summary>The number of findings each filter shows.</summary>
    internal static IReadOnlyDictionary<FindingsFilter, int> Counts(IReadOnlyList<RouteFinding> findings)
    {
        var counts = Enum.GetValues<FindingsFilter>().ToDictionary(f => f, _ => 0);
        counts[FindingsFilter.All] = findings.Count;
        foreach (var finding in findings)
            counts[RouteCheckVerdict.FilterOf(finding)]++;
        return counts;
    }

    /// <summary>Whether a finding passes a filter.</summary>
    internal static bool Passes(RouteFinding finding, FindingsFilter filter) =>
        filter == FindingsFilter.All || RouteCheckVerdict.FilterOf(finding) == filter;

    /// <summary>The list's entries after <paramref name="filter"/>, by group, merged (D2).</summary>
    internal static IReadOnlyList<FindingsEntry> Entries(IReadOnlyList<RouteFinding> findings, FindingsFilter filter) =>
        findings.Where(f => Passes(f, filter))
            .GroupBy(f => f.Group)
            .OrderBy(g => g.Key)
            .SelectMany(g => FindingsEntry.For(g.Key, g.ToList()))
            .ToList();

    /// <summary>Re-reads the service.</summary>
    public void Refresh()
    {
        var findings = _service.Findings;
        var selectedId = _service.Selected?.Id;
        var filter = _service.Filter;
        OnPropertyChanged(nameof(Filter));
        var counts = Counts(findings);
        foreach (var chip in FilterChips)
        {
            chip.Count = counts[chip.Filter];
            chip.IsSelected = chip.Filter == filter;
            // D1: a chip shows only when it has findings (Clearance only with a
            // draft); the selected one stays so the filter can be seen and undone.
            chip.IsVisible = chip.Filter == FindingsFilter.All || chip.IsSelected
                || (chip.Count > 0 && (chip.Filter != FindingsFilter.Clearance || _service.HasDraft));
        }

        _shown = findings.Where(f => Passes(f, filter)).ToList();
        var entries = Entries(findings, filter);
        var infoOpen = _service.InfoOpen;
        var expanded = _service.ExpandedId;
        if (Reveal(entries, infoOpen, expanded))
            return;
        var layout = entries
            .Select(e => (e.Group, e.Group != RouteFindingGroup.Info || infoOpen, e.Id, e.IsMerged && e.Id == expanded, e.Members))
            .ToList();

        // Rebuild only what changed so a selection change doesn't recreate rows.
        var same = layout.Count == _layout.Count
            && layout.Zip(_layout).All(p => p.First.Group == p.Second.Group && p.First.Item2 == p.Second.Open
                && p.First.Id == p.Second.Id && p.First.Item4 == p.Second.Expanded
                && p.First.Members.SequenceEqual(p.Second.Members, ReferenceEqualityComparer.Instance));
        if (!same)
        {
            _layout = layout;
            Groups.Clear();
            foreach (var group in entries.GroupBy(e => e.Group))
            {
                var open = group.Key != RouteFindingGroup.Info || infoOpen;
                var rows = group.Select(e => e.IsMerged
                    ? new RouteFindingRowViewModel(e.Members[0], e.Id, e.Members) { IsExpanded = e.Id == expanded }
                    : new RouteFindingRowViewModel(e.Members[0]));
                Groups.Add(new RouteFindingGroupViewModel(group.Key, rows, group.Sum(e => e.Members.Count), open));
            }
        }

        UpdateWarningCard(_service.Selected);
        foreach (var row in Groups.SelectMany(g => g.Items).SelectMany(r => r.IsMerged ? r.Members : [r]))
        {
            row.IsSelected = row.Finding.Id == selectedId;
            row.WarningCard = row.IsSelected ? _card : null;
        }

        Count = findings.Count;
        IsFilteredEmpty = findings.Count > 0 && _shown.Count == 0;
        CountChanged?.Invoke(this, EventArgs.Empty);
    }

    private (string Id, string Entry, RouteFindingGroup Group)? _revealed;

    // E1: a selected S-124 finding's card stays in view. When it lands in a
    // folded INFO or a collapsed merged row (a recompute moved it, or MCP
    // selected it there), unfold that once; the mariner can fold it again.
    private bool Reveal(IReadOnlyList<FindingsEntry> entries, bool infoOpen, string? expanded)
    {
        if (_service.Selected is not { Warning: not null } selected
            || entries.FirstOrDefault(e => e.Members.Any(m => m.Id == selected.Id)) is not { } entry)
        {
            _revealed = null;
            return false;
        }

        var at = (selected.Id, entry.Id, entry.Group);
        if (_revealed == at)
            return false;
        _revealed = at;
        var unfold = entry.Group == RouteFindingGroup.Info && !infoOpen;
        var expand = entry.IsMerged && entry.Id != expanded;
        if (!unfold && !expand)
            return false;
        _service.SetView(infoOpen: unfold ? true : null, expandedId: expand ? entry.Id : null);
        return true;
    }

    // E1/E2: the selected S-124 finding's card. It's kept while the same
    // finding stays selected, so a recompute (a new departure or speed)
    // re-judges its status at the new ETA without closing it or losing Show all.
    private void UpdateWarningCard(RouteFinding? selected)
    {
        if (selected?.Warning is not { } warning)
        {
            DetachCard();
            return;
        }

        var navigator = _service.WarningNavigator;
        var entry = WarningEntry(selected);
        if (_card is null || _cardId != selected.Id)
        {
            DetachCard();
            _card = new NavigationalWarningCardViewModel(warning.Info, selected.Feature?.DatasetId, _service.TimeFormat, warning.From)
            {
                // E1: the list clips the text at 5 lines. E4: the provider's
                // "not for navigation" label stays off; the verdict carries the disclaimer.
                ClipAt = InlineClipLines,
                IsNew = entry is not null && navigator!.IsNew(entry),
            };
            _cardId = selected.Id;
            _card.CopyRequested += OnCardCopyRequested;
            _card.ZoomRequested += OnCardZoomRequested;
            _card.LinkRequested += OnCardLinkRequested;
            OnPropertyChanged(nameof(WarningCard));
            OnPropertyChanged(nameof(WarningCardId));
        }

        _card.SetTimeFormat(_service.TimeFormat);
        _card.SetViewTime(warning.From);
        _card.SetLinkTargets(navigator?.Warnings ?? []);
        _card.SetWarningSet(navigator?.WarningSet ?? Datasets.Pipelines.NavigationalWarningSet.Empty);
        _card.SetStatusOnRoute(warning, StatusAtEta(warning, _service.Departure, _service.TimeFormat));
        _card.CanZoom = entry is { Extent: not null } || selected.Location is not null;
        CanOpenInWarnings = entry is not null;
    }

    /// <summary>The inline Warning card's text clip, in lines (E1).</summary>
    internal const int InlineClipLines = 5;

    /// <summary>
    /// The inline card's status pill at the ETA (E2): "In force at 06:08",
    /// "Not in force at 06:15" or "Cancelled"; the time in the Local/UTC
    /// preference, with the weekday when it isn't the departure's day.
    /// </summary>
    internal static string StatusAtEta(RouteFindingWarning warning, DateTimeOffset? departure, TimeFormat format)
    {
        var at = PassageFormatting.FormatEta(warning.From, departure ?? warning.From, format);
        return warning.Status switch
        {
            RouteWarningStatus.InForce => string.Format(CultureInfo.CurrentCulture, Strings.Findings_WarningInForceAtFormat, at),
            RouteWarningStatus.NotInForce => string.Format(CultureInfo.CurrentCulture, Strings.Findings_WarningNotInForceAtFormat, at),
            _ => Strings.Findings_WarningCancelled,
        };
    }

    private bool _canOpenInWarnings;
    /// <summary>Whether the inline card's warning is listed in the Navigational warnings panel, which shows its link.</summary>
    public bool CanOpenInWarnings
    {
        get => _canOpenInWarnings;
        private set => SetProperty(ref _canOpenInWarnings, value);
    }

    private NavigationalWarningEntry? WarningEntry(RouteFinding? finding) =>
        finding is { Warning: not null, Feature: { } feature } && _service.WarningNavigator is { } navigator
            ? navigator.Find(feature.DatasetId, feature.FeatureId)
            : null;

    private void DetachCard()
    {
        if (_card is null)
            return;
        _card.CopyRequested -= OnCardCopyRequested;
        _card.ZoomRequested -= OnCardZoomRequested;
        _card.LinkRequested -= OnCardLinkRequested;
        _card = null;
        _cardId = null;
        CanOpenInWarnings = false;
        OnPropertyChanged(nameof(WarningCard));
        OnPropertyChanged(nameof(WarningCardId));
    }

    private void OnCardCopyRequested(object? sender, string text) => CopyRequested?.Invoke(this, text);

    // Zoom to frames the warning as the panel does, or the finding without the panel.
    private void OnCardZoomRequested(object? sender, EventArgs e)
    {
        if (_service.Selected is not { } finding)
            return;
        if (WarningEntry(finding) is { Extent: not null } entry)
            _service.WarningNavigator!.ZoomTo(entry);
        else
            _service.Select(finding);
    }

    private void OnCardLinkRequested(object? sender, NavigationalWarningEntry target) =>
        _service.WarningNavigator?.Reveal(target);
}

/// <summary>A source filter chip (D1): "All 7", "Route 1", "Warnings 6".</summary>
internal sealed class FindingsFilterChipViewModel : ViewModelBase
{
    public FindingsFilterChipViewModel(FindingsFilter filter)
    {
        Filter = filter;
    }

    public FindingsFilter Filter { get; }

    /// <summary>A stable AutomationId suffix: All, Clearance, Route or Warnings.</summary>
    public string Key => Filter.ToString();

    private int _count;
    /// <summary>How many findings the chip shows.</summary>
    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
                OnPropertyChanged(nameof(Label));
        }
    }

    /// <summary>The chip's text, e.g. "Route 1".</summary>
    public string Label => string.Format(CultureInfo.CurrentCulture, Filter switch
    {
        FindingsFilter.Clearance => Strings.Findings_FilterClearanceFormat,
        FindingsFilter.Route => Strings.Findings_FilterRouteFormat,
        FindingsFilter.Warnings => Strings.Findings_FilterWarningsFormat,
        _ => Strings.Findings_FilterAllFormat,
    }, Count);

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}

/// <summary>A group heading and its rows (F1); INFO folds (D3).</summary>
internal sealed class RouteFindingGroupViewModel
{
    public RouteFindingGroupViewModel(RouteFindingGroup group, IEnumerable<RouteFindingRowViewModel> items, int count = -1, bool isOpen = true)
    {
        Group = group;
        Items = [.. items];
        Count = count < 0 ? Items.Count : count;
        IsOpen = isOpen;
    }

    public RouteFindingGroup Group { get; }

    /// <summary>"MUST REVIEW", "CHECK" or "INFO".</summary>
    public string Label => Group switch
    {
        RouteFindingGroup.MustReview => Strings.Findings_GroupMustReview,
        RouteFindingGroup.Check => Strings.Findings_GroupCheck,
        _ => Strings.Findings_GroupInfo,
    };

    /// <summary>A stable AutomationId suffix: MustReview, Check or Info.</summary>
    public string Key => Group.ToString();

    /// <summary>How many findings the group holds (merged rows count each).</summary>
    public int Count { get; }

    /// <summary>The heading's count, e.g. "3".</summary>
    public string CountLabel => Count.ToString(CultureInfo.CurrentCulture);

    /// <summary>Whether the heading folds the group: INFO only (D3).</summary>
    public bool IsFoldable => Group == RouteFindingGroup.Info;

    /// <summary>Whether the rows show: always, except a folded INFO.</summary>
    public bool IsOpen { get; }

    public bool IsFolded => !IsOpen;

    public IReadOnlyList<RouteFindingRowViewModel> Items { get; }
}

/// <summary>The severity icon a finding row shows (F2).</summary>
internal enum RouteFindingIcon
{
    /// <summary>Octagon, destructive: must review.</summary>
    Octagon,

    /// <summary>Triangle, warning: check.</summary>
    Triangle,

    /// <summary>Waves, muted: a confidence finding.</summary>
    Waves,

    /// <summary>Info, muted.</summary>
    Info,
}

/// <summary>One finding row (F2): icon, title, meta line and tag.</summary>
internal sealed class RouteFindingRowViewModel : ViewModelBase
{
    public RouteFindingRowViewModel(RouteFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        Finding = finding;
        Id = finding.Id;
        Members = [];
    }

    /// <summary>A merged row (D2): <paramref name="members"/> share a code; the row expands into their sub-rows.</summary>
    public RouteFindingRowViewModel(RouteFinding first, string id, IReadOnlyList<RouteFinding> members)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(members);
        Finding = first;
        Id = id;
        Members = members.Select(m => new RouteFindingRowViewModel(m) { IsSubRow = true }).ToList();
    }

    /// <summary>The finding, or a merged row's first finding.</summary>
    public RouteFinding Finding { get; }

    /// <summary>The finding's id, or the merged row's <c>merged.{group}.{code}</c>.</summary>
    public string Id { get; }

    /// <summary>A merged row's findings, as sub-rows; empty for a single finding.</summary>
    public IReadOnlyList<RouteFindingRowViewModel> Members { get; }

    /// <summary>Whether the row merges same-code findings (D2).</summary>
    public bool IsMerged => Members.Count > 0;

    /// <summary>Whether this is a merged row's sub-row, indented 24 px.</summary>
    public bool IsSubRow { get; init; }

    /// <summary>"×9" before the tag on a merged row; <c>null</c> otherwise.</summary>
    public string? CountLabel => IsMerged ? string.Format(CultureInfo.CurrentCulture, Strings.Findings_MergedCountFormat, Members.Count) : null;

    /// <summary>Whether a merged row shows its sub-rows.</summary>
    public bool IsExpanded { get; init; }

    /// <summary>Whether to show the sub-rows under this row.</summary>
    public bool ShowMembers => IsMerged && IsExpanded;

    /// <summary>The row's AutomationId: <c>Routes.Check.Finding.Expand</c> for a merged row, else <c>Routes.Check.Finding</c>.</summary>
    public string AutomationId => IsMerged ? "Routes.Check.Finding.Expand" : "Routes.Check.Finding";

    /// <summary>The title; a merged row whose findings are titled differently says "{first} and N more".</summary>
    public string Title => IsMerged && Members.Any(m => m.Finding.Title != Finding.Title)
        ? string.Format(CultureInfo.CurrentCulture, Strings.Findings_AndMoreFormat, Finding.Title, Members.Count - 1)
        : Finding.Title;

    /// <summary>The meta line; a merged row's summarises its findings ("Legs 7 → 9 · 9 stretches").</summary>
    public string Meta => IsMerged ? FindingsEntry.MergedMeta(Members.Select(m => m.Finding).ToList()) : Finding.Meta;

    /// <summary>The check that produced it: "Clearance", "Route check", "S-124", "Confidence" or "Data".</summary>
    public string Tag => TagOf(Finding.Source);

    /// <summary>The severity icon: a confidence finding shows waves, otherwise its group's icon.</summary>
    public RouteFindingIcon Icon => IconOf(Finding);

    public bool IsOctagon => Icon == RouteFindingIcon.Octagon;

    public bool IsTriangle => Icon == RouteFindingIcon.Triangle;

    public bool IsWaves => Icon == RouteFindingIcon.Waves;

    public bool IsInfo => Icon == RouteFindingIcon.Info;

    /// <summary>The icon's tone (F5: destructive only for must-review findings).</summary>
    public CheckTone Tone => ToneOf(Finding);

    public bool IsDestructive => Tone == CheckTone.Destructive;

    public bool IsWarning => Tone == CheckTone.Warning;

    public bool IsNeutral => Tone == CheckTone.Neutral;

    /// <summary>Whether the finding has a place to zoom to.</summary>
    public bool HasLocation => Finding.Location is not null;

    /// <summary>The row's tooltip: what clicking does.</summary>
    public string Tooltip => IsMerged ? Strings.Tooltip_FindingMerged
        : HasLocation ? Strings.Tooltip_FindingRow
        : Strings.Tooltip_FindingRowNoPlace;

    private bool _isSelected;
    /// <summary>Whether this is the selected finding (the selected-row pattern).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private NavigationalWarningCardViewModel? _warningCard;
    /// <summary>The Warning card the row expands into while it's the selected S-124 finding (#959, E1); <c>null</c> otherwise.</summary>
    public NavigationalWarningCardViewModel? WarningCard
    {
        get => _warningCard;
        set
        {
            if (SetProperty(ref _warningCard, value))
                OnPropertyChanged(nameof(HasWarningCard));
        }
    }

    /// <summary>Whether the row shows its inline Warning card.</summary>
    public bool HasWarningCard => _warningCard is not null;

    internal static string TagOf(RouteFindingSource source) => source switch
    {
        RouteFindingSource.Clearance => Strings.Findings_TagClearance,
        RouteFindingSource.RouteCheck => Strings.Findings_TagRouteCheck,
        RouteFindingSource.S124 => Strings.Findings_TagS124,
        RouteFindingSource.Confidence => Strings.Findings_TagConfidence,
        _ => Strings.Findings_TagData,
    };

    internal static RouteFindingIcon IconOf(RouteFinding finding) =>
        finding.Source == RouteFindingSource.Confidence ? RouteFindingIcon.Waves
        : finding.Group switch
        {
            RouteFindingGroup.MustReview => RouteFindingIcon.Octagon,
            RouteFindingGroup.Check => RouteFindingIcon.Triangle,
            _ => RouteFindingIcon.Info,
        };

    internal static CheckTone ToneOf(RouteFinding finding) =>
        finding.Group == RouteFindingGroup.MustReview ? CheckTone.Destructive
        : IconOf(finding) == RouteFindingIcon.Triangle ? CheckTone.Warning
        : CheckTone.Neutral;
}
