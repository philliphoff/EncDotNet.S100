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
    private bool _availabilityRefreshPosted;
    private readonly Action<Action> _dispatch;

    private LibraryNodeViewModel? _selectedNode;
    private IReadOnlyList<SourceIndex?>? _itemsBasis;
    private IReadOnlyList<LibraryItemViewModel> _allItems = [];
    private IReadOnlyList<LibraryItemViewModel> _items = [];
    private LibraryItemViewModel? _selectedItem;
    private string _filterText = string.Empty;
    private bool _showCancelled;
    private bool _refreshPosted;
    private bool _showCoverage = true;
    private GeoPosition? _location;

    public LibraryPanelViewModel(
        LibraryService library, ILibraryImporter importer, ILibraryLoader loader, ILibraryDownloader downloader)
        : this(library, importer, loader, downloader, PostToUiThread)
    {
    }

    internal LibraryPanelViewModel(
        LibraryService library,
        ILibraryImporter importer,
        ILibraryLoader loader,
        ILibraryDownloader downloader,
        Action<Action> dispatch)
    {
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
        RefreshCommand = new RelayCommand(Refresh);
        RemoveCommand = new RelayCommand(Remove, () => _selectedNode?.CanRemove == true);
        KeepInLibraryCommand = new RelayCommand(Keep, () => _selectedNode?.CanKeep == true);
        ZoomToCommand = new RelayCommand(ZoomToSelected, () => _selectedItem?.HasBounds == true);
        ClearLocationCommand = new RelayCommand(() => SetLocation(null));
        LoadCommand = new AsyncRelayCommand(LoadSelectedAsync, () => _selectedItem?.CanLoad == true);
        LoadAsYouPanCommand = new AsyncRelayCommand(LoadListedAsYouPanAsync, () => _items.Count > 0);
        DownloadCommand = new AsyncRelayCommand(DownloadSelectedAsync, () => _selectedItem?.CanDownload == true);
        DownloadListedCommand = new AsyncRelayCommand(DownloadListedAsync, () => DownloadableCount > 0);
        _loader.Changed += OnLoaderChanged;
        _downloader.Changed += OnLoaderChanged;

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
                ((RelayCommand)ZoomToCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)LoadCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DownloadCommand).NotifyCanExecuteChanged();
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

    /// <summary>"N datasets" or "M of N datasets" for the list header.</summary>
    public string ItemsSummary =>
        _items.Count == _allItems.Count
            ? string.Format(CultureInfo.CurrentCulture, Strings.Library_ItemCountFormat, _allItems.Count)
            : string.Format(CultureInfo.CurrentCulture, Strings.Library_FilteredItemCountFormat, _items.Count, _allItems.Count);

    public ICommand AddFolderCommand { get; }

    public ICommand AddExchangeSetZipCommand { get; }

    /// <summary>Opens the directory of known online catalogues (NOAA, USACE, …).</summary>
    public ICommand AddOnlineCatalogueCommand { get; }

    public ICommand AddS128CatalogueCommand { get; }

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

    /// <summary>Loads the selected dataset now.</summary>
    public ICommand LoadCommand { get; }

    /// <summary>Registers every listed local dataset to load as it comes into view.</summary>
    public ICommand LoadAsYouPanCommand { get; }

    /// <summary>Downloads the selected online dataset, then loads it.</summary>
    public ICommand DownloadCommand { get; }

    /// <summary>Downloads every listed online (or outdated) dataset.</summary>
    public ICommand DownloadListedCommand { get; }

    /// <summary>How many listed datasets can be downloaded.</summary>
    public int DownloadableCount => _items.Count(i => _downloader.CanDownload(i.Item) && (i.EffectiveItem.Location is RemoteItemLocation || _downloader.IsOutdated(i.Item)));

    /// <summary>True when some listed dataset can be downloaded.</summary>
    public bool HasDownloadable => DownloadableCount > 0;

    /// <summary>"Download 1,193 (203 MB)" for the bulk download button.</summary>
    public string DownloadListedText
    {
        get
        {
            var downloadable = _items
                .Where(i => _downloader.CanDownload(i.Item) && (i.EffectiveItem.Location is RemoteItemLocation || _downloader.IsOutdated(i.Item)))
                .ToArray();
            var bytes = downloadable.Sum(i => (i.Item.Location as RemoteItemLocation)?.SizeBytes ?? 0);
            return string.Format(CultureInfo.CurrentCulture, Strings.Library_DownloadListedFormat,
                downloadable.Length, LibraryItemViewModel.FormatBytes(bytes));
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

        var index = sameSpot && previousItem is not null
            ? (_items.ToList().FindIndex(i => SameItem(i, previousItem)) + 1) % Math.Max(1, _items.Count)
            : 0;
        SelectedItem = _items.Count > 0 ? _items[index] : null;
        return true;
    }

    public void Dispose()
    {
        _library.Changed -= OnLibraryChanged;
        _loader.Changed -= OnLoaderChanged;
        _downloader.Changed -= OnLoaderChanged;
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
            foreach (var item in _items)
                item.RefreshAvailability();
            ((AsyncRelayCommand)LoadCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)DownloadCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)DownloadListedCommand).NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(DownloadListedText));
            OnPropertyChanged(nameof(HasDownloadable));
            // The coverage overlay styles by availability; let it redraw.
            OnPropertyChanged(nameof(Items));
        });
    }

    private Task LoadSelectedAsync() =>
        _selectedItem is { } item ? _loader.LoadAsync([item.EffectiveItem], defer: false) : Task.CompletedTask;

    private Task LoadListedAsYouPanAsync() =>
        _loader.LoadAsync(_items.Select(i => i.EffectiveItem).ToArray(), defer: true);

    private async Task DownloadSelectedAsync()
    {
        if (_selectedItem is not { } item)
            return;

        var result = await _downloader.DownloadAsync([item.Item]).ConfigureAwait(true);
        if (result.Downloaded == 0)
            return;

        // A package's cells (and their coverage) appear once its source re-indexes.
        if (ReindexPackageSources([item]))
            return;

        item.RefreshAvailability();
        await _loader.LoadAsync([item.EffectiveItem], defer: false).ConfigureAwait(true);
    }

    private async Task DownloadListedAsync()
    {
        var items = _items
            .Where(i => i.EffectiveItem.Location is RemoteItemLocation || _downloader.IsOutdated(i.Item))
            .ToArray();
        var result = await _downloader.DownloadAsync(items.Select(i => i.Item).ToArray()).ConfigureAwait(true);
        if (result.Downloaded > 0)
            ReindexPackageSources(items);
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
                Nodes.Insert(i, LibraryNodeViewModel.ForCollection(collection));
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
        RebuildItems(force: false);
    }

    private void SetLocation(GeoPosition? position, IReadOnlyList<LibraryItemViewModel>? hits = null)
    {
        _location = position;
        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(HasLocation));
        OnPropertyChanged(nameof(LocationSummary));

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
            .Select(p => new LibraryItemViewModel(p.Item, p.Source, _loader.StateOf, _downloader))
            .ToList();

    private IReadOnlyList<LibraryItemViewModel> BuildNodeItems(LibraryNodeViewModel? node) =>
        node is null
            ? []
            : node.EnumerateItems().Select(p => new LibraryItemViewModel(p.Item, p.Source, _loader.StateOf, _downloader)).ToArray();

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
        Items = _allItems
            .Where(i => _showCancelled || !i.IsCancelled)
            .Where(i => filter.Length == 0 || i.Matches(filter))
            .ToArray();
        OnPropertyChanged(nameof(ItemsSummary));
        ((AsyncRelayCommand)LoadAsYouPanCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)DownloadListedCommand).NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DownloadListedText));
        OnPropertyChanged(nameof(HasDownloadable));

        if (_selectedItem is not null && !_items.Contains(_selectedItem))
            SelectedItem = null;
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
