using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Which time the Navigational warnings panel reads status at.</summary>
internal enum NavigationalWarningsTimeFilter
{
    /// <summary>The Timeline's view time (now while it is Live); the default, matching the map.</summary>
    ViewTime,

    /// <summary>The wall clock.</summary>
    Now,
}

/// <summary>Which warnings the Navigational warnings panel lists (#959, E3).</summary>
internal enum NavigationalWarningsScope
{
    /// <summary>Every loaded warning part.</summary>
    All,

    /// <summary>
    /// While Check route is on: the parts with a finding on the checked route
    /// (Must review, Check or Info), that is those on or near it.
    /// </summary>
    Route,
}

/// <summary>How the Navigational warnings panel orders rows within a group.</summary>
internal enum NavigationalWarningsSort
{
    /// <summary>Newest number first (year, then number, descending).</summary>
    Number,

    /// <summary>Most recently published first.</summary>
    Published,

    /// <summary>
    /// By the mariner's classification (#996, S-98 §12.12.1 item 2): On
    /// chart, then Off chart, then Information; newest number first within each.
    /// </summary>
    Classification,
}

/// <summary>
/// The Navigational warnings activity panel (#932): every warning part in the
/// loaded S-124 datasets, including those with no location, grouped by series
/// and source. Selecting a row opens the Warning card in Object Information
/// and, when the part has a location and is in force, frames it on the map.
/// </summary>
/// <remarks>
/// Status is read at the view time by default (<see cref="IViewTime"/>), or
/// now. Parts not in force at that time fall into one dimmed group at the
/// bottom, collapsed until opened.
/// <para>
/// While Check route is on (<see cref="RouteScope"/>), each part on or
/// near the checked route says so with its ETA ("On route · 06:08"), and a
/// scope lists only those parts (#959, E3).
/// </para>
/// </remarks>
internal sealed class NavigationalWarningsViewModel : ViewModelBase, INavigationalWarningNavigator, Activities.IActivityTabBadge, IDisposable
{
    private readonly NavigationalWarningsService _service;
    private readonly OpenNavigationalWarningWorkflow _open;
    private readonly ObjectInformationSelection _selection;
    private readonly RouteScopeViewModel _routeScope;
    private readonly NavigationalWarningDrawingService? _drawing;
    private readonly IViewTime? _viewTime;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly TimeProvider _clock;
    private string _query = string.Empty;
    private NavigationalWarningsSort _sort = NavigationalWarningsSort.Number;
    private NavigationalWarningsTimeFilter _timeFilter = NavigationalWarningsTimeFilter.ViewTime;
    private bool _textOnlyFilter;
    private bool _notInForceExpanded;
    private NavigationalWarningListItem? _selectedItem;
    private NavigationalWarningRowItem? _openRow;
    private string? _mapNote;
    private bool _mapNoteIsTextOnly;
    private bool _opening;
    private int _inForceCount;
    private int _notInForceCount;
    private int _textOnlyCount;
    private int _newCount;
    private bool _disposed;

    /// <param name="service">The loaded warning parts.</param>
    /// <param name="open">Opens a part in Object Information and frames it.</param>
    /// <param name="selection">What Object Information shows: a warning picked on the map is selected in the list.</param>
    /// <param name="routeFindings">The checked route's findings, for the route scope (#959 E3); <see langword="null"/> where the host has no route check.</param>
    /// <param name="drawing">Which warnings the map draws (#993), for the "Not in force" group's wording; <see langword="null"/> says nothing about drawing.</param>
    /// <param name="newOptions">The NEW options (#997); <see langword="null"/> when not offered.</param>
    /// <param name="viewTime">The Timeline's view time; <see langword="null"/> reads status now.</param>
    /// <param name="timeFormat">The Local/UTC setting; <see langword="null"/> shows local times.</param>
    /// <param name="clock">The clock.</param>
    public NavigationalWarningsViewModel(
        NavigationalWarningsService service,
        OpenNavigationalWarningWorkflow open,
        ObjectInformationSelection selection,
        RouteFindingsService? routeFindings,
        NavigationalWarningDrawingService? drawing,
        NavigationalWarningNewOptionsViewModel? newOptions,
        IViewTime? viewTime,
        ITimeFormatProvider? timeFormat,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(clock);
        _service = service;
        _open = open;
        _selection = selection;
        _drawing = drawing;
        NewOptions = newOptions;
        _viewTime = viewTime;
        _timeFormat = timeFormat;
        _clock = clock;
        _routeScope = new RouteScopeViewModel(service, routeFindings);
        _routeScope.ScopeSwitched += OnScopeSwitched;
        _routeScope.RouteChanged += OnRouteChanged;

        ToggleTextOnlyCommand = new RelayCommand(() => IsTextOnlyFilter = !IsTextOnlyFilter);
        ToggleNotInForceCommand = new RelayCommand(() => IsNotInForceExpanded = !IsNotInForceExpanded);
        ShowNotInForceCommand = new RelayCommand(() => IsNotInForceExpanded = true);
        ClearQueryCommand = new RelayCommand(() => Query = string.Empty);
        OpenLibraryCommand = new RelayCommand(() => OpenLibraryRequested?.Invoke(this, EventArgs.Empty));
        TurnOnDrawingCommand = new RelayCommand(() =>
        {
            if (_drawing is not null)
                _drawing.ShowNavigationalWarnings = true;
        });
        RefreshCommand = new RelayCommand(() => RefreshSources(), () => CanRefresh);

        _service.Changed += OnServiceChanged;
        if (_viewTime is not null)
            _viewTime.Changed += OnViewTimeChanged;
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged += OnTimeFormatChanged;
        _selection.HitChanged += OnSelectionChanged;
        if (_drawing is not null)
            _drawing.Changed += OnDrawingChanged;
        Rebuild();
    }

    /// <summary>Which warnings the map draws (#993); <see langword="null"/> where the host draws none.</summary>
    internal NavigationalWarningDrawingService? Drawing => _drawing;

    /// <summary>The route scope: each part's relation to the checked route, and the "On {route}" switch.</summary>
    public RouteScopeViewModel RouteScope => _routeScope;

    private void OnScopeSwitched(object? sender, EventArgs e) => Rebuild();

    private void OnRouteChanged(object? sender, EventArgs e)
    {
        Rebuild();
        // Open Warning cards update their "Crosses route" row.
        WarningsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The Local/UTC preference the panel's times are shown in.</summary>
    internal TimeFormat TimeFormat => _timeFormat?.Current ?? TimeFormat.Local;

    /// <inheritdoc />
    public RouteFinding? RouteFindingFor(NavigationalWarningEntry entry) => _routeScope.RouteFindingFor(entry);

    /// <inheritdoc />
    public bool OpenRouteFinding(RouteFinding finding) => _routeScope.OpenRouteFinding(finding);

    /// <summary>
    /// A part's line while Check route is on: "On route · 06:08" when it
    /// crosses, "Near route · 06:19" when near; without speeds, no time.
    /// </summary>
    internal static string RouteText(WarningDetails warning, DateTimeOffset? departure, TimeFormat format)
    {
        var crosses = warning.Relation == RouteWarningRelation.Crosses;
        if (!warning.AtEta)
            return crosses ? Strings.NavWarnings_OnRoute : Strings.NavWarnings_NearRoute;
        var at = PassageFormatting.FormatEta(warning.From, departure ?? warning.From, format);
        return string.Format(CultureInfo.CurrentCulture, crosses ? Strings.NavWarnings_OnRouteAtFormat : Strings.NavWarnings_NearRouteAtFormat, at);
    }

    /// <inheritdoc />
    public bool Reveal(NavigationalWarningEntry entry)
    {
        var opened = Select(entry);
        if (opened)
            RevealRequested?.Invoke(this, EventArgs.Empty);
        return opened;
    }

    /// <summary>Raised by <see cref="Reveal"/>: the window brings the panel forward.</summary>
    public event EventHandler? RevealRequested;

    /// <inheritdoc />
    public IReadOnlyList<NavigationalWarningEntry> Warnings => _service.Warnings;

    /// <inheritdoc />
    public NavigationalWarningSet WarningSet => _service.Set;

    /// <inheritdoc />
    public event EventHandler? WarningsChanged;

    /// <inheritdoc />
    public NavigationalWarningEntry? Find(string datasetId, string featureRef) => _service.Find(datasetId, featureRef);

    /// <inheritdoc />
    public bool IsNew(NavigationalWarningEntry entry) => _service.IsNew(entry);

    /// <inheritdoc />
    public NavigationalWarningClassification ClassificationOf(NavigationalWarningEntry entry) => _service.ClassificationOf(entry);

    /// <inheritdoc />
    public bool SetClassification(NavigationalWarningEntry entry, NavigationalWarningClassification classification) =>
        _service.SetClassification(entry, classification);

    /// <inheritdoc />
    public bool ZoomTo(NavigationalWarningEntry entry) => _open.Frame(entry);

    /// <summary>The list: group headers followed by their rows, in display order.</summary>
    public ObservableCollection<NavigationalWarningListItem> Items { get; } = [];

    /// <summary>Search text over number, type and area.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? string.Empty))
                Rebuild();
        }
    }

    /// <summary>The row order within a group.</summary>
    public NavigationalWarningsSort Sort
    {
        get => _sort;
        set
        {
            if (!SetProperty(ref _sort, value))
                return;
            OnPropertyChanged(nameof(SortIndex));
            Rebuild();
        }
    }

    /// <summary>The sort as a combo-box index (0 Number, 1 Published, 2 Classification).</summary>
    public int SortIndex
    {
        get => (int)_sort;
        set => Sort = value switch
        {
            1 => NavigationalWarningsSort.Published,
            2 => NavigationalWarningsSort.Classification,
            _ => NavigationalWarningsSort.Number,
        };
    }

    /// <summary>Which time status is read at.</summary>
    public NavigationalWarningsTimeFilter TimeFilter
    {
        get => _timeFilter;
        set
        {
            if (!SetProperty(ref _timeFilter, value))
                return;
            OnPropertyChanged(nameof(IsViewTimeFilter));
            OnPropertyChanged(nameof(IsNowFilter));
            OnPropertyChanged(nameof(ViewTimeText));
            OnPropertyChanged(nameof(NoneInForceText));
            Rebuild();
        }
    }

    /// <summary>Whether status is read at the view time.</summary>
    public bool IsViewTimeFilter
    {
        get => _timeFilter == NavigationalWarningsTimeFilter.ViewTime;
        set { if (value) TimeFilter = NavigationalWarningsTimeFilter.ViewTime; }
    }

    /// <summary>Whether status is read now.</summary>
    public bool IsNowFilter
    {
        get => _timeFilter == NavigationalWarningsTimeFilter.Now;
        set { if (value) TimeFilter = NavigationalWarningsTimeFilter.Now; }
    }

    /// <summary>The time status is read at (UTC).</summary>
    public DateTimeOffset StatusTime => _timeFilter == NavigationalWarningsTimeFilter.Now || _viewTime is null
        ? _clock.GetUtcNow()
        : _viewTime.Current;

    /// <summary>The view time beside the filter, in the Local/UTC preference; empty for Now.</summary>
    public string ViewTimeText => IsViewTimeFilter
        ? (_timeFormat?.Current == TimeFormat.Utc ? StatusTime.ToUniversalTime() : StatusTime.ToLocalTime())
            .ToString("d MMM HH:mm", CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>Whether only parts with no location are listed.</summary>
    public bool IsTextOnlyFilter
    {
        get => _textOnlyFilter;
        set
        {
            if (SetProperty(ref _textOnlyFilter, value))
                Rebuild();
        }
    }

    /// <summary>Whether the "Not in force" group shows its rows.</summary>
    public bool IsNotInForceExpanded
    {
        get => _notInForceExpanded;
        set
        {
            if (SetProperty(ref _notInForceExpanded, value))
                Rebuild();
        }
    }

    /// <summary>"N in force".</summary>
    public string InForceText => string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_InForceFormat, _inForceCount);

    /// <summary>"N new": the in-force parts whose warnings are new.</summary>
    public string NewText => string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_NewFormat, _newCount);

    /// <summary>Number of in-force parts whose warnings are new.</summary>
    public int NewCount => _newCount;

    /// <summary>Whether the summary shows "· N new".</summary>
    public bool HasNew => _newCount > 0;

    /// <summary>The NEW options (#997): filters on when NEW shows and "Mark new since"; <c>null</c> when not offered.</summary>
    public NavigationalWarningNewOptionsViewModel? NewOptions { get; }

    /// <inheritdoc />
    public int BadgeCount => _newCount;

    /// <inheritdoc />
    public string BadgeName => NewText;

    /// <summary>Whether Refresh has a source to read again (a listed warning came from a SECOM service or online feed).</summary>
    public bool CanRefresh => _service.CanRefreshSources;

    /// <summary>Reads the sources of the listed warnings again, through the Library's refresh and sync.</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Reads every source again; returns how many were asked.</summary>
    public int RefreshSources()
    {
        using var cmd = ViewerObservability.BeginCommand("navwarnings.refresh");
        return _service.RefreshSources();
    }

    /// <summary>"N text-only": the in-force parts with no location.</summary>
    public string TextOnlyText => string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_TextOnlyFormat, _textOnlyCount);

    /// <summary>Number of parts in force at <see cref="StatusTime"/>.</summary>
    public int InForceCount => _inForceCount;

    /// <summary>Number of parts not in force at <see cref="StatusTime"/>.</summary>
    public int NotInForceCount => _notInForceCount;

    /// <summary>Number of in-force parts with no location.</summary>
    public int TextOnlyCount => _textOnlyCount;

    /// <summary>Whether the summary line shows (there are warnings to count).</summary>
    public bool HasSummary => _service.Warnings.Count > 0;

    /// <summary>Loading: an S-124 dataset is still loading and nothing is listed yet.</summary>
    public bool IsLoadingState => _service.IsLoading && _service.Warnings.Count == 0;

    /// <summary>No sources: no S-124 dataset is loaded or loading.</summary>
    public bool IsNoSourcesState => !_service.IsLoading && !_service.HasWarningDatasets;

    /// <summary>S-124 datasets are loaded but hold no warning parts.</summary>
    public bool IsEmptyState => !IsLoadingState && _service.HasWarningDatasets && _service.Warnings.Count == 0;

    /// <summary>None in force: every listed part is outside its validity at <see cref="StatusTime"/>.</summary>
    public bool IsNoneInForceState =>
        _service.Warnings.Count > 0 && _inForceCount == 0 && string.IsNullOrWhiteSpace(_query) && !_textOnlyFilter && !_routeScope.IsRouteScope;

    /// <summary>"No warnings in force at view time." or "… now."</summary>
    public string NoneInForceText => IsNowFilter ? Strings.NavWarnings_NoneInForceNow : Strings.NavWarnings_NoneInForceViewTime;

    /// <summary>"Show N not in force".</summary>
    public string ShowNotInForceText =>
        string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_ShowNotInForceFormat, _notInForceCount);

    /// <summary>
    /// Whether the line "Not drawn: Navigational warnings are off in Display
    /// controls" shows: warnings are loaded and the Display controls switch
    /// hides them all on the map (#995). The list still works.
    /// </summary>
    public bool IsSwitchedOff => _drawing is { ShowNavigationalWarnings: false } && _service.HasWarningDatasets;

    /// <summary>Turns the Display controls' Navigational warnings switch back on.</summary>
    public ICommand TurnOnDrawingCommand { get; }

    /// <summary>Whether the search found nothing.</summary>
    public bool IsNoMatchesState => _service.Warnings.Count > 0 && Items.Count == 0 && !IsNoneInForceState && !_routeScope.IsNoneOnRouteState;

    /// <summary>Whether the list shows (as opposed to a state message only).</summary>
    public bool HasList => Items.Count > 0;

    /// <summary>
    /// The selected list item. Selecting a row opens it (see
    /// <see cref="Open"/>); a group header can't be selected.
    /// </summary>
    public NavigationalWarningListItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value is not null and not NavigationalWarningRowItem)
            {
                // Headers aren't selectable: keep the previous selection.
                OnPropertyChanged();
                return;
            }

            if (!SetProperty(ref _selectedItem, value) || value is not NavigationalWarningRowItem row || _opening)
                return;
            Open(row);
        }
    }

    /// <summary>
    /// The in-map note while the open part draws nothing or isn't framed:
    /// "… has no location, so nothing is drawn for it." or "… isn't in force
    /// at view time, so the map wasn't moved."; <c>null</c> otherwise.
    /// </summary>
    public string? MapNote
    {
        get => _mapNote;
        private set
        {
            if (SetProperty(ref _mapNote, value))
                OnPropertyChanged(nameof(HasMapNote));
        }
    }

    /// <summary>Whether <see cref="MapNote"/> shows.</summary>
    public bool HasMapNote => _mapNote is not null;

    /// <summary>Whether the map note is for a part with no location (it carries the document glyph).</summary>
    public bool IsMapNoteTextOnly
    {
        get => _mapNoteIsTextOnly;
        private set => SetProperty(ref _mapNoteIsTextOnly, value);
    }

    /// <summary>Toggles the text-only filter (the summary's count).</summary>
    public ICommand ToggleTextOnlyCommand { get; }

    /// <summary>Opens or closes the "Not in force" group.</summary>
    public ICommand ToggleNotInForceCommand { get; }

    /// <summary>Opens the "Not in force" group (the none-in-force state's action).</summary>
    public ICommand ShowNotInForceCommand { get; }

    /// <summary>Clears the search.</summary>
    public ICommand ClearQueryCommand { get; }

    /// <summary>Asks the window to show the Library panel.</summary>
    public ICommand OpenLibraryCommand { get; }

    /// <summary>Raised by <see cref="OpenLibraryCommand"/>.</summary>
    public event EventHandler? OpenLibraryRequested;

    /// <summary>
    /// Opens a row: the Warning card in Object Information, and, when the part
    /// has a location and is in force, the map framed on it. A part that isn't
    /// in force leaves the map where it is; a part with no location shows the
    /// in-map note.
    /// </summary>
    /// <param name="row">The row to open.</param>
    /// <returns>Whether the part was found in its dataset and opened.</returns>
    public bool Open(NavigationalWarningRowItem row)
    {
        ArgumentNullException.ThrowIfNull(row);
        using var cmd = ViewerObservability.BeginCommand("navwarnings.open");
        var entry = row.Entry;
        _openRow = row;

        _opening = true;
        try
        {
            if (!ReferenceEquals(_selectedItem, row))
            {
                _selectedItem = row;
                OnPropertyChanged(nameof(SelectedItem));
            }

            // Frames the map only on a part in force; opening the card is
            // reading it, so the warning is no longer new.
            var outcome = _open.Open(entry, frame: row.IsInForce);
            if (!outcome.Opened)
                cmd.SetStatus(false, "warning part not found");
        }
        finally
        {
            _opening = false;
        }

        var label = entry.Warning.Label ?? entry.DatasetId;
        if (entry.IsTextOnly)
        {
            IsMapNoteTextOnly = true;
            MapNote = string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_MapNoteNoLocation, label);
        }
        else if (!row.IsInForce)
        {
            IsMapNoteTextOnly = false;
            MapNote = string.Format(
                CultureInfo.CurrentCulture,
                IsNowFilter ? Strings.NavWarnings_MapNoteNotInForceNow : Strings.NavWarnings_MapNoteNotInForceViewTime,
                label);
        }
        else
        {
            MapNote = null;
        }

        return true;
    }

    /// <summary>
    /// Opens a listed part, first clearing the search or text-only filter, or
    /// opening the "Not in force" group, when they hide its row.
    /// </summary>
    /// <param name="entry">A part from <see cref="NavigationalWarningsService.Warnings"/>.</param>
    /// <returns>Whether the part was found in its dataset and opened.</returns>
    public bool Select(NavigationalWarningEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var row = FindRow(entry);
        if (row is null)
        {
            if (!Matches(entry, _query))
                _query = string.Empty;
            if (_textOnlyFilter && !entry.IsTextOnly)
                _textOnlyFilter = false;
            if (_service.StatusAt(entry, StatusTime).Status != NavigationalWarningStatus.InForce)
                _notInForceExpanded = true;
            if (_routeScope.IsRouteScope && RouteFindingFor(entry) is null)
                _routeScope.ShowAll();

            OnPropertyChanged(nameof(Query));
            OnPropertyChanged(nameof(IsTextOnlyFilter));
            OnPropertyChanged(nameof(IsNotInForceExpanded));
            Rebuild();
            row = FindRow(entry);
        }

        return row is not null && Open(row);
    }

    private NavigationalWarningRowItem? FindRow(NavigationalWarningEntry entry) =>
        Items.OfType<NavigationalWarningRowItem>().FirstOrDefault(r =>
            string.Equals(r.Entry.DatasetId, entry.DatasetId, StringComparison.Ordinal)
            && string.Equals(r.Entry.FeatureRef, entry.FeatureRef, StringComparison.Ordinal));

    private void OnServiceChanged(object? sender, EventArgs e)
    {
        Rebuild();
        WarningsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnViewTimeChanged(object? sender, EventArgs e)
    {
        if (!IsViewTimeFilter)
            return;
        OnPropertyChanged(nameof(ViewTimeText));
        Rebuild();
    }

    private void OnTimeFormatChanged(TimeFormat format)
    {
        OnPropertyChanged(nameof(ViewTimeText));
        Rebuild();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_opening)
            return;

        // A pick elsewhere replaces the open warning: its note goes, and a
        // warning picked on the map is selected in the list.
        var hit = _selection.Hit;
        var row = hit is { NavigationalWarning: not null, DatasetFileName: { } datasetId }
            ? Items.OfType<NavigationalWarningRowItem>().FirstOrDefault(r =>
                string.Equals(r.Entry.DatasetId, datasetId, StringComparison.Ordinal)
                && string.Equals(r.Entry.FeatureRef, hit.FeatureRef, StringComparison.Ordinal))
            : null;
        if (row is not null && ReferenceEquals(row, _openRow))
            return;

        _openRow = row;
        MapNote = null;
        if (!ReferenceEquals(_selectedItem, row))
        {
            _selectedItem = row;
            OnPropertyChanged(nameof(SelectedItem));
        }

        if (row is not null)
            _service.MarkSeen(row.Entry);
    }

    /// <summary>"Listed in NAVAREA X 110/26 but not received: 103/26, 107/26".</summary>
    internal static string NotReceivedText(NavigationalWarningBulletinReport report) =>
        string.Format(
            CultureInfo.CurrentCulture,
            Strings.NavWarnings_NotReceivedFormat,
            report.Bulletin.Label ?? report.Series,
            string.Join(", ", report.NotReceived.Select(NumberText)));

    /// <summary>"103/26": the number and two-digit year, as a series group lists it.</summary>
    internal static string NumberText(NavigationalWarningReference reference) =>
        reference.Year is { } year
            ? $"{reference.Number.ToString("D3", CultureInfo.InvariantCulture)}/{(year % 100).ToString("D2", CultureInfo.InvariantCulture)}"
            : reference.Number.ToString("D3", CultureInfo.InvariantCulture);

    private void OnDrawingChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        if (_disposed)
            return;

        var time = StatusTime;
        var format = _timeFormat?.Current ?? TimeFormat.Local;
        var all = _service.Warnings;
        var inForce = new List<NavigationalWarningEntry>();
        var notInForce = new List<NavigationalWarningEntry>();
        var states = new Dictionary<NavigationalWarningEntry, NavigationalWarningState>(ReferenceEqualityComparer.Instance);
        foreach (var w in all)
        {
            var state = _service.StatusAt(w, time);
            states[w] = state;
            (state.Status == NavigationalWarningStatus.InForce ? inForce : notInForce).Add(w);
        }

        _inForceCount = inForce.Count;
        _notInForceCount = notInForce.Count;
        _textOnlyCount = inForce.Count(w => w.IsTextOnly);
        _newCount = inForce.Count(_service.IsNew);
        var now = _clock.GetUtcNow();

        var routeScope = _routeScope.IsRouteScope;
        var departure = _routeScope.Departure;
        bool Listed(NavigationalWarningEntry w) =>
            (!_textOnlyFilter || w.IsTextOnly) && Matches(w, _query) && (!routeScope || RouteFindingFor(w) is not null);
        string? OnRouteText(NavigationalWarningEntry w) =>
            RouteFindingFor(w)?.Details is WarningDetails onRoute ? RouteText(onRoute, departure, format) : null;

        var items = new List<NavigationalWarningListItem>();
        // A series appears once per source: a Library source by id (its name
        // and when it last answered), a file opened directly by its issuer.
        var groups = inForce.Where(Listed)
            .GroupBy(w => (
                Series: w.Warning.Series ?? Strings.NavWarnings_NoSeries,
                SourceKey: w.Source is { } source ? source.Key : "file:" + (w.Warning.Issuer ?? string.Empty)))
            .Select(g => (g.Key.Series, g.First().Source, Issuer: g.First().Warning.Issuer ?? string.Empty, Rows: g))
            .OrderBy(g => g.Series, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Source?.Name ?? g.Issuer, StringComparer.CurrentCultureIgnoreCase);
        var staleShown = new HashSet<Guid>();
        // §9.2.2: a series' bulletin names warnings that aren't loaded; say so once, on its first group.
        var notReceived = _service.Set.LatestBulletins(time)
            .Where(r => r.NotReceived.Count > 0)
            .ToList();
        foreach (var group in groups)
        {
            var missing = notReceived.FirstOrDefault(r => NavigationalWarningReference.SameSeries(r.Series, group.Series));
            if (missing is not null)
                notReceived.Remove(missing);
            // An unreachable source says so once, at the top of its first group.
            if (group.Source is { IsStale: true } stale && staleShown.Add(stale.Id))
                items.Add(new NavigationalWarningStaleItem(stale, StaleText(stale, format, now), new RelayCommand(() => _service.RefreshSources([stale.Id]))));
            var rows = Ordered(group.Rows).ToList();
            var sourceText = group.Source is { } from ? SourceText(from, format, now) : group.Issuer;
            items.Add(new NavigationalWarningGroupItem(group.Series, rows.Count, sourceText, isNotInForce: false, isExpanded: true, command: null)
            {
                IsStale = group.Source?.IsStale == true,
                SourceId = group.Source?.Id,
                NotReceivedText = missing is null ? null : NotReceivedText(missing),
            });
            items.AddRange(rows.Select(w => Row(w, states[w], time, format, isNew: _service.IsNew(w), routeText: OnRouteText(w))));
        }

        // A series whose bulletin has no parts, and nothing else in force, still gets its note.
        if (string.IsNullOrWhiteSpace(_query) && !_textOnlyFilter)
        {
            foreach (var orphan in notReceived)
            {
                items.Add(new NavigationalWarningGroupItem(orphan.Series, 0, orphan.Bulletin.Issuer ?? string.Empty, isNotInForce: false, isExpanded: true, command: null)
                {
                    NotReceivedText = NotReceivedText(orphan),
                });
            }
        }

        var outRows = Ordered(notInForce.Where(Listed)).ToList();
        // On the route, the few parts not in force at the panel's time show
        // unfolded: they may well be in force when the vessel gets there.
        var outExpanded = _notInForceExpanded || routeScope;
        if (outRows.Count > 0)
        {
            // The map draws at the view time (#993): say so when the panel does
            // too, unless the switch draws none (#995), which the panel says above.
            var drawnNote = IsNowFilter || _drawing is null or { ShowNavigationalWarnings: false }
                ? string.Empty
                : _drawing.ShowNotYetInForce ? Strings.NavWarnings_NotInForceEndedNotDrawn : Strings.NavWarnings_NotInForceNotDrawn;
            items.Add(new NavigationalWarningGroupItem(
                IsNowFilter ? Strings.NavWarnings_NotInForceGroupNow : Strings.NavWarnings_NotInForceGroupViewTime,
                outRows.Count,
                drawnNote,
                isNotInForce: true,
                isExpanded: outExpanded,
                command: ToggleNotInForceCommand));
            if (outExpanded)
                items.AddRange(outRows.Select(w => Row(w, states[w], time, format, isDrawn: _drawing?.IsDrawn(w) == true, routeText: OnRouteText(w))));
        }

        // Keep the open part selected across rebuilds.
        var selectedKey = (_selectedItem as NavigationalWarningRowItem)?.Entry;
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        var reselected = selectedKey is null ? null : FindRow(selectedKey);
        _selectedItem = reselected;
        if (_openRow is not null && reselected is not null
            && ReferenceEquals(_openRow.Entry.Dataset, reselected.Entry.Dataset)
            && _openRow.Entry.FeatureRef == reselected.Entry.FeatureRef)
        {
            _openRow = reselected;
        }

        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(InForceText));
        OnPropertyChanged(nameof(TextOnlyText));
        OnPropertyChanged(nameof(InForceCount));
        OnPropertyChanged(nameof(NotInForceCount));
        OnPropertyChanged(nameof(TextOnlyCount));
        OnPropertyChanged(nameof(NewText));
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(HasNew));
        OnPropertyChanged(nameof(BadgeCount));
        OnPropertyChanged(nameof(BadgeName));
        OnPropertyChanged(nameof(CanRefresh));
        ((RelayCommand)RefreshCommand).NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(IsLoadingState));
        OnPropertyChanged(nameof(IsNoSourcesState));
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(IsNoneInForceState));
        OnPropertyChanged(nameof(NoneInForceText));
        OnPropertyChanged(nameof(ShowNotInForceText));
        OnPropertyChanged(nameof(IsNoMatchesState));
        OnPropertyChanged(nameof(HasList));
        _routeScope.RaiseCounts();
        OnPropertyChanged(nameof(IsSwitchedOff));
    }

    /// <summary>A source's group-header text at the panel's time format.</summary>
    internal string SourceHeader(NavigationalWarningSource source) =>
        SourceText(source, _timeFormat?.Current ?? TimeFormat.Local, _clock.GetUtcNow());

    /// <summary>"AMSA · 07:40", "AMSA · 07:40, stale", or the name alone when the time is unknown.</summary>
    internal static string SourceText(NavigationalWarningSource source, TimeFormat format, DateTimeOffset now)
    {
        if (source.ReceivedAt is not { } received)
            return source.IsStale ? string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_SourceStaleNoTimeFormat, source.Name) : source.Name;
        return string.Format(
            CultureInfo.CurrentCulture,
            source.IsStale ? Strings.NavWarnings_SourceStaleFormat : Strings.NavWarnings_SourceReceivedFormat,
            source.Name,
            ReceivedText(received, format, now));
    }

    /// <summary>"AMSA hasn't answered since 07:40. You're seeing the warnings received then."</summary>
    internal static string StaleText(NavigationalWarningSource source, TimeFormat format, DateTimeOffset now) =>
        source.ReceivedAt is { } received
            ? string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_StaleFormat, source.Name, ReceivedText(received, format, now))
            : string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_StaleNoTimeFormat, source.Name);

    /// <summary>"07:40" today, else "13 Oct 07:40", in the Local/UTC preference.</summary>
    internal static string ReceivedText(DateTimeOffset received, TimeFormat format, DateTimeOffset now)
    {
        var at = format == TimeFormat.Utc ? received.ToUniversalTime() : received.ToLocalTime();
        var today = format == TimeFormat.Utc ? now.ToUniversalTime() : now.ToLocalTime();
        return at.ToString(at.Date == today.Date ? "HH:mm" : "d MMM HH:mm", CultureInfo.CurrentCulture);
    }

    private IEnumerable<NavigationalWarningEntry> Ordered(IEnumerable<NavigationalWarningEntry> warnings) => _sort switch
    {
        NavigationalWarningsSort.Published => warnings.OrderByDescending(w => w.Warning.PublicationTime ?? DateTimeOffset.MinValue)
            .ThenByDescending(w => NumberKey(w.Warning.Number)),
        NavigationalWarningsSort.Classification => warnings.OrderBy(_service.ClassificationOf)
            .ThenByDescending(w => NumberKey(w.Warning.Number))
            .ThenByDescending(w => w.Warning.PublicationTime ?? DateTimeOffset.MinValue),
        _ => warnings.OrderByDescending(w => NumberKey(w.Warning.Number))
            .ThenByDescending(w => w.Warning.PublicationTime ?? DateTimeOffset.MinValue),
    };

    // A row's classification and the command its menu sets it with (#996).
    private NavigationalWarningRowItem Row(NavigationalWarningEntry w, NavigationalWarningState state, DateTimeOffset time, TimeFormat format, bool isNew = false, bool isDrawn = false, string? routeText = null) =>
        new(w, state, time, format)
        {
            IsNew = isNew,
            IsDrawn = isDrawn,
            RouteText = routeText,
            Classification = _service.ClassificationOf(w),
            ClassifyCommand = new RelayCommand<NavigationalWarningClassification>(c => _service.SetClassification(w, c)),
        };

    /// <summary>A sort key for "106/26": year then number; parts with no number sort last.</summary>
    internal static (int Year, int Number) NumberKey(string? number)
    {
        if (string.IsNullOrEmpty(number))
            return (int.MinValue, int.MinValue);
        var slash = number.IndexOf('/', StringComparison.Ordinal);
        var n = int.TryParse(slash < 0 ? number : number[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : int.MinValue;
        var y = slash >= 0 && int.TryParse(number[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : int.MinValue;
        return (y, n);
    }

    /// <summary>Whether a part matches the search: its number, label, type, title or area.</summary>
    internal static bool Matches(NavigationalWarningEntry entry, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        var q = query.Trim();
        var w = entry.Warning;
        return Has(w.Label, q) || Has(w.Number, q) || Has(w.WarningType, q) || Has(w.GeneralType, q)
            || Has(w.Title, q) || Has(w.GeneralArea, q) || Has(w.Locality, q) || Has(entry.DatasetId, q);

        static bool Has(string? text, string q) => text?.Contains(q, StringComparison.CurrentCultureIgnoreCase) == true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _service.Changed -= OnServiceChanged;
        if (_viewTime is not null)
            _viewTime.Changed -= OnViewTimeChanged;
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged -= OnTimeFormatChanged;
        _selection.HitChanged -= OnSelectionChanged;
        if (_drawing is not null)
            _drawing.Changed -= OnDrawingChanged;
        _routeScope.ScopeSwitched -= OnScopeSwitched;
        _routeScope.RouteChanged -= OnRouteChanged;
        _routeScope.Dispose();
    }
}

/// <summary>An item in the Navigational warnings list: a group header or a row.</summary>
internal abstract class NavigationalWarningListItem : ViewModelBase
{
    /// <summary>The item's accessible name.</summary>
    public abstract string AccessibleName { get; }

    /// <summary>The list row's minimum height: headers are shorter than rows.</summary>
    public abstract double ContainerMinHeight { get; }
}

/// <summary>A group header: series (or "Not in force …"), count and source.</summary>
internal sealed class NavigationalWarningGroupItem : NavigationalWarningListItem
{
    public NavigationalWarningGroupItem(string name, int count, string source, bool isNotInForce, bool isExpanded, ICommand? command)
    {
        Name = name;
        Count = count;
        Source = source;
        IsNotInForce = isNotInForce;
        IsExpanded = isExpanded;
        ToggleCommand = command;
    }

    /// <summary>The series name, or the "Not in force" group's label.</summary>
    public string Name { get; }

    /// <summary>The number of rows in the group.</summary>
    public int Count { get; }

    /// <summary>Where the group's warnings came from: "AMSA · 07:40" for a Library source, the issuing authority for a file, or empty.</summary>
    public string Source { get; }

    /// <summary>Whether the group's source didn't answer its latest refresh.</summary>
    public bool IsStale { get; init; }

    /// <summary>
    /// What the series' latest in-force bulletin lists but isn't loaded
    /// (S-124 §9.2.2): "Listed in NAVAREA X 110/26 but not received: 103/26,
    /// 107/26"; <see langword="null"/> when nothing is missing.
    /// </summary>
    public string? NotReceivedText { get; init; }

    /// <summary>Whether <see cref="NotReceivedText"/> has a value.</summary>
    public bool HasNotReceived => NotReceivedText is not null;

    /// <summary>The group's Library source, or <see langword="null"/> for a file opened directly.</summary>
    public Guid? SourceId { get; init; }

    /// <summary>Whether this is the dimmed "Not in force" group, which opens and closes.</summary>
    public bool IsNotInForce { get; }

    /// <summary>Whether this is a series group.</summary>
    public bool IsSeries => !IsNotInForce;

    /// <summary>Whether the group shows its rows.</summary>
    public bool IsExpanded { get; }

    /// <summary>Opens or closes the group (the "Not in force" group only).</summary>
    public ICommand? ToggleCommand { get; }

    /// <inheritdoc />
    public override double ContainerMinHeight => 30;

    /// <inheritdoc />
    public override string AccessibleName => string.IsNullOrEmpty(Source)
        ? $"{Name}, {Count}"
        : $"{Name}, {Count}, {Source}";
}

/// <summary>
/// A source that didn't answer its latest refresh, at the top of its first
/// group: "AMSA hasn't answered since 07:40. You're seeing the warnings
/// received then." with Retry.
/// </summary>
internal sealed class NavigationalWarningStaleItem : NavigationalWarningListItem
{
    public NavigationalWarningStaleItem(NavigationalWarningSource source, string text, ICommand retryCommand)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        Text = text;
        RetryCommand = retryCommand;
    }

    /// <summary>The unreachable source.</summary>
    public NavigationalWarningSource Source { get; }

    /// <summary>The notice.</summary>
    public string Text { get; }

    /// <summary>Reads the source again.</summary>
    public ICommand RetryCommand { get; }

    /// <inheritdoc />
    public override double ContainerMinHeight => 40;

    /// <inheritdoc />
    public override string AccessibleName => Text;
}

/// <summary>A warning part's row: number, type, and "area · validity" or why it isn't in force.</summary>
internal sealed class NavigationalWarningRowItem : NavigationalWarningListItem
{
    public NavigationalWarningRowItem(NavigationalWarningEntry entry, NavigationalWarningState state, DateTimeOffset time, TimeFormat format)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(state);
        Entry = entry;
        State = state;
        IsInForce = state.Status == NavigationalWarningStatus.InForce;
        _subText = BuildSubText(entry, state, time, format);
    }

    private readonly string _subText;

    /// <summary>
    /// Whether the map draws the part though it isn't in force at the panel's
    /// time: it is not yet in force with "Show warnings not yet in force" on,
    /// or its card is open (#993). <see cref="SubText"/> then says "Drawn".
    /// </summary>
    public bool IsDrawn { get; init; }

    /// <summary>The part's status at the panel's time, with why (#992).</summary>
    public NavigationalWarningState State { get; }

    /// <summary>The listed part.</summary>
    public NavigationalWarningEntry Entry { get; }

    /// <summary>Whether the part is in force at the panel's time.</summary>
    public bool IsInForce { get; }

    /// <summary>Whether the part's warning is new since its source's latest refresh.</summary>
    public bool IsNew { get; init; }

    /// <summary>While Check route is on, the part's relation to the route: "On route · 06:08", "Near route · 06:19"; <c>null</c> when off it (#959, E3).</summary>
    public string? RouteText { get; init; }

    /// <summary>Whether <see cref="RouteText"/> shows.</summary>
    public bool HasRouteText => RouteText is not null;

    /// <summary>The mariner's classification of the part's warning (#996).</summary>
    public NavigationalWarningClassification Classification { get; init; }

    /// <summary>Sets <see cref="Classification"/>; the parameter is the new <see cref="NavigationalWarningClassification"/>.</summary>
    public ICommand? ClassifyCommand { get; init; }

    /// <summary>Whether the warning is Off chart or Information, so it isn't drawn (the glyph is emphasised).</summary>
    public bool IsClassifiedOff => Classification != NavigationalWarningClassification.OnChart;

    /// <summary>Whether the classification is On chart (the menu's check).</summary>
    public bool IsOnChart => Classification == NavigationalWarningClassification.OnChart;

    /// <summary>Whether the classification is Off chart (the menu's check).</summary>
    public bool IsOffChart => Classification == NavigationalWarningClassification.OffChart;

    /// <summary>Whether the classification is Information (the menu's check).</summary>
    public bool IsInformation => Classification == NavigationalWarningClassification.Information;

    /// <summary>The classification glyph: a map On chart, a crossed eye Off chart, an "i" for Information.</summary>
    public FluentIcons.Common.Icon ClassificationGlyph => GlyphOf(Classification);

    /// <summary>The classification glyph's tooltip: what it means, and that it changes it.</summary>
    public string ClassificationTip => string.Format(CultureInfo.CurrentCulture, Strings.Tooltip_NavWarnings_ClassificationFormat, TipOf(Classification));

    /// <summary>The classification glyph's accessible name, e.g. "Classification: Off chart".</summary>
    public string ClassificationName => string.Format(CultureInfo.CurrentCulture, Strings.Label_NavWarnings_ClassificationFormat, NameOf(Classification));

    /// <summary>"On chart", "Off chart" or "Information".</summary>
    internal static string NameOf(NavigationalWarningClassification classification) => classification switch
    {
        NavigationalWarningClassification.OffChart => Strings.NavWarnings_ClassOffChart,
        NavigationalWarningClassification.Information => Strings.NavWarnings_ClassInformation,
        _ => Strings.NavWarnings_ClassOnChart,
    };

    /// <summary>What a classification means for drawing.</summary>
    internal static string TipOf(NavigationalWarningClassification classification) => classification switch
    {
        NavigationalWarningClassification.OffChart => Strings.Tooltip_NavWarnings_ClassOffChart,
        NavigationalWarningClassification.Information => Strings.Tooltip_NavWarnings_ClassInformation,
        _ => Strings.Tooltip_NavWarnings_ClassOnChart,
    };

    /// <summary>The glyph of a classification.</summary>
    internal static FluentIcons.Common.Icon GlyphOf(NavigationalWarningClassification classification) => classification switch
    {
        NavigationalWarningClassification.OffChart => FluentIcons.Common.Icon.EyeOff,
        NavigationalWarningClassification.Information => FluentIcons.Common.Icon.Info,
        _ => FluentIcons.Common.Icon.Map,
    };

    /// <summary>The number (e.g. "106/26"); the dataset's name when the preamble gives none.</summary>
    public string Number => Entry.Warning.Number ?? Entry.DatasetId;

    /// <summary>The detailed type, else the general type, else the title.</summary>
    public string TypeText => Entry.Warning.WarningType ?? Entry.Warning.GeneralType ?? Entry.Warning.Title ?? string.Empty;

    /// <summary>"Coral Sea · 13 Oct – 24 Oct", "No location · …", or "Ended 12 Oct · NAVAREA X".</summary>
    public string SubText => IsDrawn && !IsInForce ? $"{_subText} · {Strings.NavWarnings_Drawn}" : _subText;

    /// <summary>Whether the part has no location (the document glyph).</summary>
    public bool IsTextOnly => Entry.IsTextOnly;

    /// <summary>The row's opacity: dimmed when not in force.</summary>
    public double RowOpacity => IsInForce ? 1.0 : 0.62;

    /// <summary>The geometry glyph: a document for a text-only part.</summary>
    public FluentIcons.Common.Icon Glyph => Entry.Geometry switch
    {
        S100GeometryType.Point => FluentIcons.Common.Icon.Circle,
        S100GeometryType.Curve => FluentIcons.Common.Icon.Line,
        S100GeometryType.Surface => FluentIcons.Common.Icon.SquareDovetailJoint,
        _ => FluentIcons.Common.Icon.Document,
    };

    /// <summary>The glyph's tooltip.</summary>
    public string GlyphTip => Entry.Geometry switch
    {
        S100GeometryType.Point => Strings.NavWarnings_GeometryPoint,
        S100GeometryType.Curve => Strings.NavWarnings_GeometryLine,
        S100GeometryType.Surface => Strings.NavWarnings_GeometryArea,
        _ => Strings.NavWarnings_NoLocation,
    };

    /// <inheritdoc />
    public override double ContainerMinHeight => 40;

    /// <inheritdoc />
    public override string AccessibleName
    {
        get
        {
            var name = string.IsNullOrEmpty(TypeText) ? (Entry.Warning.Label ?? Number) : $"{Entry.Warning.Label ?? Number}, {TypeText}";
            if (IsClassifiedOff)
                name = $"{name}, {NameOf(Classification)}";
            return IsNew ? $"{name}, {Strings.NavWarnings_New}" : name;
        }
    }

    private static string BuildSubText(NavigationalWarningEntry entry, NavigationalWarningState state, DateTimeOffset time, TimeFormat format)
    {
        var w = entry.Warning;
        if (NavigationalWarningStatusText.Reason(state, brief: true) is { } reason)
            return reason;
        if (state.Status != NavigationalWarningStatus.InForce)
        {
            var status = state.Status;
            var when = status == NavigationalWarningStatus.NotYetInForce
                ? NextStart(w, time) is { } start
                    ? string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_StartsFormat, ShortDate(start, format))
                    : Strings.Pick_Warning_StatusNotYet
                : LastEnd(w, time) is { } end
                    ? string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_EndedFormat, ShortDate(end, format))
                    : Strings.Pick_Warning_StatusEnded;
            return w.Series is { } series ? $"{when} · {series}" : when;
        }

        var segments = new List<string>(3);
        if (entry.IsTextOnly)
            segments.Add(Strings.NavWarnings_NoLocation);
        if ((w.GeneralArea ?? w.Locality) is { } area)
            segments.Add(area);
        segments.Add(ValidityShort(w, format));
        return string.Join(" · ", segments);
    }

    /// <summary>"13 Oct – 24 Oct", "from 13 Oct", or "until cancelled".</summary>
    internal static string ValidityShort(NavigationalWarningInfo w, TimeFormat format)
    {
        DateTimeOffset? start = w.Validity.Count > 0 && w.Validity.All(r => r.Start is not null)
            ? w.Validity.Min(r => r.Start!.Value)
            : null;
        DateTimeOffset? end = w.Validity.Count > 0 && w.Validity.All(r => r.End is not null)
            ? w.Validity.Max(r => r.End!.Value)
            : w.CancellationDate;
        return (start, end) switch
        {
            ({ } s, { } e) => $"{ShortDate(s, format)} – {ShortDate(e, format)}",
            ({ } s, null) => string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_ValidFromFormat, ShortDate(s, format)),
            (null, { } e) => string.Format(CultureInfo.CurrentCulture, Strings.Pick_Warning_ValidUntil, ShortDate(e, format)),
            _ => Strings.NavWarnings_UntilCancelled,
        };
    }

    private static DateTimeOffset? NextStart(NavigationalWarningInfo w, DateTimeOffset time) =>
        w.Validity.Where(r => r.Start is { } s && s > time).Select(r => r.Start).Min();

    private static DateTimeOffset? LastEnd(NavigationalWarningInfo w, DateTimeOffset time)
    {
        if (w.CancellationDate is { } cancelled && cancelled <= time)
            return cancelled;
        return w.Validity.Where(r => r.End is { } e && e < time).Select(r => r.End).Max();
    }

    /// <summary>"13 Oct" in the Local/UTC preference.</summary>
    internal static string ShortDate(DateTimeOffset value, TimeFormat format) =>
        (format == TimeFormat.Utc ? value.ToUniversalTime() : value.ToLocalTime())
            .ToString("d MMM", CultureInfo.CurrentCulture);
}
