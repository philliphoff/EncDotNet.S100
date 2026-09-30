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

    private LibraryNodeViewModel? _selectedNode;
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

    public LibraryPanelViewModel(
        LibraryService library,
        ILibraryImporter importer,
        ILibraryLoader loader,
        ILibraryDownloader downloader,
        Func<CollectionSource, EncDotNet.S100.Collections.Indexing.FeedHealth?>? feedHealth = null,
        Services.Notifications.INotificationService? notifications = null)
        : this(library, importer, loader, downloader, PostToUiThread, feedHealth, notifications)
    {
    }

    internal LibraryPanelViewModel(
        LibraryService library,
        ILibraryImporter importer,
        ILibraryLoader loader,
        ILibraryDownloader downloader,
        Action<Action> dispatch,
        Func<CollectionSource, EncDotNet.S100.Collections.Indexing.FeedHealth?>? feedHealth = null,
        Services.Notifications.INotificationService? notifications = null)
    {
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
        LoadCommand = new AsyncRelayCommand(LoadSelectedAsync, () => _selectedItem?.CanLoad == true);
        LoadAsYouPanCommand = new AsyncRelayCommand(LoadListedAsYouPanAsync, () => _items.Count > 0);
        DownloadCommand = new AsyncRelayCommand(DownloadSelectedAsync, () => _selectedItem?.CanDownload == true);
        DownloadOnlyCommand = new AsyncRelayCommand(() => DownloadSelectedAsync(load: false), () => _selectedItem?.CanDownload == true);
        LoadOrDownloadCommand = new AsyncRelayCommand(
            () => _selectedItem?.CanLoadAfterDownload == true ? DownloadSelectedAsync(load: true) : LoadSelectedAsync(),
            () => _selectedItem is { } item && (item.CanLoad || item.CanLoadAfterDownload));
        DownloadListedCommand = new AsyncRelayCommand(DownloadListedAsync, () => DownloadableCount > 0);
        CancelDownloadsCommand = new RelayCommand(() => _downloader.CancelAll(), () => IsBulkDownloading);
        _loader.Changed += OnLoaderChanged;
        _downloader.Changed += OnLoaderChanged;
        _downloader.ProgressChanged += OnDownloadProgress;

        _library.Changed += OnLibraryChanged;
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
                OnPropertyChanged(nameof(HasSelectedItem));
                OnPropertyChanged(nameof(LocationHitIndex));
                OnPropertyChanged(nameof(LocationPositionText));
                ((RelayCommand)ZoomToCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)LoadCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DownloadCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DownloadOnlyCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)LoadOrDownloadCommand).NotifyCanExecuteChanged();
            }
        }
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
                ApplyFilter();
        }
    }

    /// <summary>Whether cancelled datasets are listed.</summary>
    public bool ShowCancelled
    {
        get => _showCancelled;
        set
        {
            if (SetProperty(ref _showCancelled, value))
                ApplyFilter();
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
                ApplyFilter();
            }
        }
    }

    public bool IsStateAll { get => _stateFilter == LibraryStateFilter.All; set { if (value) StateFilter = LibraryStateFilter.All; } }

    public bool IsStateLocal { get => _stateFilter == LibraryStateFilter.Local; set { if (value) StateFilter = LibraryStateFilter.Local; } }

    public bool IsStateOnline { get => _stateFilter == LibraryStateFilter.Online; set { if (value) StateFilter = LibraryStateFilter.Online; } }

    public bool IsStateUpdates { get => _stateFilter == LibraryStateFilter.Updates; set { if (value) StateFilter = LibraryStateFilter.Updates; } }

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
    /// The map location the list is filtered to (set by tapping the map), or
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

            var downloadable = Downloadable().ToArray();
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

            if (DownloadableCount == 0)
                return Strings.Library_BulkNothing;

            var scope = IsFiltered ? Strings.Library_BulkScopeFiltered : Strings.Library_BulkScopeAll;
            var local = _items.Count(i => i.PrimaryAvailability == LibraryPrimaryAvailability.Local && !_downloader.IsOutdated(i.Item));
            return local == 0 ? scope : string.Format(CultureInfo.CurrentCulture, Strings.Library_BulkAlreadyLocalFormat, scope, local);
        }
    }

    /// <summary>True when the list is narrowed by text, state or a map tap.</summary>
    private bool IsFiltered => _filterText.Trim().Length > 0 || _stateFilter != LibraryStateFilter.All || _location is not null;

    /// <summary>How many listed datasets can be downloaded.</summary>
    public int DownloadableCount => Downloadable().Count();

    private IEnumerable<LibraryItemViewModel> Downloadable() =>
        _items.Where(i => _downloader.CanDownload(i.Item) && (i.EffectiveItem.Location is RemoteItemLocation || _downloader.IsOutdated(i.Item)));

    /// <summary>True when some listed dataset can be downloaded (and no bulk download is running).</summary>
    public bool HasDownloadable => !IsBulkDownloading && DownloadableCount > 0;

    /// <summary>"Download 1,193 (203 MB)" for the bulk download button.</summary>
    public string DownloadListedText
    {
        get
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.Library_DownloadListedFormat, DownloadableCount);
        }
    }

    /// <summary>
    /// Handles a tap on the map: lists every library dataset whose coverage
    /// contains <paramref name="position"/> and selects the most detailed one.
    /// Tapping the same spot again selects the next overlapping dataset.
    /// Returns false (changing nothing) when no dataset covers the position.
    /// </summary>
    public bool SelectAt(GeoPosition position)
    {
        var hits = HitsAt(position);
        if (hits.Count == 0)
            return false;

        var sameSpot = _location is { } previous && Near(previous, position);
        var previousItem = _selectedItem;
        SetLocation(position, hits);

        if (sameSpot && previousItem is not null)
        {
            SelectedItem = _items.FirstOrDefault(i => SameItem(i, previousItem));
            NextAtLocation();
        }
        else
        {
            SelectedItem = _items.Count > 0 ? _items[0] : null;
        }

        return true;
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
        ((RelayCommand)CancelDownloadsCommand).NotifyCanExecuteChanged();
        UpdateNodeDownloadStatus();
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
        _loader.LoadAsync(_items.Where(i => !i.IsGroupHeader).Select(i => i.EffectiveItem).ToArray(), defer: true);

    private Task DownloadSelectedAsync() => DownloadSelectedAsync(load: true);

    private Task DownloadSelectedAsync(bool load) =>
        _selectedItem is { } item ? DownloadItemAsync(item, load) : Task.CompletedTask;

    /// <summary>Downloads one row (the details' Download, "Load after download", or a retry), then optionally loads it.</summary>
    private async Task DownloadItemAsync(LibraryItemViewModel item, bool load)
    {
        TrackDownloads([item]);
        var result = await _downloader.DownloadAsync([item.Item]).ConfigureAwait(true);
        if (result.Downloaded == 0)
            return;

        // A package's cells (and their coverage) appear once its source re-indexes.
        if (ReindexPackageSources([item]))
        {
            await AnnouncePackagesAsync([item]).ConfigureAwait(true);
            return;
        }

        item.RefreshAvailability();
        if (load)
            await _loader.LoadAsync([item.EffectiveItem], defer: false).ConfigureAwait(true);
    }

    private async Task DownloadListedAsync()
    {
        var items = _items
            .Where(i => i.EffectiveItem.Location is RemoteItemLocation || _downloader.IsOutdated(i.Item))
            .ToArray();
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

        for (var i = Nodes.Count - 1; i >= 0; i--)
        {
            if (!snapshot.Any(c => c.Id == Nodes[i].Id))
            {
                if (ReferenceEquals(Nodes[i], _selectedNode) || Nodes[i].Children.Contains(_selectedNode!))
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

            var selectedWasChild = _selectedNode is { IsCollection: false } && existing.Children.Contains(_selectedNode);
            existing.Update(collection);
            if (selectedWasChild && !existing.Children.Contains(_selectedNode!))
                SelectedNode = existing;

            var at = Nodes.IndexOf(existing);
            if (at != i)
                Nodes.Move(at, i);
        }

        SelectedNode ??= Nodes.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
        ((RelayCommand)RemoveCommand).NotifyCanExecuteChanged();
        ((RelayCommand)KeepInLibraryCommand).NotifyCanExecuteChanged();
        ((RelayCommand)RenameCommand).NotifyCanExecuteChanged();
        RebuildItems(force: false);
    }

    private void SetLocation(GeoPosition? position, IReadOnlyList<LibraryItemViewModel>? hits = null)
    {
        _location = position;
        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(HasLocation));
        OnPropertyChanged(nameof(LocationSummary));
        ((RelayCommand)NextAtLocationCommand).NotifyCanExecuteChanged();

        var selected = _selectedItem;
        _itemsBasis = null;
        _allItems = position is { } p ? hits ?? HitsAt(p) : BuildNodeItems(_selectedNode);
        ApplyFilter();
        SelectedItem = selected is null ? null : _items.FirstOrDefault(i => SameItem(i, selected));
    }

    /// <summary>
    /// Every library dataset covering <paramref name="position"/>, most
    /// detailed (highest usage band, then smallest extent) first.
    /// </summary>
    private List<LibraryItemViewModel> HitsAt(GeoPosition position) =>
        _library.Collections
            .SelectMany(c => c.Sources)
            .SelectMany(s => (s.Index?.Items ?? []).Select(i => (Item: i, Source: s)))
            .Where(p => CoverageGeometry.Contains(p.Item, position))
            .OrderByDescending(p => p.Item.UsageBand ?? 0)
            .ThenBy(p => CoverageGeometry.Area(p.Item))
            .Select(p => new LibraryItemViewModel(p.Item, p.Source, _loader.StateOf, _downloader, CollectionNameOf(p.Source.Id), RetryDownloadAsync))
            .ToList();

    private string? CollectionNameOf(Guid sourceId) =>
        _library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == sourceId))?.Definition.Name;

    private IReadOnlyList<LibraryItemViewModel> BuildNodeItems(LibraryNodeViewModel? node) =>
        node is null
            ? []
            : node.EnumerateItems()
                .Select(p => new LibraryItemViewModel(p.Item, p.Source, _loader.StateOf, _downloader, node.Collection.Definition.Name, RetryDownloadAsync))
                .ToArray();

    private Task RetryDownloadAsync(LibraryItemViewModel item) => DownloadItemAsync(item, load: false);

    private static bool SameItem(LibraryItemViewModel a, LibraryItemViewModel b) =>
        a.Source.Id == b.Source.Id && a.Item.Key == b.Item.Key;

    private static bool Near(GeoPosition a, GeoPosition b) =>
        Math.Abs(a.Latitude - b.Latitude) < 1e-4 && Math.Abs(a.Longitude - b.Longitude) < 1e-4;

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
        _textFiltered = _allItems
            .Where(i => _showCancelled || !i.IsCancelled)
            .Where(i => filter.Length == 0 || i.Matches(filter))
            .ToArray();
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

    /// <summary>The unpacked package a community-list dataset came from, if any.</summary>
    private static (Guid, string)? PackageOf(LibraryItemViewModel item) =>
        item.Source.Definition is ChartCatalogsFeedSource
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

    private static bool InState(LibraryItemViewModel item, LibraryStateFilter state) => state switch
    {
        LibraryStateFilter.Local => item.Availability is LibraryAvailability.Local or LibraryAvailability.Loaded or LibraryAvailability.Deferred,
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
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(LocalCount));
        OnPropertyChanged(nameof(OnlineCount));
        OnPropertyChanged(nameof(UpdatesCount));
    }

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
        foreach (var other in Nodes.SelectMany(n => n.Children.Prepend(n)).Where(n => n.IsRenaming))
            other.IsRenaming = false;
        node.RenameText = node.Name;
        node.IsRenaming = true;
    }

    private void CommitRename()
    {
        var node = Nodes.SelectMany(n => n.Children.Prepend(n)).FirstOrDefault(n => n.IsRenaming);
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
        foreach (var node in Nodes.SelectMany(n => n.Children.Prepend(n)).Where(n => n.IsRenaming))
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

    /// <summary>Adds a known path (for example a dropped folder), confirming the target collection.</summary>
    Task AddPathAsync(string path, Guid? targetCollectionId);

    /// <summary>
    /// True when <paramref name="path"/> is already a library source or lies
    /// inside a folder source.
    /// </summary>
    bool IsInLibrary(string path);
}
