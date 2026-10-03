using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Which datasets the Library list shows, by where their data is and what is new.</summary>
internal enum LibraryStateFilter
{
    /// <summary>Every dataset.</summary>
    All,

    /// <summary>On disk and current: local, loaded or loading on pan.</summary>
    Local,

    /// <summary>Downloadable, not yet downloaded.</summary>
    Online,

    /// <summary>Downloaded, with a newer edition or update available.</summary>
    Updates,
}

/// <summary>
/// View model for the Library panel (issue #655), which replaces the former
/// S-128 Catalog panel. Shows the user's dataset collections as a tree of
/// collections and sources, the indexed datasets of the selected node as a
/// filterable list, and the selected dataset's metadata — without loading any
/// dataset.
/// </summary>
internal sealed class LibraryPanelViewModel : ViewModelBase, IDisposable
{
    private readonly LibraryService _library;
    private readonly ILibraryImporter _importer;
    private readonly ILibraryLoader _loader;
    private readonly ILibraryDownloader _downloader;
    private readonly Func<CollectionSource, EncDotNet.S100.Collections.Indexing.FeedHealth?>? _feedHealth;
    private bool _availabilityRefreshPosted;
    private bool _progressRefreshPosted;
    private readonly Dictionary<Guid, List<CollectionItem>> _downloadingBySource = [];
    private readonly HashSet<(Guid Source, string Package)> _expandedPackages = [];
    private readonly Dictionary<(Guid Source, string Package), LibraryItemViewModel> _packageHeaders = [];
    private readonly Services.Notifications.INotificationService? _notifications;
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private readonly ITimer? _clock;
    private readonly Services.GlobalTimeService? _viewTime;
    private bool _atViewTime;
    private IReadOnlyList<LibraryItemViewModel> _textMatched = [];

    private LibraryNodeViewModel? _selectedNode;
    private bool _hasSynced;
    private IReadOnlyList<SourceIndex?>? _itemsBasis;
    private IReadOnlyList<LibraryItemViewModel> _allItems = [];
    private IReadOnlyList<LibraryItemViewModel> _items = [];
    private LibraryItemViewModel? _selectedItem;
    private string _filterText = string.Empty;
    private bool _showCancelled;
    private LibraryStateFilter _stateFilter;
    private IReadOnlyList<LibraryItemViewModel> _textFiltered = [];
    private int _listedDatasets;
    private bool _refreshPosted;
    private bool _showCoverage = true;
    private GeoPosition? _location;
    private GeoPosition? _tapPosition;
    private IReadOnlyList<LibraryItemViewModel>? _tapHits;
    private bool _selectingTapHit;

    public LibraryPanelViewModel(
        LibraryService library,
        ILibraryImporter importer,
        ILibraryLoader loader,
        ILibraryDownloader downloader,
        Func<CollectionSource, EncDotNet.S100.Collections.Indexing.FeedHealth?>? feedHealth = null,
        Services.Notifications.INotificationService? notifications = null,
        TimeProvider? time = null,
        Services.GlobalTimeService? viewTime = null)
        : this(library, importer, loader, downloader, PostToUiThread, feedHealth, notifications, time, viewTime)
    {
    }

    internal LibraryPanelViewModel(
        LibraryService library,
        ILibraryImporter importer,
        ILibraryLoader loader,
        ILibraryDownloader downloader,
        Action<Action> dispatch,
        Func<CollectionSource, EncDotNet.S100.Collections.Indexing.FeedHealth?>? feedHealth = null,
        Services.Notifications.INotificationService? notifications = null,
        TimeProvider? time = null,
        Services.GlobalTimeService? viewTime = null)
    {
        _time = time ?? TimeProvider.System;
        _viewTime = viewTime;
        _feedHealth = feedHealth;
        _notifications = notifications;
        ArgumentNullException.ThrowIfNull(downloader);
        _downloader = downloader;
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(dispatch);
        _library = library;
        _importer = importer;
        _loader = loader;
        _dispatch = dispatch;

        AddFolderCommand = new AsyncRelayCommand(() => _importer.AddFolderAsync(TargetCollectionId));
        AddExchangeSetZipCommand = new AsyncRelayCommand(() => _importer.AddExchangeSetZipAsync(TargetCollectionId));
        AddOnlineCatalogueCommand = new AsyncRelayCommand(() => _importer.AddOnlineCatalogueAsync(TargetCollectionId));
        AddS128CatalogueCommand = new AsyncRelayCommand(() => _importer.AddS128CatalogueAsync(TargetCollectionId));
        AddSharedFeedCommand = new AsyncRelayCommand(() => _importer.AddSharedFeedAsync(TargetCollectionId));
        AddCollectionManifestCommand = new AsyncRelayCommand(() => _importer.AddCollectionManifestAsync(TargetCollectionId));
        ChooseGroupsCommand = new AsyncRelayCommand(ChooseGroupsAsync, () => _selectedNode?.CanChooseGroups == true);
        AddCurrentsForAreaCommand = new AsyncRelayCommand(AddCurrentsForAreaAsync, () => CanAddCurrentsForArea);
        RefreshCommand = new RelayCommand(Refresh);
        RefreshAllCommand = new RelayCommand(() => _library.Refresh());
        RenameCommand = new RelayCommand(BeginRename, () => _selectedNode?.CanRename == true);
        CommitRenameCommand = new RelayCommand(CommitRename);
        CancelRenameCommand = new RelayCommand(CancelRename);
        RemoveCommand = new RelayCommand(Remove, () => _selectedNode?.CanRemove == true);
        KeepInLibraryCommand = new RelayCommand(Keep, () => _selectedNode?.CanKeep == true);
        ZoomToCommand = new RelayCommand(ZoomToSelected, () => _selectedItem?.HasBounds == true);
        ClearLocationCommand = new RelayCommand(() => SetLocation(null));
        NextAtLocationCommand = new RelayCommand(NextAtLocation, () => _location is not null && _items.Count > 1);
        NextTapHitCommand = new RelayCommand(NextTapHit, () => TapHitCount > 1);
        ListTapHitsCommand = new RelayCommand(ListTapHits, () => _tapPosition is not null);
        ClearTapCommand = new RelayCommand(ClearTap);
        LoadCommand = new AsyncRelayCommand(LoadSelectedAsync, () => _selectedItem?.CanLoad == true);
        LoadAsYouPanCommand = new AsyncRelayCommand(LoadListedAsYouPanAsync, () => _items.Count > 0);
        DownloadCommand = new AsyncRelayCommand(DownloadSelectedAsync, () => _selectedItem?.CanDownload == true);
        DownloadOnlyCommand = new AsyncRelayCommand(() => DownloadSelectedAsync(load: false), () => _selectedItem?.CanDownload == true);
        LoadOrDownloadCommand = new AsyncRelayCommand(
            () => _selectedItem?.CanLoadAfterDownload == true ? DownloadSelectedAsync(load: true) : LoadSelectedAsync(),
            () => _selectedItem is { } item && (item.CanLoad || item.CanLoadAfterDownload));
        DownloadListedCommand = new AsyncRelayCommand(DownloadListedAsync, () => DownloadableCount > 0);
        CancelDownloadsCommand = new RelayCommand(() => _downloader.CancelAll(), () => IsBulkDownloading);
        ShowOnTimelineCommand = new RelayCommand(
            () =>
            {
                if (_selectedItem?.ValidWindow is { } window)
                    ShowOnTimelineRequested?.Invoke(this, window);
            },
            () => HasViewTime && _selectedItem?.ValidWindow is not null);
        GoToRunStartCommand = new RelayCommand(
            () =>
            {
                if (_selectedItem?.ValidWindow is { } window)
                    GoToTimeRequested?.Invoke(this, window.Start);
            },
            () => HasViewTime && _selectedItem?.ValidWindow is not null);
        if (_viewTime is not null)
        {
            // The "At view time" facet follows the Timeline (#711, handoff G2).
            _viewTime.CurrentTimeChanged += _ => _dispatch(OnViewTimeChanged);
            _viewTime.RangeChanged += () => _dispatch(OnViewTimeChanged);
        }
        _loader.Changed += OnLoaderChanged;
        _downloader.Changed += OnLoaderChanged;
        _downloader.ProgressChanged += OnDownloadProgress;

        _library.Changed += OnLibraryChanged;

        // Forecast runs age by the minute: time left, and Expired (#685).
        _clock = _time.CreateTimer(_ => _dispatch(OnClockTick), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        Sync();
    }

    /// <summary>Collection nodes, each with its source nodes as children.</summary>
    public ObservableCollection<LibraryNodeViewModel> Nodes { get; } = [];

    /// <summary>True when the library has no collection.</summary>
    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>The selected collection or source.</summary>
    public LibraryNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                OnPropertyChanged(nameof(HasSelectedNode));
                ((RelayCommand)RemoveCommand).NotifyCanExecuteChanged();
                ((RelayCommand)KeepInLibraryCommand).NotifyCanExecuteChanged();
                ((RelayCommand)RenameCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)ChooseGroupsCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)AddCurrentsForAreaCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanAddCurrentsForArea));
                EndTap();
                if (_location is not null)
                    SetLocation(null);
                else
                    RebuildItems(force: true);
            }
        }
    }

    /// <summary>True when a node is selected.</summary>
    public bool HasSelectedNode => _selectedNode is not null;

    /// <summary>The datasets of <see cref="SelectedNode"/> passing the filters.</summary>
    public IReadOnlyList<LibraryItemViewModel> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    /// <summary>The selected dataset.</summary>
    public LibraryItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                // Picking a row by hand keeps the tap while the row is one of its hits.
                if (!_selectingTapHit && _tapHits is not null && (value is null || TapHitIndexOf(value) < 0))
                    EndTap();

                OnPropertyChanged(nameof(HasSelectedItem));
                OnPropertyChanged(nameof(LocationHitIndex));
                OnPropertyChanged(nameof(LocationPositionText));
                OnPropertyChanged(nameof(TapPositionText));
                ((RelayCommand)ZoomToCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)LoadCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DownloadCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DownloadOnlyCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)LoadOrDownloadCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ShowOnTimelineCommand).NotifyCanExecuteChanged();
                ((RelayCommand)GoToRunStartCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(HasValidWindow));
                UpdatePairing();
            }
        }
    }

    /// <summary>
    /// For a selected S-102 area node: opens the S-111 wizard at its models
    /// step with the models covering the area ticked (#685, handoff B8).
    /// </summary>
    public ICommand AddCurrentsForAreaCommand { get; }

    /// <summary>True when the selected node is an area of a remote S-100 catalogue (S-102).</summary>
    public bool CanAddCurrentsForArea => _selectedNode is { IsGroup: true, Source.Definition: S100CatalogueFeedSource };

    private Task AddCurrentsForAreaAsync()
    {
        if (!CanAddCurrentsForArea
            || GeoBounds.UnionAll(_selectedNode!.EnumerateItems().Select(p => p.Item.Bounds).OfType<GeoBounds>()) is not { } area)
        {
            return Task.CompletedTask;
        }

        return _importer.AddCurrentsForAreaAsync(area, targetCollectionId: null);
    }

    private LibraryPairing? _pairing;

    /// <summary>
    /// For a selected S-111 tile, its S-102 twin in the same grid cell, when an
    /// S-102 collection lists it (#685, handoff B7); otherwise <see langword="null"/>.
    /// </summary>
    public LibraryPairing? Pairing
    {
        get => _pairing;
        private set
        {
            if (SetProperty(ref _pairing, value))
                OnPropertyChanged(nameof(HasPairing));
        }
    }

    /// <summary>True when <see cref="Pairing"/> is shown.</summary>
    public bool HasPairing => _pairing is not null;

    private void UpdatePairing()
    {
        if (_selectedItem is not { IsForecast: true, IsModelHeader: false } tile
            || ForecastRuns.BathymetryTwinOf(tile.Item.Name) is not { } twin)
        {
            Pairing = null;
            return;
        }

        var match = _library.Collections
            .SelectMany(c => c.Sources)
            .Where(s => s.Definition is S100CatalogueFeedSource && s.Index is not null)
            .SelectMany(s => s.Index!.Items)
            .FirstOrDefault(i => string.Equals(i.Name, twin, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            Pairing = null;
            return;
        }

        var local = _downloader.Localize(match).Location is LocalItemLocation;
        var size = (match.Location as RemoteItemLocation)?.SizeBytes;
        var detail = string.Join(" · ", new[]
        {
            $"{match.ProductSpec} {match.Name}",
            Strings.Library_Pairing_SameCell,
            local ? Strings.Library_Availability_Local : Strings.Library_Availability_Online,
            size is { } bytes && !local ? LibraryItemViewModel.FormatBytes(bytes) : null,
        }.OfType<string>());
        Pairing = new LibraryPairing(Strings.Library_Pairing_Bathymetry, detail, local,
            new AsyncRelayCommand(async () =>
            {
                await _downloader.DownloadAsync([match]).ConfigureAwait(true);
                UpdatePairing();
            }, () => !local && _downloader.CanDownload(match)));
    }

    /// <summary>True when a dataset is selected.</summary>
    public bool HasSelectedItem => _selectedItem is not null;

    /// <summary>Free text matched against name, title, spec and properties.</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty))
            {
                EndTap();
                ApplyFilter();
            }
        }
    }

    /// <summary>Whether cancelled datasets are listed.</summary>
    public bool ShowCancelled
    {
        get => _showCancelled;
        set
        {
            if (SetProperty(ref _showCancelled, value))
            {
                EndTap();
                ApplyFilter();
            }
        }
    }

    /// <summary>Which datasets are listed by state (the segments under the filter box).</summary>
    public LibraryStateFilter StateFilter
    {
        get => _stateFilter;
        set
        {
            if (SetProperty(ref _stateFilter, value))
            {
                OnPropertyChanged(nameof(IsStateAll));
                OnPropertyChanged(nameof(IsStateLocal));
                OnPropertyChanged(nameof(IsStateOnline));
                OnPropertyChanged(nameof(IsStateUpdates));
                EndTap();
                ApplyFilter();
            }
        }
    }

    public bool IsStateAll { get => _stateFilter == LibraryStateFilter.All; set { if (value) StateFilter = LibraryStateFilter.All; } }

    public bool IsStateLocal { get => _stateFilter == LibraryStateFilter.Local; set { if (value) StateFilter = LibraryStateFilter.Local; } }

    public bool IsStateOnline { get => _stateFilter == LibraryStateFilter.Online; set { if (value) StateFilter = LibraryStateFilter.Online; } }

    public bool IsStateUpdates { get => _stateFilter == LibraryStateFilter.Updates; set { if (value) StateFilter = LibraryStateFilter.Updates; } }

    /// <summary>
    /// Lists only data valid at the Timeline's view time (#711, handoff G2):
    /// a toggle beside the filter box that narrows whichever segment is chosen.
    /// </summary>
    public bool IsAtViewTime
    {
        get => _atViewTime;
        set
        {
            if (SetProperty(ref _atViewTime, value))
            {
                EndTap();
                ApplyFilter();
            }
        }
    }

    /// <summary>"Only data valid at the view time · 12".</summary>
    public string AtViewTimeTooltip => string.Format(CultureInfo.CurrentCulture, Strings.Tooltip_LibraryAtViewTimeFormat, AtViewTimeCount);

    /// <summary>True when the Timeline has a view time, so "At view time" and the row's Timeline actions apply.</summary>
    public bool HasViewTime => _viewTime?.CurrentTime is not null && _viewTime.IsActive;

    /// <summary>Of the datasets matching the text, how many hold data at the view time.</summary>
    public int AtViewTimeCount { get; private set; }

    /// <summary>True when the selected dataset has a time window (forecast run or time coverage).</summary>
    public bool HasValidWindow => _selectedItem?.ValidWindow is not null;

    /// <summary>Shows the selected dataset's window on the Timeline (#711, handoff G3).</summary>
    public ICommand ShowOnTimelineCommand { get; }

    /// <summary>Moves the Timeline's view time to the start of the selected dataset's run or window.</summary>
    public ICommand GoToRunStartCommand { get; }

    /// <summary>Raised with a window the Timeline should show.</summary>
    public event EventHandler<(DateTime Start, DateTime End)>? ShowOnTimelineRequested;

    /// <summary>Raised with a time the Timeline should move to.</summary>
    public event EventHandler<DateTime>? GoToTimeRequested;

    /// <summary>Raised after <see cref="Reveal"/> selected a dataset, so the view can show the panel and scroll to it.</summary>
    public event EventHandler<LibraryItemViewModel>? Revealed;

    /// <summary>
    /// Selects a dataset by its source and key, as "Reveal in Library" does
    /// from the Timeline (#711, handoff G3): its source node, every state,
    /// no text filter, and its model's group opened.
    /// </summary>
    internal bool Reveal(Guid sourceId, string key)
    {
        var node = Nodes.SelectMany(n => n.SelfAndDescendants())
            .FirstOrDefault(n => !n.IsGroup && n.Source?.Id == sourceId && n.EnumerateItems().Any(p => p.Item.Key == key))
            ?? Nodes.FirstOrDefault(n => n.EnumerateItems().Any(p => p.Source.Id == sourceId && p.Item.Key == key));
        if (node is null)
            return false;
        if (_location is not null)
            SetLocation(null);
        _filterText = string.Empty;
        OnPropertyChanged(nameof(FilterText));
        _stateFilter = LibraryStateFilter.All;
        _atViewTime = false;
        OnPropertyChanged(nameof(StateFilter));
        OnPropertyChanged(nameof(IsStateAll));
        OnPropertyChanged(nameof(IsStateLocal));
        OnPropertyChanged(nameof(IsStateOnline));
        OnPropertyChanged(nameof(IsStateUpdates));
        OnPropertyChanged(nameof(IsAtViewTime));
        if (_selectedNode != node)
            SelectedNode = node;
        else
            ApplyFilter();
        var row = _allItems.FirstOrDefault(i => i.Source.Id == sourceId && i.Item.Key == key);
        if (row is not null && PackageOf(row) is { } package && _expandedPackages.Add(package))
            ApplyFilter();
        SelectedItem = _items.FirstOrDefault(i => i.Source.Id == sourceId && i.Item.Key == key);
        if (_selectedItem is { } selected)
            Revealed?.Invoke(this, selected);
        return _selectedItem is not null;
    }

    private void OnViewTimeChanged()
    {
        OnPropertyChanged(nameof(HasViewTime));
        ((RelayCommand)ShowOnTimelineCommand).NotifyCanExecuteChanged();
        ((RelayCommand)GoToRunStartCommand).NotifyCanExecuteChanged();
        if (_atViewTime)
            ApplyFilter();
        else
            Recount();
    }

    /// <summary>Datasets passing the text filter (the "All" segment's count).</summary>
    public int AllCount => _textFiltered.Count;

    /// <summary>Of those, how many are local and current (local, loaded or on pan).</summary>
    public int LocalCount { get; private set; }

    /// <summary>Of those, how many are online.</summary>
    public int OnlineCount { get; private set; }

    /// <summary>Of those, how many have an update available.</summary>
    public int UpdatesCount { get; private set; }

    /// <summary>Whether the listed datasets' coverage is drawn on the map.</summary>
    public bool ShowCoverage
    {
        get => _showCoverage;
        set => SetProperty(ref _showCoverage, value);
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

    /// <summary>Raised when the user asks to zoom the map to a dataset's bounds.</summary>
    public event EventHandler<GeoBounds>? ZoomRequested;

    /// <summary>Raised when the user asks to centre the map on a dataset without changing the zoom.</summary>
    public event EventHandler<GeoPosition>? CenterRequested;

    /// <summary>
    /// Centres the map on the selected dataset (its bounds' centre), keeping
    /// the zoom — e.g. when its row is double-clicked and it may be off screen.
    /// </summary>
    public void CenterOnSelected()
    {
        if (_selectedItem is { IsGroupHeader: false, Item.Bounds: { } bounds })
            CenterRequested?.Invoke(this, CenterOf(bounds));
    }

    /// <summary>The centre of <paramref name="bounds"/>, across the antimeridian when they cross it.</summary>
    internal static GeoPosition CenterOf(GeoBounds bounds)
    {
        var east = bounds.CrossesAntimeridian ? bounds.East + 360 : bounds.East;
        return new GeoPosition((bounds.South + bounds.North) / 2, GeoBounds.NormalizeLongitude((bounds.West + east) / 2));
    }

    /// <summary>"116", or "8 of 116" when filtered, shown inside the filter box.</summary>
    public string ItemsSummary =>
        ListedCount == _allItems.Count
            ? string.Format(CultureInfo.CurrentCulture, Strings.Library_ItemCountFormat, _allItems.Count)
            : string.Format(CultureInfo.CurrentCulture, Strings.Library_FilteredItemCountFormat, ListedCount, _allItems.Count);

    /// <summary>
    /// How many datasets pass the filters — including those inside a collapsed
    /// package group, and never the groups' header rows.
    /// </summary>
    private int ListedCount => _listedDatasets;

    public ICommand AddFolderCommand { get; }

    public ICommand AddExchangeSetZipCommand { get; }

    /// <summary>Opens the directory of known online catalogues (NOAA, USACE, …).</summary>
    public ICommand AddOnlineCatalogueCommand { get; }

    public ICommand AddS128CatalogueCommand { get; }

    /// <summary>Connects to a feed served by <c>s100 feed serve</c> on another computer.</summary>
    public ICommand AddSharedFeedCommand { get; }

    /// <summary>Picks a collection manifest (<c>*.s100collection.json</c>).</summary>
    public ICommand AddCollectionManifestCommand { get; }

    /// <summary>Changes which groups the selected collection-manifest source includes.</summary>
    public ICommand ChooseGroupsCommand { get; }

    /// <summary>Re-indexes every source.</summary>
    public ICommand RefreshAllCommand { get; }

    /// <summary>Starts renaming the selected collection or source in place.</summary>
    public ICommand RenameCommand { get; }

    /// <summary>Applies the name typed while renaming.</summary>
    public ICommand CommitRenameCommand { get; }

    /// <summary>Leaves renaming without changing the name.</summary>
    public ICommand CancelRenameCommand { get; }

    /// <summary>Re-indexes the selected node (or everything when nothing is selected).</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Removes the selected collection or source from the library (never the data).</summary>
    public ICommand RemoveCommand { get; }

    /// <summary>Persists the selected session S-128 catalogue as a collection.</summary>
    public ICommand KeepInLibraryCommand { get; }

    /// <summary>Zooms the map to the selected dataset.</summary>
    public ICommand ZoomToCommand { get; }

    /// <summary>Returns the list to the selected node's datasets.</summary>
    public ICommand ClearLocationCommand { get; }

    /// <summary>Selects the next dataset covering the tapped point (as tapping the same spot again does).</summary>
    public ICommand NextAtLocationCommand { get; }

    /// <summary>How many listed datasets cover the tapped point.</summary>
    public int LocationHitCount => _location is null ? 0 : ListedCount;

    /// <summary>The selected dataset's 1-based position among them, or 0 when none is selected.</summary>
    public int LocationHitIndex => _location is null || _selectedItem is null ? 0 : IndexOf(_selectedItem) + 1;

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
        _selectedItem is null ? 0 : TapHitIndexOf(_selectedItem) + 1, TapHitCount);

    /// <summary>Selects the next dataset the tap hit, wrapping round (as tapping the same spot again does).</summary>
    public ICommand NextTapHitCommand { get; }

    /// <summary>Lists the datasets at the tapped point, from every collection (what a tap used to do).</summary>
    public ICommand ListTapHitsCommand { get; }

    /// <summary>Ends the tap: hides the banner and clears the selection.</summary>
    public ICommand ClearTapCommand { get; }

    /// <summary>Loads the selected dataset now.</summary>
    public ICommand LoadCommand { get; }

    /// <summary>Registers every listed local dataset to load as it comes into view.</summary>
    public ICommand LoadAsYouPanCommand { get; }

    /// <summary>Downloads the selected online dataset, then loads it ("Load after download").</summary>
    public ICommand DownloadCommand { get; }

    /// <summary>Downloads the selected online dataset without loading it.</summary>
    public ICommand DownloadOnlyCommand { get; }

    /// <summary>
    /// The details header's one Load button: loads a local dataset, or
    /// downloads an online one and then loads it.
    /// </summary>
    public ICommand LoadOrDownloadCommand { get; }

    /// <summary>Downloads every listed online (or outdated) dataset.</summary>
    public ICommand DownloadListedCommand { get; }

    /// <summary>Cancels the running bulk download.</summary>
    public ICommand CancelDownloadsCommand { get; }

    /// <summary>True while a bulk download runs (the bulk bar shows its progress and Cancel).</summary>
    public bool IsBulkDownloading => _downloader.Progress is not null;

    /// <summary>True when there are listed datasets for the bulk bar to act on.</summary>
    public bool HasBulkBar => _items.Count > 0 || IsBulkDownloading;

    /// <summary>
    /// The bulk bar's first line: "6 to download · 16,4 MB" (or, while
    /// downloading, what is left).
    /// </summary>
    public string BulkSummary
    {
        get
        {
            if (_downloader.Progress is { } progress)
            {
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkToDownloadFormat,
                    progress.Remaining, LibraryItemViewModel.FormatBytes(progress.BytesLeft));
            }

            if (IsBulkOffline)
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkOfflineFormat, LocalListedCount());

            var c = CultureInfo.CurrentCulture;
            if (NewRunModels() is { Count: > 0 } newRuns)
            {
                var bytes = LibraryItemViewModel.FormatBytes(Downloadable().Sum(i => (i.Item.Location as RemoteItemLocation)?.SizeBytes ?? 0));
                return string.Format(c, newRuns.Count == 1 ? Strings.Library_BulkNewRunFormat : Strings.Library_BulkNewRunsFormat, newRuns.Count, bytes);
            }

            if (ExpiredModels() is { Count: > 0 } expired)
                return string.Format(c, expired.Count == 1 ? Strings.Library_BulkRunExpired : Strings.Library_BulkRunsExpiredFormat, expired.Count);

            var downloadable = Downloadable().ToArray();
            if (IsUpdateMode)
            {
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkUpdatesFormat, downloadable.Length,
                    LibraryItemViewModel.FormatBytes(downloadable.Sum(i => (i.Item.Location as RemoteItemLocation)?.SizeBytes ?? 0)));
            }

            return downloadable.Length == 0
                ? string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkListedFormat, ListedCount)
                : string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkToDownloadFormat, downloadable.Length,
                    LibraryItemViewModel.FormatBytes(downloadable.Sum(i => (i.Item.Location as RemoteItemLocation)?.SizeBytes ?? 0)));
        }
    }

    /// <summary>
    /// The bulk bar's second line: what the buttons act on ("Filtered set · 2
    /// already local"), or the download's progress.
    /// </summary>
    public string BulkScope
    {
        get
        {
            if (_downloader.Progress is { } progress)
            {
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkDownloadingFormat,
                    Math.Min(progress.Total, progress.Completed + progress.Failed + 1), progress.Total, progress.Fraction);
            }

            if (IsBulkOffline)
                return Strings.Library_BulkOfflineScope;

            if (NewRunModels() is { Count: > 0 } newRuns)
            {
                // "cbofs 18:00Z replaces 12:00Z" for one model; the models otherwise.
                var first = ListedDatasets().First(i => i.IsForecast && i.Availability == LibraryAvailability.Outdated);
                return newRuns.Count == 1
                    && EncDotNet.S100.Collections.Indexing.S100ForecastFeedIndexer.RunOf(first.Item) is { } onlineRun
                    && _downloader.LocalPublishedAtOf(first.Item) is { } localRun
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkRunReplacesFormat, newRuns[0],
                        onlineRun.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + "Z",
                        localRun.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + "Z")
                    : string.Join(", ", newRuns);
            }

            if (ExpiredModels() is { Count: > 0 })
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkLastCheckedFormat, LastChecked());

            if (IsUpdateMode)
            {
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkUpdatesScopeFormat,
                    LocalListedCount(), _items.Count(i => !i.IsGroupHeader));
            }

            if (DownloadableCount == 0)
                return Strings.Library_BulkNothing;

            var scope = IsFiltered ? Strings.Library_BulkScopeFiltered : Strings.Library_BulkScopeAll;
            var local = ListedDatasets().Count(i => i.PrimaryAvailability == LibraryPrimaryAvailability.Local && !_downloader.IsOutdated(i.Item));
            return local == 0 ? scope : string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkAlreadyLocalFormat, scope, local);
        }
    }

    /// <summary>True when the list is narrowed by text, state or a map tap.</summary>
    private bool IsFiltered => _filterText.Trim().Length > 0 || _stateFilter != LibraryStateFilter.All || _location is not null;

    /// <summary>How many listed datasets can be downloaded.</summary>
    public int DownloadableCount => Downloadable().Count();

    private IEnumerable<LibraryItemViewModel> Downloadable() =>
        IsBulkOffline ? []
        : NewRunModels().Count > 0
            ? ListedDatasets().Where(i => i.IsForecast && i.Availability == LibraryAvailability.Outdated && _downloader.CanDownload(i.Item))
        : ExpiredModels().Count > 0 ? []
        : IsUpdateMode ? ListedDatasets().Where(i => i.QuietUpdates && i.Availability == LibraryAvailability.Outdated && _downloader.CanDownload(i.Item))
        : ListedDatasets().Where(i => _downloader.CanDownload(i.Item) && (i.EffectiveItem.Location is RemoteItemLocation || _downloader.IsOutdated(i.Item)));

    /// <summary>
    /// The listed datasets, including the tiles under a collapsed forecast
    /// model's row (an unpacked package's rows are local already).
    /// </summary>
    private IEnumerable<LibraryItemViewModel> ListedDatasets() =>
        _items.SelectMany(i => i.IsModelHeader ? i.Members : i.IsGroupHeader ? [] : [i]).Distinct();

    /// <summary>The listed forecast models with a newer run than the one downloaded (#685).</summary>
    private IReadOnlyList<string> NewRunModels() => ForecastModelsIn(LibraryAvailability.Outdated);

    /// <summary>The listed forecast models whose downloaded run has ended, with nothing newer known.</summary>
    private IReadOnlyList<string> ExpiredModels() => ForecastModelsIn(LibraryAvailability.Expired);

    private IReadOnlyList<string> ForecastModelsIn(LibraryAvailability state) =>
        ListedDatasets()
            .Where(i => i.IsForecast && i.Availability == state)
            .Select(i => ForecastRuns.ModelOf(i.Item))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// True when listed forecast runs have ended and no newer run is known:
    /// the bulk bar offers "Check for new runs" (Refresh) instead of a download.
    /// </summary>
    public bool IsCheckForRuns => !IsBulkDownloading && !IsBulkOffline && NewRunModels().Count == 0 && ExpiredModels().Count > 0;

    /// <summary>When the selected node's catalogues were last checked ("21:04" today, else the date).</summary>
    private string LastChecked()
    {
        var sources = _selectedNode is { } node ? node.Source is { } s ? [s] : node.Collection.Sources : [];
        var checkedAt = sources
            .Select(x => _feedHealth?.Invoke(x.Definition)?.CheckedAt ?? x.Index?.IndexedAt)
            .OfType<DateTimeOffset>()
            .DefaultIfEmpty(_time.GetUtcNow())
            .Max()
            .ToLocalTime();
        return checkedAt.Date == _time.GetLocalNow().Date
            ? checkedAt.ToString("t", CultureInfo.CurrentCulture)
            : checkedAt.ToString("d", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// True when listed remote-catalogue datasets have newer editions online:
    /// the bulk bar then offers "Update downloaded (N)" for those alone (#685),
    /// rather than mixing updates into a download of everything listed.
    /// </summary>
    public bool IsUpdateMode => ListedDatasets().Any(i => i.QuietUpdates && !i.IsForecast && i.Availability == LibraryAvailability.Outdated);

    /// <summary>
    /// True when the selected node is remote catalogues whose server could not
    /// be reached: the cached index still lists and loads, but nothing downloads.
    /// </summary>
    public bool IsBulkOffline =>
        !IsBulkDownloading
        && _selectedNode is { } node
        && (node.Source is { } source ? [source] : node.Collection.Sources) is { Count: > 0 } sources
        && sources.All(s => s.Definition is S100CatalogueFeedSource or S100ForecastFeedSource
            && _feedHealth?.Invoke(s.Definition) is { IsReachable: false });

    /// <summary>How many listed datasets have a copy on disk (current or not).</summary>
    private int LocalListedCount() =>
        ListedDatasets().Count(i => i.PrimaryAvailability is LibraryPrimaryAvailability.Local
            or LibraryPrimaryAvailability.Update or LibraryPrimaryAvailability.Expired);

    /// <summary>True when some listed dataset can be downloaded (and no bulk download is running).</summary>
    public bool HasDownloadable => !IsBulkDownloading && DownloadableCount > 0;

    /// <summary>"Download 1,193 (203 MB)" for the bulk download button.</summary>
    public string DownloadListedText
    {
        get
        {
            if (NewRunModels() is { Count: > 0 } newRuns)
                return string.Format(CultureInfo.CurrentCulture, Strings.Library_UpdateDownloadedFormat, newRuns.Count);
            return string.Format(CultureInfo.CurrentCulture,
                IsUpdateMode ? Strings.Library_UpdateDownloadedFormat : Strings.Library_DownloadListedFormat, DownloadableCount);
        }
    }

    /// <summary>
    /// Selects the tree node of a remote catalogue's area (a map tap on it
    /// when zoomed out; #685): its group node, or the source when it shows no
    /// area nodes. The collection and source open so the node is visible.
    /// </summary>
    public void SelectArea(Guid sourceId, string folder)
    {
        foreach (var collection in Nodes)
        {
            if (collection.Children.FirstOrDefault(c => c.Id == sourceId) is not { } source)
                continue;

            collection.IsExpanded = true;
            source.IsExpanded = true;
            SelectedNode = source.Children.FirstOrDefault(c => c.GroupId == folder) ?? source;
            return;
        }
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
        SelectedItem = smallestFirst
            .Select(m => _items.FirstOrDefault(i => i.Source.Id == m.SourceId && i.IsForecast
                && (i.IsModelHeader || i.IsForecastRunRow) && ForecastRuns.ModelOf(i.Item) == m.Model))
            .FirstOrDefault(i => i is not null) ?? _selectedItem;
    }

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

        var current = _selectedItem is null ? -1 : IndexIn(hits, _selectedItem);
        _tapPosition = position;
        _tapHits = hits;
        SelectTapHit(sameSpot && current >= 0 ? (current + 1) % hits.Count : 0);
        OnTapChanged();
    }

    /// <summary>Ends any map tap and clears the selection (a tap where nothing is outlined, or the banner's ×).</summary>
    public void ClearTap()
    {
        EndTap();
        SelectedItem = null;
    }

    private void NextTapHit()
    {
        if (_tapHits is not { Count: > 0 } hits)
            return;
        var current = _selectedItem is null ? -1 : IndexIn(hits, _selectedItem);
        SelectTapHit((current + 1) % hits.Count);
    }

    private void SelectTapHit(int index)
    {
        var hit = _tapHits![index];
        _selectingTapHit = true;
        try
        {
            // The list may have been rebuilt since the tap; select the row as listed now.
            SelectedItem = _items.FirstOrDefault(i => SameItem(i, hit)) ?? hit;
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

    /// <summary>Forgets the map tap (the banner hides); the selection is left alone.</summary>
    private void EndTap()
    {
        if (_tapHits is null)
            return;
        _tapHits = null;
        _tapPosition = null;
        OnTapChanged();
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

    /// <summary>Selects the next dataset at the tapped point, wrapping round.</summary>
    private void NextAtLocation()
    {
        if (_location is null || _items.Count == 0)
            return;
        var next = _selectedItem is null ? 0 : (IndexOf(_selectedItem) + 1) % _items.Count;
        SelectedItem = _items[next];
    }

    private int IndexOf(LibraryItemViewModel item)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (SameItem(_items[i], item))
                return i;
        }

        return -1;
    }

    public void Dispose()
    {
        _clock?.Dispose();
        _library.Changed -= OnLibraryChanged;
        _loader.Changed -= OnLoaderChanged;
        _downloader.Changed -= OnLoaderChanged;
        _downloader.ProgressChanged -= OnDownloadProgress;
    }

    private void OnDownloadProgress(object? sender, EventArgs e)
    {
        lock (Nodes)
        {
            if (_progressRefreshPosted)
                return;
            _progressRefreshPosted = true;
        }

        _dispatch(() =>
        {
            lock (Nodes)
                _progressRefreshPosted = false;
            RefreshDownloadProgress();
        });
    }

    /// <summary>Updates rows, the bulk bar and the tree's download lines — without re-resolving availability.</summary>
    private void RefreshDownloadProgress()
    {
        foreach (var item in _items)
            item.RefreshDownload();
        OnPropertyChanged(nameof(IsBulkDownloading));
        OnPropertyChanged(nameof(HasDownloadable));
        OnPropertyChanged(nameof(HasBulkBar));
        OnPropertyChanged(nameof(BulkSummary));
        OnPropertyChanged(nameof(BulkScope));
        OnPropertyChanged(nameof(IsBulkOffline));
        OnPropertyChanged(nameof(IsCheckForRuns));
        OnPropertyChanged(nameof(DownloadListedText));
        ((RelayCommand)CancelDownloadsCommand).NotifyCanExecuteChanged();
        UpdateNodeDownloadStatus();
    }

    /// <summary>
    /// Every minute: forecast runs lose time and may expire (#685), so their
    /// rows, the counts, the status lines and the bulk bar are re-evaluated.
    /// </summary>
    private void OnClockTick()
    {
        var forecasts = _allItems.Where(i => i.IsForecast).ToArray();
        if (forecasts.Length == 0)
            return;

        foreach (var item in forecasts)
            item.RefreshClock();
        foreach (var header in _packageHeaders.Values.Where(h => h.IsModelHeader))
            header.Members = header.Members;
        UpdateCatalogueCounts();
        if (_stateFilter == LibraryStateFilter.All)
            Recount();
        else
            ApplyFilter();
        RefreshDownloadProgress();
        ((AsyncRelayCommand)DownloadListedCommand).NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasDownloadable));
    }

    /// <summary>
    /// Gives each remote S-100 catalogue node its local and update counts for
    /// its status line ("Catalogue 30.09 · 140 updates"); a one-source
    /// collection shows its source's.
    /// </summary>
    private void UpdateCatalogueCounts()
    {
        foreach (var collection in Nodes)
        {
            LibraryCatalogueCounts? only = null;
            LibraryForecastCounts? onlyForecast = null;
            foreach (var child in collection.Children)
            {
                if (child.Source is { Definition: S100ForecastFeedSource, Index: { } runs })
                {
                    child.ForecastCounts = ForecastCountsOf(runs);
                    if (collection.Children.Count == 1)
                        onlyForecast = child.ForecastCounts;
                    continue;
                }

                if (child.Source is not { Definition: S100CatalogueFeedSource, Index: { } index })
                    continue;

                int local = 0, outdated = 0;
                foreach (var item in index.Items)
                {
                    if (_downloader.Localize(item).Location is not LocalItemLocation)
                        continue;
                    local++;
                    if (_downloader.IsOutdated(item))
                        outdated++;
                }

                child.CatalogueCounts = new LibraryCatalogueCounts(index.Items.Count, local, outdated);
                if (collection.Children.Count == 1)
                    only = child.CatalogueCounts;
            }

            collection.CatalogueCounts = only;
            collection.ForecastCounts = onlyForecast;
        }
    }

    /// <summary>
    /// Per model of a forecast source: whether a run is on disk, whether a
    /// newer run is listed, whether the downloaded run has ended, and the least
    /// time left among the downloaded runs.
    /// </summary>
    private LibraryForecastCounts ForecastCountsOf(SourceIndex index)
    {
        var now = _time.GetUtcNow();
        int models = 0, local = 0, newer = 0, expired = 0;
        TimeSpan? left = null;
        foreach (var model in index.Items.GroupBy(i => ForecastRuns.ModelOf(i) ?? i.Name, StringComparer.Ordinal))
        {
            models++;
            var downloaded = model.FirstOrDefault(i => _downloader.LocalPublishedAtOf(i) is not null);
            if (downloaded is null)
                continue;

            local++;
            if (model.Any(_downloader.IsOutdated))
            {
                newer++;
                continue;
            }

            var end = _downloader.LocalPublishedAtOf(downloaded) + ForecastRuns.Horizon(downloaded);
            if (end is { } e && e <= now)
                expired++;
            else if (end is { } e2 && (left is null || e2 - now < left))
                left = e2 - now;
        }

        return new LibraryForecastCounts(models, local, newer, expired, left);
    }

    /// <summary>Sets "Downloading 2 of 5 · 4,3 MB left" on the nodes whose datasets are downloading.</summary>
    private void UpdateNodeDownloadStatus()
    {
        var bySource = new Dictionary<Guid, string?>();
        lock (_downloadingBySource)
        {
            foreach (var (sourceId, items) in _downloadingBySource.ToArray())
            {
                var statuses = items.Select(_downloader.StatusOf).ToArray();
                var pending = statuses.Count(s => s is { State: not LibraryDownloadItemState.Failed });
                if (pending == 0 || _downloader.Progress is null)
                {
                    _downloadingBySource.Remove(sourceId);
                    bySource[sourceId] = null;
                    continue;
                }

                var left = items.Zip(statuses)
                    .Where(p => p.Second is { State: not LibraryDownloadItemState.Failed })
                    .Sum(p => Math.Max(0, ((p.First.Location as RemoteItemLocation)?.SizeBytes ?? 0) - p.Second!.BytesReceived));
                bySource[sourceId] = string.Format(CultureInfo.CurrentCulture, Strings.Library_StatusLine_DownloadingFormat,
                    items.Count - pending + 1, items.Count, LibraryItemViewModel.FormatBytes(left));
            }
        }

        foreach (var collection in Nodes)
        {
            string? collectionStatus = null;
            foreach (var child in collection.Children)
            {
                if (bySource.TryGetValue(child.Id, out var status))
                    child.DownloadStatus = status;
                collectionStatus ??= child.DownloadStatus;
            }

            collection.DownloadStatus = collectionStatus;
        }
    }

    private void OnLoaderChanged(object? sender, EventArgs e)
    {
        // Coalesce bursts (e.g. one notification per downloaded cell).
        lock (Nodes)
        {
            if (_availabilityRefreshPosted)
                return;
            _availabilityRefreshPosted = true;
        }

        _dispatch(() =>
        {
            lock (Nodes)
                _availabilityRefreshPosted = false;
            // Every row, not only the listed ones: the state segments count them all.
            // (A row whose availability was never resolved returns at once.)
            foreach (var item in _allItems)
                item.RefreshAvailability();
            foreach (var header in _packageHeaders.Values.Where(h => h.IsModelHeader))
                header.Members = header.Members;
            UpdateCatalogueCounts();
            UpdatePairing();
            RefreshDownloadProgress();
            // A dataset whose state changed may now belong to another segment.
            if (_stateFilter == LibraryStateFilter.All)
                Recount();
            else
                ApplyFilter();
            ((AsyncRelayCommand)LoadCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)DownloadCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)DownloadOnlyCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)LoadOrDownloadCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)DownloadListedCommand).NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(DownloadListedText));
            OnPropertyChanged(nameof(HasDownloadable));
            OnPropertyChanged(nameof(BulkSummary));
            OnPropertyChanged(nameof(BulkScope));
            // The coverage overlay styles by availability; let it redraw.
            OnPropertyChanged(nameof(Items));
        });
    }

    private Task LoadSelectedAsync() =>
        _selectedItem is { } item ? _loader.LoadAsync([item.EffectiveItem], defer: false) : Task.CompletedTask;

    private Task LoadListedAsYouPanAsync() =>
        _loader.LoadAsync(ListedDatasets().Select(i => i.EffectiveItem).ToArray(), defer: true);

    private Task DownloadSelectedAsync() => DownloadSelectedAsync(load: true);

    private Task DownloadSelectedAsync(bool load) =>
        _selectedItem is { } item ? DownloadItemAsync(item, load) : Task.CompletedTask;

    /// <summary>Downloads one row (the details' Download, "Load after download", or a retry), then optionally loads it.</summary>
    private Task DownloadItemAsync(LibraryItemViewModel item, bool load) => DownloadRowsAsync([item], load);

    /// <summary>
    /// Downloads <paramref name="rows"/> as the details' Download does, then
    /// optionally loads them (also used by the MCP <c>library_action</c> tool, #715).
    /// </summary>
    internal async Task DownloadRowsAsync(IReadOnlyList<LibraryItemViewModel> rows, bool load)
    {
        TrackDownloads(rows);
        var result = await _downloader.DownloadAsync(rows.Select(row => row.Item).ToArray()).ConfigureAwait(true);
        if (result.Downloaded == 0)
            return;

        // A package's cells (and their coverage) appear once its source re-indexes.
        if (ReindexPackageSources(rows))
        {
            await AnnouncePackagesAsync(rows).ConfigureAwait(true);
            return;
        }

        foreach (var row in rows)
            row.RefreshAvailability();
        if (load)
            await _loader.LoadAsync(rows.Select(row => row.EffectiveItem).ToArray(), defer: false).ConfigureAwait(true);
    }

    /// <summary>Opens <paramref name="rows"/> now, or as the map pans to them when <paramref name="defer"/> is true.</summary>
    internal Task<LibraryLoadResult> LoadRowsAsync(IReadOnlyList<LibraryItemViewModel> rows, bool defer) =>
        _loader.LoadAsync(rows.Select(row => row.EffectiveItem).ToArray(), defer);

    /// <summary>The downloader behind the panel, for download progress and cancelling.</summary>
    internal ILibraryDownloader Downloader => _downloader;

    private async Task DownloadListedAsync()
    {
        var items = Downloadable().ToArray();
        TrackDownloads(items);
        var result = await _downloader.DownloadAsync(items.Select(i => i.Item).ToArray()).ConfigureAwait(true);
        if (result.Downloaded > 0 && ReindexPackageSources(items))
            await AnnouncePackagesAsync(items).ConfigureAwait(true);
    }

    /// <summary>
    /// Once the re-index lists an unpacked package's datasets, opens its group
    /// (once) and says how many datasets it held.
    /// </summary>
    private async Task AnnouncePackagesAsync(IEnumerable<LibraryItemViewModel> items)
    {
        var packages = items
            .Where(i => i.IsPackageEntry)
            .Select(i => (i.Source.Id, ((RemoteItemLocation)i.Item.Location).Package!))
            .Distinct()
            .ToArray();
        if (packages.Length == 0)
            return;

        await _library.WhenIdle().ConfigureAwait(true);
        var counts = packages.Select(p => _library.Collections
                .SelectMany(c => c.Sources)
                .Where(s => s.Id == p.Item1)
                .SelectMany(s => s.Index?.Items ?? [])
                .Count(i => i.Properties.TryGetValue("package", out var pkg) && pkg == p.Item2 && i.Key != pkg))
            .ToArray();
        foreach (var package in packages)
            _expandedPackages.Add(package);
        _dispatch(() => RebuildItems(force: true));

        var datasets = counts.Sum();
        if (datasets == 0)
            return;
        _notifications?.Create(Strings.Toast_LibraryUnpackedTitle)
            .WithSeverity(Services.Notifications.NotificationSeverity.Success)
            .WithContent(packages.Length == 1
                ? string.Format(CultureInfo.CurrentCulture, Strings.Toast_LibraryUnpackedFormat, datasets)
                : string.Format(CultureInfo.CurrentCulture, Strings.Toast_LibraryUnpackedManyFormat, packages.Length, datasets))
            .Show();
    }

    /// <summary>Remembers which sources' datasets are downloading, for the tree's status lines.</summary>
    private void TrackDownloads(IEnumerable<LibraryItemViewModel> items)
    {
        lock (_downloadingBySource)
        {
            foreach (var group in items.GroupBy(i => i.Source.Id))
                _downloadingBySource[group.Key] = group.Select(i => i.Item).ToList();
        }
    }

    /// <summary>
    /// Re-indexes the sources of any package items among <paramref name="items"/>
    /// (community lists list a downloaded package's cells, not the package).
    /// Items with a stated layout (S-100 feeds) are already listed as
    /// themselves and need no re-index. Returns true when there were any.
    /// </summary>
    private bool ReindexPackageSources(IEnumerable<LibraryItemViewModel> items)
    {
        var sources = items
            .Where(i => i.Item.Location is RemoteItemLocation { Package: not null, Layout: null })
            .Select(i => i.Source.Id)
            .Distinct()
            .ToArray();
        foreach (var source in sources)
            _library.Refresh(sourceId: source);
        return sources.Length > 0;
    }

    /// <summary>
    /// The persisted collection new sources are added to by default: the
    /// selected node's collection, unless it is the session collection.
    /// </summary>
    private Guid? TargetCollectionId =>
        _selectedNode is { Collection.IsSession: false } node ? node.Collection.Id : null;

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        // Coalesce bursts of indexing notifications into one UI update.
        lock (Nodes)
        {
            if (_refreshPosted)
                return;
            _refreshPosted = true;
        }

        _dispatch(() =>
        {
            lock (Nodes)
                _refreshPosted = false;
            Sync();
        });
    }

    /// <summary>Brings <see cref="Nodes"/> in line with the library snapshot.</summary>
    internal void Sync()
    {
        var snapshot = _library.Collections;
        var knownSources = Nodes.SelectMany(n => n.Children).Select(c => c.Id).ToHashSet();

        for (var i = Nodes.Count - 1; i >= 0; i--)
        {
            if (!snapshot.Any(c => c.Id == Nodes[i].Id))
            {
                if (_selectedNode is not null && Nodes[i].SelfAndDescendants().Contains(_selectedNode))
                    SelectedNode = null;
                Nodes.RemoveAt(i);
            }
        }

        for (var i = 0; i < snapshot.Count; i++)
        {
            var collection = snapshot[i];
            var existing = Nodes.FirstOrDefault(n => n.Id == collection.Id);
            if (existing is null)
            {
                Nodes.Insert(i, LibraryNodeViewModel.ForCollection(collection, _feedHealth));
                continue;
            }

            var selected = _selectedNode is { IsCollection: false } node && existing.SelfAndDescendants().Contains(node)
                ? node
                : null;
            existing.Update(collection);
            if (selected is not null && !existing.SelfAndDescendants().Contains(selected))
            {
                // A vanished group falls back to its source, a vanished source to its collection.
                SelectedNode = selected.IsGroup
                    ? existing.Children.FirstOrDefault(c => c.Id == selected.Id) ?? existing
                    : existing;
            }

            var at = Nodes.IndexOf(existing);
            if (at != i)
                Nodes.Move(at, i);
        }

        // A newly added collection manifest opens expanded, with its collection, once.
        if (_hasSynced)
        {
            foreach (var collection in Nodes)
            {
                foreach (var source in collection.Children.Where(
                    c => c.Source?.Definition is LocalManifestSource && !knownSources.Contains(c.Id)))
                {
                    collection.IsExpanded = true;
                    source.IsExpanded = true;
                }
            }
        }

        _hasSynced = true;

        SelectedNode ??= Nodes.FirstOrDefault();
        UpdateCatalogueCounts();
        OnPropertyChanged(nameof(IsEmpty));
        ((RelayCommand)RemoveCommand).NotifyCanExecuteChanged();
        ((RelayCommand)KeepInLibraryCommand).NotifyCanExecuteChanged();
        ((RelayCommand)RenameCommand).NotifyCanExecuteChanged();
        RebuildItems(force: false);
    }

    private void SetLocation(GeoPosition? position)
    {
        _location = position;
        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(HasLocation));
        OnPropertyChanged(nameof(HasTap));
        OnPropertyChanged(nameof(LocationSummary));
        ((RelayCommand)NextAtLocationCommand).NotifyCanExecuteChanged();

        var selected = _selectedItem;
        _itemsBasis = null;
        _allItems = position is { } p ? HitsAt(p) : BuildNodeItems(_selectedNode);
        ApplyFilter();
        SelectedItem = selected is null ? null : _items.FirstOrDefault(i => SameItem(i, selected));
    }

    /// <summary>
    /// Every library dataset covering <paramref name="position"/>, most
    /// detailed (highest usage band, then smallest extent) first.
    /// </summary>
    internal List<LibraryItemViewModel> HitsAt(GeoPosition position) =>
        _library.Collections
            .SelectMany(c => c.Sources)
            .SelectMany(s => (s.Index?.Items ?? []).Select(i => (Item: i, Source: s)))
            .Where(p => CoverageGeometry.Contains(p.Item, position))
            .OrderByDescending(p => p.Item.UsageBand ?? 0)
            .ThenBy(p => CoverageGeometry.Area(p.Item))
            .Select(p => CreateItem(p.Item, p.Source))
            .ToList();

    /// <summary>The collections the Library holds now, sources and indexes included.</summary>
    internal IReadOnlyList<LibraryCollection> Collections => _library.Collections;

    /// <summary>
    /// A row view model for <paramref name="item"/>, wired exactly as the
    /// panel's own rows are, so its state, tags and details match what the
    /// user sees (used by the MCP Library tools, #715).
    /// </summary>
    internal LibraryItemViewModel CreateItem(CollectionItem item, LibrarySource source) =>
        new(item, source, _loader.StateOf, _downloader, CollectionNameOf(source.Id), RetryDownloadAsync, _time);

    private string? CollectionNameOf(Guid sourceId) =>
        _library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == sourceId))?.Definition.Name;

    private IReadOnlyList<LibraryItemViewModel> BuildNodeItems(LibraryNodeViewModel? node) =>
        node is null
            ? []
            : node.EnumerateItems()
                .Select(p => new LibraryItemViewModel(p.Item, p.Source, _loader.StateOf, _downloader, node.Collection.Definition.Name, RetryDownloadAsync, _time))
                .ToArray();

    private Task RetryDownloadAsync(LibraryItemViewModel item) => DownloadItemAsync(item, load: false);

    private static bool SameItem(LibraryItemViewModel a, LibraryItemViewModel b) =>
        a.Source.Id == b.Source.Id && a.Item.Key == b.Item.Key;

    private void ZoomToSelected()
    {
        if (_selectedItem?.Item.Bounds is { } bounds)
            ZoomRequested?.Invoke(this, bounds);
    }

    private void RebuildItems(bool force)
    {
        if (_location is { } location)
        {
            // Listing datasets at a map location: refresh the hits only when
            // the library itself changed.
            if (force)
                return;
            SetLocation(location);
            return;
        }

        var node = _selectedNode;
        var basis = node?.ItemIndexes;
        if (!force && basis is not null && _itemsBasis is not null
            && basis.Count == _itemsBasis.Count && basis.Zip(_itemsBasis).All(p => ReferenceEquals(p.First, p.Second)))
        {
            return;
        }

        _itemsBasis = basis;
        var selectedKey = _selectedItem is { } sel ? (sel.Source.Id, sel.Item.Key) : default;
        _allItems = BuildNodeItems(node);
        ApplyFilter();

        SelectedItem = selectedKey == default
            ? null
            : _items.FirstOrDefault(i => i.Source.Id == selectedKey.Id && i.Item.Key == selectedKey.Key);
    }

    private void ApplyFilter()
    {
        var filter = _filterText.Trim();
        _textMatched = _allItems
            .Where(i => _showCancelled || !i.IsCancelled)
            .Where(i => filter.Length == 0 || i.Matches(filter))
            .ToArray();
        var viewTime = ViewTime;
        _textFiltered = _atViewTime ? [.. _textMatched.Where(i => IsValidAt(i, viewTime))] : _textMatched;
        var listed = _textFiltered.Where(i => InState(i, _stateFilter)).ToArray();
        _listedDatasets = listed.Length;
        Items = GroupPackages(listed, expandAll: filter.Length > 0);
        Recount();
        OnPropertyChanged(nameof(LocationHitCount));
        OnPropertyChanged(nameof(LocationHitsText));
        OnPropertyChanged(nameof(LocationCoverText));
        OnPropertyChanged(nameof(LocationPositionText));
        ((RelayCommand)NextAtLocationCommand).NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ItemsSummary));
        OnPropertyChanged(nameof(HasBulkBar));
        OnPropertyChanged(nameof(IsBulkOffline));
        OnPropertyChanged(nameof(IsUpdateMode));
        OnPropertyChanged(nameof(IsCheckForRuns));
        OnPropertyChanged(nameof(BulkSummary));
        OnPropertyChanged(nameof(BulkScope));
        ((AsyncRelayCommand)LoadAsYouPanCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)DownloadListedCommand).NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DownloadListedText));
        OnPropertyChanged(nameof(HasDownloadable));

        if (_selectedItem is not null && !_items.Contains(_selectedItem))
            SelectedItem = null;
    }

    /// <summary>
    /// Lists the datasets an unpacked community-list package holds under a
    /// header row named by the package (collapsed unless it was just
    /// unpacked, or the text filter matched inside it), so the entry the user
    /// downloaded does not simply disappear.
    /// </summary>
    private IReadOnlyList<LibraryItemViewModel> GroupPackages(IEnumerable<LibraryItemViewModel> items, bool expandAll)
    {
        var rows = new List<LibraryItemViewModel>();
        var groups = new Dictionary<(Guid, string), List<LibraryItemViewModel>>();
        foreach (var item in items)
        {
            if (PackageOf(item) is { } key)
            {
                if (!groups.TryGetValue(key, out var members))
                {
                    groups[key] = members = [];
                    rows.Add(item);  // the group's place in the list
                }

                members.Add(item);
            }
            else
            {
                rows.Add(item);
            }
        }

        if (groups.Count == 0)
            return rows;

        var result = new List<LibraryItemViewModel>(rows.Count + groups.Count);
        foreach (var row in rows)
        {
            if (PackageOf(row) is not { } key)
            {
                result.Add(row);
                continue;
            }

            var members = groups[key];
            var expanded = expandAll || _expandedPackages.Contains(key);
            if (row.IsForecast)
            {
                if (!_packageHeaders.TryGetValue(key, out var model) || !ReferenceEquals(model.Source, row.Source))
                {
                    _packageHeaders[key] = model = LibraryItemViewModel.ForModelGroup(
                        row.Source, members, expanded, TogglePackage, _loader.StateOf, _downloader, _time);
                }
                else
                {
                    model.Members = members;
                }

                model.IsExpanded = expanded;
                result.Add(model);
                if (expanded)
                {
                    foreach (var member in members)
                        member.IsGroupChild = true;
                    result.AddRange(members);
                }

                continue;
            }

            if (!_packageHeaders.TryGetValue(key, out var header) || !ReferenceEquals(header.Source, row.Source))
            {
                _packageHeaders[key] = header = LibraryItemViewModel.ForPackageGroup(
                    row.Source, key.Item2, row.Item.Properties.GetValueOrDefault("packageTitle"), members.Count, expanded, TogglePackage,
                    members.Select(m => (m.Item.Location as RemoteItemLocation)?.LastModified).Max());
            }

            header.GroupCount = members.Count;
            header.IsExpanded = expanded;
            result.Add(header);
            if (expanded)
            {
                foreach (var member in members)
                    member.IsGroupChild = true;
                result.AddRange(members);
            }
        }

        return result;
    }

    /// <summary>
    /// The row a dataset is listed under: the unpacked package a community-list
    /// dataset came from, or the model of a forecast run's tile (#685); else
    /// <see langword="null"/>.
    /// </summary>
    private static (Guid, string)? PackageOf(LibraryItemViewModel item) =>
        item.IsForecast && item.Source.Definition is S100ForecastFeedSource { Shape: ForecastShape.Tiles }
            && ForecastRuns.ModelOf(item.Item) is { } model
            ? (item.Source.Id, "model:" + model)
        : item.Source.Definition is ChartCatalogsFeedSource
        && item.Item.Properties.TryGetValue("package", out var package)
        && item.Item.Key != package
            ? (item.Source.Id, package)
            : null;

    private void TogglePackage(LibraryItemViewModel header)
    {
        if (!_expandedPackages.Remove(header.GroupKey))
            _expandedPackages.Add(header.GroupKey);
        var selected = _selectedItem;
        ApplyFilter();
        SelectedItem = selected is null ? null : _items.FirstOrDefault(i => SameItem(i, selected));
    }

    private DateTime? ViewTime => _viewTime?.IsActive == true ? _viewTime.CurrentTime : null;

    /// <summary>The Timeline's view time, while it has one (for the MCP <c>validAt</c> filter).</summary>
    internal DateTime? CurrentViewTime => ViewTime;

    /// <summary>True when the item's data covers <paramref name="viewTime"/>.</summary>
    private static bool IsValidAt(LibraryItemViewModel item, DateTime? viewTime) =>
        viewTime is { } at && item.ValidWindow is { } window && at >= window.Start && at <= window.End;

    private static bool InState(LibraryItemViewModel item, LibraryStateFilter state) => state switch
    {
        LibraryStateFilter.Local => item.Availability is LibraryAvailability.Local or LibraryAvailability.Loaded or LibraryAvailability.Deferred,
        LibraryStateFilter.Updates when item.IsForecast => item.Availability is LibraryAvailability.Outdated or LibraryAvailability.Expired,
        LibraryStateFilter.Online => item.Availability == LibraryAvailability.Online,
        LibraryStateFilter.Updates => item.Availability == LibraryAvailability.Outdated,
        _ => true,
    };

    /// <summary>Updates the segment counts (availability can change without the list changing).</summary>
    private void Recount()
    {
        LocalCount = _textFiltered.Count(i => InState(i, LibraryStateFilter.Local));
        OnlineCount = _textFiltered.Count(i => InState(i, LibraryStateFilter.Online));
        UpdatesCount = _textFiltered.Count(i => InState(i, LibraryStateFilter.Updates));
        var viewTime = ViewTime;
        AtViewTimeCount = viewTime is null ? 0 : _textMatched.Count(i => IsValidAt(i, viewTime));
        OnPropertyChanged(nameof(AtViewTimeCount));
        OnPropertyChanged(nameof(AtViewTimeTooltip));
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(LocalCount));
        OnPropertyChanged(nameof(OnlineCount));
        OnPropertyChanged(nameof(UpdatesCount));
    }

    private Task ChooseGroupsAsync() =>
        _selectedNode is { CanChooseGroups: true, Source.Definition: LocalManifestSource source } node
            ? _importer.ChooseManifestGroupsAsync(node.Collection.Id, source)
            : Task.CompletedTask;

    private void Refresh()
    {
        if (_selectedNode is { Collection.IsSession: false } node)
            _library.Refresh(node.Collection.Id, node.Source?.Id);
        else
            _library.Refresh();
    }

    private void Remove()
    {
        if (_selectedNode is not { CanRemove: true } node)
            return;

        if (node.IsCollection)
            _library.RemoveCollection(node.Collection.Id);
        else
            _library.RemoveSource(node.Collection.Id, node.Id);
    }

    private void BeginRename()
    {
        if (_selectedNode is not { CanRename: true } node)
            return;
        foreach (var other in Nodes.SelectMany(n => n.SelfAndDescendants()).Where(n => n.IsRenaming))
            other.IsRenaming = false;
        node.RenameText = node.Name;
        node.IsRenaming = true;
    }

    private void CommitRename()
    {
        var node = Nodes.SelectMany(n => n.SelfAndDescendants()).FirstOrDefault(n => n.IsRenaming);
        if (node is null)
            return;

        node.IsRenaming = false;
        var name = node.RenameText.Trim();
        if (name.Length == 0 || name == node.Name)
            return;

        if (node.IsCollection)
            _library.RenameCollection(node.Collection.Id, name);
        else
            _library.RenameSource(node.Collection.Id, node.Id, name);
    }

    private void CancelRename()
    {
        foreach (var node in Nodes.SelectMany(n => n.SelfAndDescendants()).Where(n => n.IsRenaming))
            node.IsRenaming = false;
    }

    private void Keep()
    {
        if (_selectedNode is { CanKeep: true, Source: { } source })
            _library.KeepSessionCatalogue(source.Id);
    }

    private static void PostToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}

/// <summary>
/// Adds sources to the library interactively: picks a path (or online feed)
/// and confirms the target collection. Implemented by
/// <see cref="Services.LibraryImportCoordinator"/>.
/// </summary>
internal interface ILibraryImporter
{
    /// <summary>Picks a folder to scan for exchange sets and datasets.</summary>
    Task AddFolderAsync(Guid? targetCollectionId);

    /// <summary>Picks a zipped exchange set.</summary>
    Task AddExchangeSetZipAsync(Guid? targetCollectionId);

    /// <summary>
    /// Opens the directory of known online catalogues (issue #670), then the
    /// chosen catalogue's scope picker.
    /// </summary>
    Task AddOnlineCatalogueAsync(Guid? targetCollectionId);

    /// <summary>
    /// Asks for the URL of a feed served by <c>s100 feed serve</c> on another
    /// computer, checks it, then opens its product picker (issue #680).
    /// </summary>
    Task AddSharedFeedAsync(Guid? targetCollectionId);

    /// <summary>Opens the scope picker for a known online catalogue directly.</summary>
    Task AddKnownCatalogueAsync(EncDotNet.S100.Collections.KnownSources.KnownCatalogueSource source, Guid? targetCollectionId);

    /// <summary>Picks an S-128 catalogue file.</summary>
    Task AddS128CatalogueAsync(Guid? targetCollectionId);

    /// <summary>Picks a collection manifest (<c>*.s100collection.json</c>) and opens its group picker.</summary>
    Task AddCollectionManifestAsync(Guid? targetCollectionId);

    /// <summary>Reopens a collection manifest's group picker to change an existing source's selection.</summary>
    Task ChooseManifestGroupsAsync(Guid collectionId, LocalManifestSource source);

    /// <summary>
    /// Opens the S-111 surface-currents catalogue at its models step with the
    /// models covering <paramref name="area"/> ticked (#685, handoff B8).
    /// </summary>
    Task AddCurrentsForAreaAsync(GeoBounds area, Guid? targetCollectionId) => Task.CompletedTask;

    /// <summary>Adds a known path (for example a dropped folder), confirming the target collection.</summary>
    Task AddPathAsync(string path, Guid? targetCollectionId);

    /// <summary>
    /// True when <paramref name="path"/> is already a library source or lies
    /// inside a folder source.
    /// </summary>
    bool IsInLibrary(string path);
}
