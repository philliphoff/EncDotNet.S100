using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The list the Library's map taps act on (#1396): its rows, its selection,
/// and listing the datasets at a map point.
/// </summary>
internal interface ILibraryTapList
{
    /// <summary>The rows listed now.</summary>
    IReadOnlyList<LibraryItemViewModel> Items { get; }

    /// <summary>How many datasets are listed, including those under a collapsed group.</summary>
    int ListedCount { get; }

    /// <summary>The selected row.</summary>
    LibraryItemViewModel? SelectedItem { get; set; }

    /// <summary>Lists the datasets at <paramref name="position"/>, or the tree's selected node's when <see langword="null"/>, keeping the selection.</summary>
    void ListAt(GeoPosition? position);
}

/// <summary>
/// The Library panel's map taps (#1396): the tap banner ("2 here · 1 of 2",
/// Next, List these) while a tap on outlined datasets is active, and the
/// location chip and banner while the list shows the datasets at a point.
/// The panel says when its list or selection changes.
/// </summary>
internal sealed class LibraryTapViewModel : ViewModelBase
{
    private readonly ILibraryTapList _list;
    private GeoPosition? _location;
    private GeoPosition? _tapPosition;
    private IReadOnlyList<LibraryItemViewModel>? _tapHits;
    private bool _selectingTapHit;

    /// <param name="list">The list the taps select in and filter.</param>
    public LibraryTapViewModel(ILibraryTapList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        _list = list;
        ClearLocationCommand = new RelayCommand(() => SetLocation(null));
        NextAtLocationCommand = new RelayCommand(NextAtLocation, () => _location is not null && _list.Items.Count > 1);
        NextTapHitCommand = new RelayCommand(NextTapHit, () => TapHitCount > 1);
        ListTapHitsCommand = new RelayCommand(ListTapHits, () => _tapPosition is not null);
        ClearTapCommand = new RelayCommand(ClearTap);
    }

    /// <summary>
    /// The map location the list is filtered to (set by the tap banner's "List these"), or
    /// <see langword="null"/> to list the selected node's datasets.
    /// </summary>
    public GeoPosition? Location => _location;

    /// <summary>True when the list shows the datasets at a map location.</summary>
    public bool HasLocation => _location is not null;

    /// <summary>"Datasets at 57°N 152°W" for the location chip.</summary>
    public string LocationSummary => _location is { } p
        ? string.Format(CultureInfo.CurrentCulture, Strings.Library_AtLocationFormat, LatLonFormatter.Format(p.Latitude, p.Longitude))
        : string.Empty;

    /// <summary>Returns the list to the selected node's datasets.</summary>
    public ICommand ClearLocationCommand { get; }

    /// <summary>Selects the next dataset covering the tapped point (as tapping the same spot again does).</summary>
    public ICommand NextAtLocationCommand { get; }

    /// <summary>How many listed datasets cover the tapped point.</summary>
    public int LocationHitCount => _location is null ? 0 : _list.ListedCount;

    /// <summary>The selected dataset's 1-based position among them, or 0 when none is selected.</summary>
    public int LocationHitIndex => _location is null || _list.SelectedItem is not { } selected ? 0 : IndexIn(_list.Items, selected) + 1;

    /// <summary>"3 datasets" for the map-tap banner.</summary>
    public string LocationHitsText => LocationHitCount == 1
        ? Strings.Library_LocationHitsOne
        : string.Format(CultureInfo.CurrentCulture, Strings.Library_LocationHitsFormat, LocationHitCount);

    /// <summary>"cover this point" (or "covers" for one).</summary>
    public string LocationCoverText => LocationHitCount == 1 ? Strings.Library_LocationCovers : Strings.Library_LocationCover;

    /// <summary>"1 / 3" for the map-tap banner.</summary>
    public string LocationPositionText =>
        string.Format(CultureInfo.CurrentCulture, Strings.Library_LocationIndexFormat, LocationHitIndex, LocationHitCount);

    /// <summary>
    /// True while a map tap is active and the list is not already the point
    /// list: the tap banner ("2 here · 1 of 2") shows.
    /// </summary>
    public bool HasTap => _tapHits is not null && _location is null;

    /// <summary>How many outlined datasets the active tap hit.</summary>
    public int TapHitCount => _tapHits?.Count ?? 0;

    /// <summary>"2 here" for the tap banner.</summary>
    public string TapHitsText => string.Format(CultureInfo.CurrentCulture, Strings.Library_TapHereFormat, TapHitCount);

    /// <summary>"· 1 of 2" for the tap banner.</summary>
    public string TapPositionText => string.Format(CultureInfo.CurrentCulture, Strings.Library_TapIndexFormat,
        _list.SelectedItem is { } selected ? TapHitIndexOf(selected) + 1 : 0, TapHitCount);

    /// <summary>Selects the next dataset the tap hit, wrapping round (as tapping the same spot again does).</summary>
    public ICommand NextTapHitCommand { get; }

    /// <summary>Lists the datasets at the tapped point, from every collection (what a tap used to do).</summary>
    public ICommand ListTapHitsCommand { get; }

    /// <summary>Ends the tap: hides the banner and clears the selection.</summary>
    public ICommand ClearTapCommand { get; }

    /// <summary>
    /// Handles a map tap that hit <paramref name="hits"/> (listed, outlined
    /// datasets, most detailed first): selects the first, or — when the same
    /// spot was tapped again — the one after the current selection. The list
    /// itself does not change; the tap banner offers Next and "List these".
    /// </summary>
    public void SelectTapHits(GeoPosition position, IReadOnlyList<LibraryItemViewModel> hits, bool sameSpot)
    {
        ArgumentNullException.ThrowIfNull(hits);
        if (hits.Count == 0)
        {
            ClearTap();
            return;
        }

        var current = _list.SelectedItem is { } selected ? IndexIn(hits, selected) : -1;
        _tapPosition = position;
        _tapHits = hits;
        SelectTapHit(sameSpot && current >= 0 ? (current + 1) % hits.Count : 0);
        OnTapChanged();
    }

    /// <summary>
    /// Lists what covers a tapped point inside forecast model domains (#685,
    /// handoff E1): the list is filtered to the point, so each model there shows
    /// with its tiles, and the smallest domain's model (listed first in
    /// <paramref name="smallestFirst"/>) is selected.
    /// </summary>
    public void ListModelsAt(GeoPosition position, IReadOnlyList<(Guid SourceId, string Model)> smallestFirst)
    {
        ArgumentNullException.ThrowIfNull(smallestFirst);
        EndTap();
        SetLocation(position);
        _list.SelectedItem = smallestFirst
            .Select(m => _list.Items.FirstOrDefault(i => i.Source.Id == m.SourceId && i.IsForecast
                && (i.IsModelHeader || i.IsForecastRunRow) && ForecastRuns.ModelOf(i.Item) == m.Model))
            .FirstOrDefault(i => i is not null) ?? _list.SelectedItem;
    }

    /// <summary>Ends any map tap and clears the selection (a tap where nothing is outlined, or the banner's ×).</summary>
    public void ClearTap()
    {
        EndTap();
        _list.SelectedItem = null;
    }

    /// <summary>Forgets the map tap (the banner hides); the selection is left alone.</summary>
    public void EndTap()
    {
        if (_tapHits is null)
            return;
        _tapHits = null;
        _tapPosition = null;
        OnTapChanged();
    }

    /// <summary>Lists the datasets at <paramref name="position"/>, or the selected node's again when <see langword="null"/>.</summary>
    public void SetLocation(GeoPosition? position)
    {
        _location = position;
        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(HasLocation));
        OnPropertyChanged(nameof(HasTap));
        OnPropertyChanged(nameof(LocationSummary));
        ((RelayCommand)NextAtLocationCommand).NotifyCanExecuteChanged();
        _list.ListAt(position);
    }

    /// <summary>The list's selection changed: a row picked by hand ends a tap it isn't one of the hits of.</summary>
    public void OnSelectionChanged()
    {
        var selected = _list.SelectedItem;
        if (!_selectingTapHit && _tapHits is not null && (selected is null || TapHitIndexOf(selected) < 0))
            EndTap();
        OnPropertyChanged(nameof(LocationHitIndex));
        OnPropertyChanged(nameof(LocationPositionText));
        OnPropertyChanged(nameof(TapPositionText));
    }

    /// <summary>The list was filtered again: the location banner's counts change.</summary>
    public void OnListChanged()
    {
        OnPropertyChanged(nameof(LocationHitCount));
        OnPropertyChanged(nameof(LocationHitsText));
        OnPropertyChanged(nameof(LocationCoverText));
        OnPropertyChanged(nameof(LocationPositionText));
        ((RelayCommand)NextAtLocationCommand).NotifyCanExecuteChanged();
    }

    private void NextTapHit()
    {
        if (_tapHits is not { Count: > 0 } hits)
            return;
        var current = _list.SelectedItem is { } selected ? IndexIn(hits, selected) : -1;
        SelectTapHit((current + 1) % hits.Count);
    }

    private void SelectTapHit(int index)
    {
        var hit = _tapHits![index];
        _selectingTapHit = true;
        try
        {
            // The list may have been rebuilt since the tap; select the row as listed now.
            _list.SelectedItem = _list.Items.FirstOrDefault(i => SameItem(i, hit)) ?? hit;
        }
        finally
        {
            _selectingTapHit = false;
        }
    }

    private void ListTapHits()
    {
        if (_tapPosition is not { } position)
            return;
        EndTap();
        SetLocation(position);
    }

    /// <summary>Selects the next dataset at the tapped point, wrapping round.</summary>
    private void NextAtLocation()
    {
        var items = _list.Items;
        if (_location is null || items.Count == 0)
            return;
        var next = _list.SelectedItem is { } selected ? (IndexIn(items, selected) + 1) % items.Count : 0;
        _list.SelectedItem = items[next];
    }

    private void OnTapChanged()
    {
        OnPropertyChanged(nameof(HasTap));
        OnPropertyChanged(nameof(TapHitCount));
        OnPropertyChanged(nameof(TapHitsText));
        OnPropertyChanged(nameof(TapPositionText));
        ((RelayCommand)NextTapHitCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ListTapHitsCommand).NotifyCanExecuteChanged();
    }

    private int TapHitIndexOf(LibraryItemViewModel item) => _tapHits is { } hits ? IndexIn(hits, item) : -1;

    private static int IndexIn(IReadOnlyList<LibraryItemViewModel> items, LibraryItemViewModel item)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (SameItem(items[i], item))
                return i;
        }

        return -1;
    }

    /// <summary>True when two rows list the same dataset (rows are rebuilt as the list changes).</summary>
    internal static bool SameItem(LibraryItemViewModel a, LibraryItemViewModel b) =>
        a.Source.Id == b.Source.Id && a.Item.Key == b.Item.Key;
}
