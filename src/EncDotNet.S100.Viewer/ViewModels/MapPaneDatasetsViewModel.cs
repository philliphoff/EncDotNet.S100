using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A map pane's header count and checklist (split map step 2 §2.6, D18): "2
/// of 4 datasets" for the pane's own map, and a checklist that sets which
/// datasets that map draws. Each exchange set is one tri-state row, then
/// come the loose datasets (Q5); the list ends with <b>Open Datasets</b>.
/// </summary>
/// <remarks>
/// The count follows every visibility change, made in any pane, from the
/// Datasets list or over MCP. The rows are built when the checklist opens
/// and kept up to date while it's open.
/// </remarks>
internal sealed class MapPaneDatasetsViewModel : ViewModelBase, IDisposable
{
    private readonly MapPaneViewModel _pane;
    private readonly DatasetsViewModel _datasets;
    private readonly Func<DatasetEntry, bool> _drawnInEveryMap;
    private readonly Action<Action> _dispatch;
    private readonly HashSet<DatasetEntry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ExchangeSetHeader> _headers = new(ReferenceEqualityComparer.Instance);
    private int _shown;
    private int _total;
    private bool _isOpen;
    private bool _rowsStale = true;
    private bool _disposed;

    /// <summary>Starts following the datasets' visibility in <paramref name="pane"/>'s map.</summary>
    /// <param name="pane">The pane whose map the count and checklist are for.</param>
    /// <param name="datasets">The loaded datasets.</param>
    /// <param name="drawnInEveryMap">
    /// True for a dataset that is drawn in every pane or none, such as a
    /// received S-421 route (Q4): its row shows or hides it in every map.
    /// </param>
    /// <param name="openDatasets">Opens the left dock's Datasets tab.</param>
    /// <param name="dispatch">Runs an update on the UI thread; a visibility change can come from any thread.</param>
    public MapPaneDatasetsViewModel(
        MapPaneViewModel pane,
        DatasetsViewModel datasets,
        Func<DatasetEntry, bool> drawnInEveryMap,
        Action openDatasets,
        Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(drawnInEveryMap);
        ArgumentNullException.ThrowIfNull(openDatasets);
        ArgumentNullException.ThrowIfNull(dispatch);
        _pane = pane;
        _datasets = datasets;
        _drawnInEveryMap = drawnInEveryMap;
        _dispatch = dispatch;
        OpenDatasetsCommand = new RelayCommand(() =>
        {
            IsOpen = false;
            openDatasets();
        });

        _pane.PropertyChanged += OnPanePropertyChanged;
        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        _datasets.ExchangeSetHeaders.CollectionChanged += OnHeadersChanged;
        SyncEntries();
        SyncHeaders();
        UpdateCount();
    }

    /// <summary>The pane's map.</summary>
    public MapViewId Map => _pane.Id;

    /// <summary>How many datasets the pane's map shows.</summary>
    public int Shown
    {
        get => _shown;
        private set
        {
            if (SetProperty(ref _shown, value))
                OnPropertyChanged(nameof(CountText));
        }
    }

    /// <summary>How many datasets are loaded.</summary>
    public int Total
    {
        get => _total;
        private set
        {
            if (SetProperty(ref _total, value))
            {
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    /// <summary>The header's count, and its accessible name: "2 of 4 datasets".</summary>
    public string CountText => string.Format(CultureInfo.CurrentCulture, Strings.SplitMap_DatasetCountFormat, _shown, _total);

    /// <summary>The count's tooltip and help text: "Choose the datasets Map 2 shows".</summary>
    public string CountToolTip => string.Format(CultureInfo.CurrentCulture, Strings.SplitMap_DatasetChecklistToolTipFormat, _pane.Title);

    /// <summary>The checklist's heading: "Shown in Map 2".</summary>
    public string Heading => string.Format(CultureInfo.CurrentCulture, Strings.SplitMap_DatasetChecklistHeadingFormat, _pane.Title);

    /// <summary>True when no dataset is loaded: the checklist says so instead of listing rows.</summary>
    public bool IsEmpty => _total == 0;

    /// <summary>
    /// Whether the checklist is open. Opening it builds the rows; while it's
    /// open they follow loads, removals and visibility changes.
    /// </summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!SetProperty(ref _isOpen, value))
                return;
            if (value)
                RebuildRowsIfStale();
        }
    }

    /// <summary>The checklist's rows: one per exchange set, then the loose datasets (Q5).</summary>
    public ObservableCollection<MapPaneDatasetRow> Rows { get; } = [];

    /// <summary>Opens the left dock's Datasets tab and closes the checklist.</summary>
    public IRelayCommand OpenDatasetsCommand { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pane.PropertyChanged -= OnPanePropertyChanged;
        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
        _datasets.ExchangeSetHeaders.CollectionChanged -= OnHeadersChanged;
        foreach (var entry in _entries)
            entry.VisibilityChanged -= OnVisibilityChanged;
        foreach (var header in _headers)
            header.Datasets.CollectionChanged -= OnHeaderMembersChanged;
        _entries.Clear();
        _headers.Clear();
        Rows.Clear();
    }

    /// <summary>Shows or hides <paramref name="entry"/> in this pane's map, or in every map for one drawn in every pane.</summary>
    internal void SetShown(DatasetEntry entry, bool shown)
    {
        if (_drawnInEveryMap(entry))
            entry.SetVisible(shown);
        else
            entry.SetVisibleIn(Map, shown);
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapPaneViewModel.Title))
        {
            OnPropertyChanged(nameof(CountToolTip));
            OnPropertyChanged(nameof(Heading));
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncEntries();
        UpdateCount();
        MarkRowsStale();
    }

    private void OnHeadersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncHeaders();
        MarkRowsStale();
    }

    private void OnHeaderMembersChanged(object? sender, NotifyCollectionChangedEventArgs e) => MarkRowsStale();

    private void OnVisibilityChanged(object? sender, MapViewId? map)
    {
        // A change in another map doesn't move this pane's count; null is every map.
        if (map is { } changed && changed != Map)
            return;
        _dispatch(() =>
        {
            if (_disposed)
                return;
            UpdateCount();
            foreach (var row in Rows)
                row.Refresh();
        });
    }

    private void SyncEntries()
    {
        var current = _datasets.Entries.ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var gone in _entries.Where(e => !current.Contains(e)).ToArray())
        {
            gone.VisibilityChanged -= OnVisibilityChanged;
            _entries.Remove(gone);
        }

        foreach (var entry in _datasets.Entries)
        {
            if (_entries.Add(entry))
                entry.VisibilityChanged += OnVisibilityChanged;
        }
    }

    private void SyncHeaders()
    {
        var current = _datasets.ExchangeSetHeaders.ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var gone in _headers.Where(h => !current.Contains(h)).ToArray())
        {
            gone.Datasets.CollectionChanged -= OnHeaderMembersChanged;
            _headers.Remove(gone);
        }

        foreach (var header in _datasets.ExchangeSetHeaders)
        {
            if (_headers.Add(header))
                header.Datasets.CollectionChanged += OnHeaderMembersChanged;
        }
    }

    private void UpdateCount()
    {
        var map = Map;
        var shown = 0;
        foreach (var entry in _datasets.Entries)
        {
            if (entry.IsVisibleIn(map))
                shown++;
        }

        Total = _datasets.Entries.Count;
        Shown = shown;
    }

    private void MarkRowsStale()
    {
        _rowsStale = true;
        if (_isOpen)
            RebuildRowsIfStale();
    }

    private void RebuildRowsIfStale()
    {
        if (!_rowsStale || _disposed)
            return;
        _rowsStale = false;

        Rows.Clear();
        var grouped = new HashSet<DatasetEntry>(ReferenceEqualityComparer.Instance);
        foreach (var header in _datasets.ExchangeSetHeaders)
        {
            if (header.Datasets.Count == 0)
                continue;
            grouped.UnionWith(header.Datasets);
            Rows.Add(new MapPaneDatasetRow(this, header));
        }

        foreach (var entry in _datasets.Entries)
        {
            if (!grouped.Contains(entry))
                Rows.Add(new MapPaneDatasetRow(this, entry));
        }
    }
}

/// <summary>
/// A row of a pane's dataset checklist (D18, Q5): a loose dataset, or an
/// exchange set whose box is checked when the pane shows all its datasets,
/// cleared when it shows none, and indeterminate in between.
/// </summary>
internal sealed class MapPaneDatasetRow : ViewModelBase
{
    private readonly MapPaneDatasetsViewModel _owner;
    private readonly DatasetEntry? _entry;
    private readonly ExchangeSetHeader? _header;

    /// <summary>A row for one loose dataset.</summary>
    /// <param name="owner">The checklist.</param>
    /// <param name="entry">The dataset.</param>
    public MapPaneDatasetRow(MapPaneDatasetsViewModel owner, DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(entry);
        _owner = owner;
        _entry = entry;
    }

    /// <summary>A tri-state row for every dataset of an exchange set.</summary>
    /// <param name="owner">The checklist.</param>
    /// <param name="header">The exchange set.</param>
    public MapPaneDatasetRow(MapPaneDatasetsViewModel owner, ExchangeSetHeader header)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(header);
        _owner = owner;
        _header = header;
    }

    /// <summary>True for an exchange set's row.</summary>
    public bool IsExchangeSet => _header is not null;

    /// <summary>The dataset, or <see langword="null"/> for an exchange set's row.</summary>
    public DatasetEntry? Entry => _entry;

    /// <summary>The exchange set, or <see langword="null"/> for a dataset's row.</summary>
    public ExchangeSetHeader? Header => _header;

    /// <summary>The row's name, and the check box's accessible name: the dataset's or exchange set's name.</summary>
    public string Label => _header?.DisplayName ?? _entry?.DisplayName ?? string.Empty;

    /// <summary>The second line: the product ("S-101"), or for an exchange set how many of its datasets show ("3 of 12").</summary>
    public string Detail => _header is { } header
        ? string.Format(CultureInfo.CurrentCulture, Strings.SplitMap_DatasetChecklistSetCountFormat, ShownMembers(header), header.Datasets.Count)
        : _entry?.ProductSpec ?? string.Empty;

    /// <summary>
    /// Whether the pane's map shows the row: for an exchange set, <see langword="null"/>
    /// when it shows some of its datasets. Setting it toggles the row, whatever the
    /// value: a dataset's row flips; an exchange set's row hides every dataset when
    /// all show, else shows every one.
    /// </summary>
    /// <remarks>
    /// The check box sets its own next value (indeterminate goes to cleared),
    /// so the row works the value out from its state and raises the change, and
    /// the box follows.
    /// </remarks>
    public bool? IsChecked
    {
        get => State();
        set
        {
            Toggle();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Detail));
        }
    }

    /// <summary>Flips the row, as clicking its box does.</summary>
    public void Toggle()
    {
        if (_header is { } header)
        {
            var show = State() != true;
            foreach (var member in header.Datasets.ToArray())
                _owner.SetShown(member, show);
        }
        else if (_entry is { } entry)
        {
            _owner.SetShown(entry, !entry.IsVisibleIn(_owner.Map));
        }
    }

    /// <summary>Raises the state's change after a visibility change.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(Detail));
    }

    private bool? State()
    {
        if (_entry is { } entry)
            return entry.IsVisibleIn(_owner.Map);
        if (_header is not { } header || header.Datasets.Count == 0)
            return false;
        var shown = ShownMembers(header);
        return shown == 0 ? false : shown == header.Datasets.Count ? true : null;
    }

    private int ShownMembers(ExchangeSetHeader header)
    {
        var map = _owner.Map;
        var shown = 0;
        foreach (var member in header.Datasets)
        {
            if (member.IsVisibleIn(map))
                shown++;
        }

        return shown;
    }
}
