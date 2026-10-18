using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using Mapsui;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Which time the Navigational warnings panel reads status at.</summary>
internal enum NavigationalWarningsTimeFilter
{
    /// <summary>The Timeline's view time (now while it is Live); the default, matching the map.</summary>
    ViewTime,

    /// <summary>The wall clock.</summary>
    Now,
}

/// <summary>How the Navigational warnings panel orders rows within a group.</summary>
internal enum NavigationalWarningsSort
{
    /// <summary>Newest number first (year, then number, descending).</summary>
    Number,

    /// <summary>Most recently published first.</summary>
    Published,
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
/// </remarks>
internal sealed class NavigationalWarningsViewModel : ViewModelBase, INavigationalWarningNavigator, IDisposable
{
    /// <summary>Half the side of the box a point warning frames, in EPSG:3857 metres (about 1 NM).</summary>
    internal const double PointFrameHalfMetres = 1852.0;

    private readonly NavigationalWarningsService _service;
    private readonly IPickService? _pick;
    private readonly PickReportViewModel? _pickReport;
    private readonly IViewTime? _viewTime;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly ICapabilityAccessor<IMapViewportController>? _viewport;
    private readonly IDatasetLoaderService? _loader;
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
    private bool _disposed;

    public NavigationalWarningsViewModel(
        NavigationalWarningsService service,
        IPickService? pick = null,
        PickReportViewModel? pickReport = null,
        IViewTime? viewTime = null,
        ITimeFormatProvider? timeFormat = null,
        ICapabilityAccessor<IMapViewportController>? viewport = null,
        IDatasetLoaderService? loader = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _pick = pick;
        _pickReport = pickReport;
        _viewTime = viewTime;
        _timeFormat = timeFormat;
        _viewport = viewport;
        _loader = loader;
        _clock = clock ?? TimeProvider.System;

        ToggleTextOnlyCommand = new RelayCommand(() => IsTextOnlyFilter = !IsTextOnlyFilter);
        ToggleNotInForceCommand = new RelayCommand(() => IsNotInForceExpanded = !IsNotInForceExpanded);
        ShowNotInForceCommand = new RelayCommand(() => IsNotInForceExpanded = true);
        ClearQueryCommand = new RelayCommand(() => Query = string.Empty);
        OpenLibraryCommand = new RelayCommand(() => OpenLibraryRequested?.Invoke(this, EventArgs.Empty));

        _service.Changed += OnServiceChanged;
        if (_viewTime is not null)
            _viewTime.Changed += OnViewTimeChanged;
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged += OnTimeFormatChanged;
        if (_pickReport is not null)
        {
            _pickReport.PropertyChanged += OnPickReportChanged;
            _pickReport.WarningNavigator = this;
        }
        Rebuild();
    }

    /// <summary>Object Information, where opened warnings show.</summary>
    internal PickReportViewModel? PickReport => _pickReport;

    /// <inheritdoc />
    public IReadOnlyList<NavigationalWarningEntry> Warnings => _service.Warnings;

    /// <inheritdoc />
    public event EventHandler? WarningsChanged;

    /// <inheritdoc />
    public NavigationalWarningEntry? Find(string datasetId, string featureRef) => _service.Find(datasetId, featureRef);

    /// <inheritdoc />
    public bool ZoomTo(NavigationalWarningEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (FrameExtent(entry) is not { } extent)
            return false;
        _viewport?.Current?.ZoomToExtent(extent);
        return true;
    }

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

    /// <summary>The sort as a combo-box index (0 Number, 1 Published).</summary>
    public int SortIndex
    {
        get => (int)_sort;
        set => Sort = value == 1 ? NavigationalWarningsSort.Published : NavigationalWarningsSort.Number;
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
        _service.Warnings.Count > 0 && _inForceCount == 0 && string.IsNullOrWhiteSpace(_query) && !_textOnlyFilter;

    /// <summary>"No warnings in force at view time." or "… now."</summary>
    public string NoneInForceText => IsNowFilter ? Strings.NavWarnings_NoneInForceNow : Strings.NavWarnings_NoneInForceViewTime;

    /// <summary>"Show N not in force".</summary>
    public string ShowNotInForceText =>
        string.Format(CultureInfo.CurrentCulture, Strings.NavWarnings_ShowNotInForceFormat, _notInForceCount);

    /// <summary>Whether the search found nothing.</summary>
    public bool IsNoMatchesState => _service.Warnings.Count > 0 && Items.Count == 0 && !IsNoneInForceState;

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

            var opened = OpenInObjectInformation(entry);
            if (!opened)
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
            if (FrameExtent(entry) is { } extent)
                _viewport?.Current?.ZoomToExtent(extent);
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
            if (entry.Warning.StatusAt(StatusTime) != NavigationalWarningStatus.InForce)
                _notInForceExpanded = true;
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

    /// <summary>
    /// The EPSG:3857 extent selecting a part frames: its bounding box, never
    /// smaller than about 2 NM across, so a point opens at a usable scale.
    /// </summary>
    internal static MRect? FrameExtent(NavigationalWarningEntry entry)
    {
        if (entry.Extent is not { } box)
            return null;
        var halfW = Math.Max(box.Width / 2, PointFrameHalfMetres);
        var halfH = Math.Max(box.Height / 2, PointFrameHalfMetres);
        return new MRect(box.Centroid.X - halfW, box.Centroid.Y - halfH, box.Centroid.X + halfW, box.Centroid.Y + halfH);
    }

    private bool OpenInObjectInformation(NavigationalWarningEntry entry)
    {
        if (_pick is null || _loader is null)
            return false;
        using var processors = _loader.AcquireProcessors();
        return processors.TryGetValue(entry.Dataset, out var processor)
            && _pick.OpenFeatureAt(processor, entry.Ordinal, entry.DatasetId);
    }

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

    private void OnPickReportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_opening || e.PropertyName is not (nameof(PickReportViewModel.SelectedHit) or nameof(PickReportViewModel.HasPick)))
            return;

        // A pick elsewhere replaces the open warning: its note goes, and a
        // warning picked on the map is selected in the list.
        var hit = _pickReport!.SelectedHit;
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
    }

    private void Rebuild()
    {
        if (_disposed)
            return;

        var time = StatusTime;
        var format = _timeFormat?.Current ?? TimeFormat.Local;
        var all = _service.Warnings;
        var inForce = new List<NavigationalWarningEntry>();
        var notInForce = new List<NavigationalWarningEntry>();
        foreach (var w in all)
            (w.Warning.StatusAt(time) == NavigationalWarningStatus.InForce ? inForce : notInForce).Add(w);

        _inForceCount = inForce.Count;
        _notInForceCount = notInForce.Count;
        _textOnlyCount = inForce.Count(w => w.IsTextOnly);

        bool Listed(NavigationalWarningEntry w) => (!_textOnlyFilter || w.IsTextOnly) && Matches(w, _query);

        var items = new List<NavigationalWarningListItem>();
        var groups = inForce.Where(Listed)
            .GroupBy(w => (Series: w.Warning.Series ?? Strings.NavWarnings_NoSeries, Source: w.Warning.Issuer ?? string.Empty))
            .OrderBy(g => g.Key.Series, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Key.Source, StringComparer.CurrentCultureIgnoreCase);
        foreach (var group in groups)
        {
            var rows = Ordered(group).ToList();
            items.Add(new NavigationalWarningGroupItem(group.Key.Series, rows.Count, group.Key.Source, isNotInForce: false, isExpanded: true, command: null));
            items.AddRange(rows.Select(w => new NavigationalWarningRowItem(w, time, format, isInForce: true)));
        }

        var outRows = Ordered(notInForce.Where(Listed)).ToList();
        if (outRows.Count > 0)
        {
            items.Add(new NavigationalWarningGroupItem(
                IsNowFilter ? Strings.NavWarnings_NotInForceGroupNow : Strings.NavWarnings_NotInForceGroupViewTime,
                outRows.Count,
                string.Empty,
                isNotInForce: true,
                isExpanded: _notInForceExpanded,
                command: ToggleNotInForceCommand));
            if (_notInForceExpanded)
                items.AddRange(outRows.Select(w => new NavigationalWarningRowItem(w, time, format, isInForce: false)));
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
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(IsLoadingState));
        OnPropertyChanged(nameof(IsNoSourcesState));
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(IsNoneInForceState));
        OnPropertyChanged(nameof(NoneInForceText));
        OnPropertyChanged(nameof(ShowNotInForceText));
        OnPropertyChanged(nameof(IsNoMatchesState));
        OnPropertyChanged(nameof(HasList));
    }

    private IEnumerable<NavigationalWarningEntry> Ordered(IEnumerable<NavigationalWarningEntry> warnings) =>
        _sort == NavigationalWarningsSort.Published
            ? warnings.OrderByDescending(w => w.Warning.PublicationTime ?? DateTimeOffset.MinValue)
                .ThenByDescending(w => NumberKey(w.Warning.Number))
            : warnings.OrderByDescending(w => NumberKey(w.Warning.Number))
                .ThenByDescending(w => w.Warning.PublicationTime ?? DateTimeOffset.MinValue);

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
        if (_pickReport is not null)
        {
            _pickReport.PropertyChanged -= OnPickReportChanged;
            if (ReferenceEquals(_pickReport.WarningNavigator, this))
                _pickReport.WarningNavigator = null;
        }
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

    /// <summary>The issuing authority, or empty.</summary>
    public string Source { get; }

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

/// <summary>A warning part's row: number, type, and "area · validity" or why it isn't in force.</summary>
internal sealed class NavigationalWarningRowItem : NavigationalWarningListItem
{
    public NavigationalWarningRowItem(NavigationalWarningEntry entry, DateTimeOffset time, TimeFormat format, bool isInForce)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        IsInForce = isInForce;
        SubText = BuildSubText(entry, time, format, isInForce);
    }

    /// <summary>The listed part.</summary>
    public NavigationalWarningEntry Entry { get; }

    /// <summary>Whether the part is in force at the panel's time.</summary>
    public bool IsInForce { get; }

    /// <summary>The number (e.g. "106/26"); the dataset's name when the preamble gives none.</summary>
    public string Number => Entry.Warning.Number ?? Entry.DatasetId;

    /// <summary>The detailed type, else the general type, else the title.</summary>
    public string TypeText => Entry.Warning.WarningType ?? Entry.Warning.GeneralType ?? Entry.Warning.Title ?? string.Empty;

    /// <summary>"Coral Sea · 13 Oct – 24 Oct", "No location · …", or "Ended 12 Oct · NAVAREA X".</summary>
    public string SubText { get; }

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
    public override string AccessibleName =>
        string.IsNullOrEmpty(TypeText) ? (Entry.Warning.Label ?? Number) : $"{Entry.Warning.Label ?? Number}, {TypeText}";

    private static string BuildSubText(NavigationalWarningEntry entry, DateTimeOffset time, TimeFormat format, bool isInForce)
    {
        var w = entry.Warning;
        if (!isInForce)
        {
            var status = w.StatusAt(time);
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
