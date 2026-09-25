using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
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
    private readonly Action<Action> _dispatch;

    private LibraryNodeViewModel? _selectedNode;
    private IReadOnlyList<SourceIndex?>? _itemsBasis;
    private IReadOnlyList<LibraryItemViewModel> _allItems = [];
    private IReadOnlyList<LibraryItemViewModel> _items = [];
    private LibraryItemViewModel? _selectedItem;
    private string _filterText = string.Empty;
    private bool _showCancelled;
    private bool _refreshPosted;

    public LibraryPanelViewModel(LibraryService library, ILibraryImporter importer)
        : this(library, importer, PostToUiThread)
    {
    }

    internal LibraryPanelViewModel(LibraryService library, ILibraryImporter importer, Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(dispatch);
        _library = library;
        _importer = importer;
        _dispatch = dispatch;

        AddFolderCommand = new AsyncRelayCommand(() => _importer.AddFolderAsync(TargetCollectionId));
        AddExchangeSetZipCommand = new AsyncRelayCommand(() => _importer.AddExchangeSetZipAsync(TargetCollectionId));
        AddNoaaFeedCommand = new AsyncRelayCommand(() => _importer.AddNoaaFeedAsync(TargetCollectionId));
        AddS128CatalogueCommand = new AsyncRelayCommand(() => _importer.AddS128CatalogueAsync(TargetCollectionId));
        RefreshCommand = new RelayCommand(Refresh);
        RemoveCommand = new RelayCommand(Remove, () => _selectedNode?.CanRemove == true);
        KeepInLibraryCommand = new RelayCommand(Keep, () => _selectedNode?.CanKeep == true);

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
                OnPropertyChanged(nameof(HasSelectedItem));
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

    /// <summary>"N datasets" or "M of N datasets" for the list header.</summary>
    public string ItemsSummary =>
        _items.Count == _allItems.Count
            ? string.Format(CultureInfo.CurrentCulture, Strings.Library_ItemCountFormat, _allItems.Count)
            : string.Format(CultureInfo.CurrentCulture, Strings.Library_FilteredItemCountFormat, _items.Count, _allItems.Count);

    public ICommand AddFolderCommand { get; }

    public ICommand AddExchangeSetZipCommand { get; }

    public ICommand AddNoaaFeedCommand { get; }

    public ICommand AddS128CatalogueCommand { get; }

    /// <summary>Re-indexes the selected node (or everything when nothing is selected).</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Removes the selected collection or source from the library (never the data).</summary>
    public ICommand RemoveCommand { get; }

    /// <summary>Persists the selected session S-128 catalogue as a collection.</summary>
    public ICommand KeepInLibraryCommand { get; }

    public void Dispose() => _library.Changed -= OnLibraryChanged;

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

    private void RebuildItems(bool force)
    {
        var node = _selectedNode;
        var basis = node?.ItemIndexes;
        if (!force && basis is not null && _itemsBasis is not null
            && basis.Count == _itemsBasis.Count && basis.Zip(_itemsBasis).All(p => ReferenceEquals(p.First, p.Second)))
        {
            return;
        }

        _itemsBasis = basis;
        var selectedKey = _selectedItem is { } sel ? (sel.Source.Id, sel.Item.Key) : default;
        _allItems = node is null
            ? []
            : node.EnumerateItems().Select(p => new LibraryItemViewModel(p.Item, p.Source)).ToArray();
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

    /// <summary>Chooses a scope of the NOAA ENC feed.</summary>
    Task AddNoaaFeedAsync(Guid? targetCollectionId);

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
