using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Library panel's tree (#655, #1396): the user's collections and their
/// sources, the node actions (add, rename, remove, refresh, keep downloaded,
/// show on map) and each node's status line. The panel lists the selected
/// node's datasets.
/// </summary>
internal sealed class LibraryTreeViewModel : ViewModelBase, IDisposable
{
    private readonly CollectionLibrary _library;
    private readonly ILibraryImporter _importer;
    private readonly LibraryNodeStatus _status;
    private readonly Func<TimeFormat>? _runFormat;
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private LibraryNodeViewModel? _selectedNode;
    private bool _hasSynced;
    private bool _refreshPosted;
    private readonly Services.Secom.SecomIdentityDirectory? _identities;
    private readonly Action<Keys.KeysTab>? _openKeys;

    /// <summary>Creates the tree and fills it from <paramref name="library"/>.</summary>
    /// <param name="library">The Library the tree shows.</param>
    /// <param name="importer">Adds sources interactively.</param>
    /// <param name="status">What each node's status line says.</param>
    /// <param name="timeFormat">The Local/UTC setting; <see langword="null"/> shows UTC.</param>
    /// <param name="dispatch">Runs a change on the UI thread (the app passes <see cref="Services.UiThread.RunOrPost"/>).</param>
    /// <param name="time">The clock SECOM identities' expiry is counted by.</param>
    /// <param name="identities">
    /// The identities SECOM sources choose from (#883): with them, SECOM rows
    /// show their identity and the details pane shows a selected source's
    /// Connection. <see langword="null"/> where the host has no keys.
    /// </param>
    /// <param name="openKeys">Opens Settings → Keys &amp; certificates on a tab; <see langword="null"/> where the host has no Settings.</param>
    public LibraryTreeViewModel(
        CollectionLibrary library,
        ILibraryImporter importer,
        LibraryNodeStatus status,
        Services.ITimeFormatProvider? timeFormat,
        Action<Action> dispatch,
        TimeProvider time,
        Services.Secom.SecomIdentityDirectory? identities,
        Action<Keys.KeysTab>? openKeys)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _library = library;
        _importer = importer;
        _status = status;
        _dispatch = dispatch;
        _runFormat = timeFormat is null ? null : () => timeFormat.Current;
        if (timeFormat is not null)
            timeFormat.TimeFormatChanged += _ => dispatch(OnTimeFormatChanged);
        status.Synced += OnSynced;

        AddFolderCommand = new AsyncRelayCommand(() => _importer.AddFolderAsync(TargetCollectionId));
        AddExchangeSetZipCommand = new AsyncRelayCommand(() => _importer.AddExchangeSetZipAsync(TargetCollectionId));
        AddOnlineCatalogueCommand = new AsyncRelayCommand(() => _importer.AddOnlineCatalogueAsync(TargetCollectionId));
        AddS128CatalogueCommand = new AsyncRelayCommand(() => _importer.AddS128CatalogueAsync(TargetCollectionId));
        AddSharedFeedCommand = new AsyncRelayCommand(() => _importer.AddSharedFeedAsync(TargetCollectionId));
        AddCollectionManifestCommand = new AsyncRelayCommand(() => _importer.AddCollectionManifestAsync(TargetCollectionId));
        ChooseGroupsCommand = new AsyncRelayCommand(ChooseGroupsAsync, () => _selectedNode?.CanChooseGroups == true);
        AddCurrentsForAreaCommand = new AsyncRelayCommand(AddCurrentsForAreaAsync, () => CanAddCurrentsForArea);
        OpenSecomFixCommand = new RelayCommand<LibraryNodeViewModel>(OpenSecomFix);
        RefreshCommand = new RelayCommand(Refresh);
        RefreshAllCommand = new RelayCommand(() => _library.Refresh());
        RenameCommand = new RelayCommand(BeginRename, () => _selectedNode?.CanRename == true);
        ToggleShowOnMapCommand = new RelayCommand(ToggleShowOnMap, () => CanShowOnMap);
        ToggleSyncCommand = new RelayCommand(ToggleSync, () => CanSyncSelected);
        CommitRenameCommand = new RelayCommand(CommitRename);
        CancelRenameCommand = new RelayCommand(CancelRename);
        RemoveCommand = new RelayCommand(Remove, () => _selectedNode?.CanRemove == true);
        KeepInLibraryCommand = new RelayCommand(Keep, () => _selectedNode?.CanKeep == true);

        _openKeys = openKeys;
        _identities = identities;
        if (identities is not null)
        {
            identities.Changed += (_, _) => _dispatch(UpdateSecomIdentities);
            SecomDetails = new Secom.SecomSourceDetailsViewModel(
                identities, (collectionId, source) => _library.UpdateSource(collectionId, source), tab => _openKeys?.Invoke(tab), _time);
        }

        _library.Changed += OnLibraryChanged;
        Sync();
    }

    /// <summary>Raised after <see cref="SelectedNode"/> changed.</summary>
    public event EventHandler? SelectedNodeChanged;

    /// <summary>Raised after <see cref="Nodes"/> were brought in line with the Library.</summary>
    public event EventHandler? Synced;

    /// <summary>Raised when <see cref="SecomDetails"/> shows another source, or none.</summary>
    public event EventHandler? SecomDetailsChanged;

    /// <summary>
    /// Raised when a SECOM row's "Choose identity…" selects its source, just
    /// before the identity picker opens: the details pane shows the source,
    /// not a dataset.
    /// </summary>
    public event EventHandler? SecomPickerOpening;

    /// <summary>Collection nodes, each with its source nodes as children.</summary>
    public ObservableCollection<LibraryNodeViewModel> Nodes { get; } = [];

    /// <summary>True when the library has no collection.</summary>
    public bool IsEmpty => Nodes.Count == 0;

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
                ((RelayCommand)ToggleShowOnMapCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanShowOnMap));
                OnPropertyChanged(nameof(SelectedShowsOnMap));
                ((RelayCommand)ToggleSyncCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanSyncSelected));
                OnPropertyChanged(nameof(SelectedSyncs));
                ((AsyncRelayCommand)ChooseGroupsCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)AddCurrentsForAreaCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanAddCurrentsForArea));
                ShowSecomDetails();
                SelectedNodeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>True when a node is selected.</summary>
    public bool HasSelectedNode => _selectedNode is not null;

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

    /// <summary>
    /// For a selected S-102 area node: opens the S-111 wizard at its models
    /// step with the models covering the area ticked (#685, handoff B8).
    /// </summary>
    public ICommand AddCurrentsForAreaCommand { get; }

    /// <summary>
    /// Follows a SECOM source's link (#880, #883 D2); the parameter is the node.
    /// "Choose identity…" and "Choose another identity…" select the source and
    /// open its picker (a row action never changes the setting by itself);
    /// the others open Settings → Keys &amp; certificates on the right tab.
    /// </summary>
    public ICommand OpenSecomFixCommand { get; }

    /// <summary>Re-indexes every source.</summary>
    public ICommand RefreshAllCommand { get; }

    /// <summary>Re-indexes the selected node (or everything when nothing is selected).</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Starts renaming the selected collection or source in place.</summary>
    public ICommand RenameCommand { get; }

    /// <summary>Applies the name typed while renaming.</summary>
    public ICommand CommitRenameCommand { get; }

    /// <summary>Leaves renaming without changing the name.</summary>
    public ICommand CancelRenameCommand { get; }

    /// <summary>Removes the selected collection or source from the library (never the data).</summary>
    public ICommand RemoveCommand { get; }

    /// <summary>Persists the selected session S-128 catalogue as a collection.</summary>
    public ICommand KeepInLibraryCommand { get; }

    /// <summary>Turns "Show on map" on or off for the selected source (or every source of the selected collection), #809.</summary>
    public ICommand ToggleShowOnMapCommand { get; }

    /// <summary>True when the selected node is a kept source or collection, whose datasets can be kept on the map.</summary>
    public bool CanShowOnMap => SelectedSources().Length > 0;

    /// <summary>True when every source of the selected node shows on the map (the menu item's tick).</summary>
    public bool SelectedShowsOnMap => SelectedSources() is { Length: > 0 } sources && sources.All(s => s.Definition.ShowOnMap);

    /// <summary>Turns "Keep downloaded" (sync) on or off for the selected online source, or a collection's online sources (#809).</summary>
    public ICommand ToggleSyncCommand { get; }

    /// <summary>True when the selected node has online sources that can be kept downloaded.</summary>
    public bool CanSyncSelected => SyncableSelectedSources().Length > 0;

    /// <summary>True when every online source of the selected node is kept downloaded (the menu item's tick).</summary>
    public bool SelectedSyncs => SyncableSelectedSources() is { Length: > 0 } sources && sources.All(s => s.Definition.Sync);

    /// <summary>True when the selected node is an area of a remote S-100 catalogue (S-102).</summary>
    public bool CanAddCurrentsForArea => _selectedNode is { IsGroup: true, Source.Definition: S100CatalogueFeedSource };

    /// <summary>A selected SECOM source's Connection details (#883 D3); null without SECOM identities.</summary>
    public Secom.SecomSourceDetailsViewModel? SecomDetails { get; }

    /// <summary>
    /// The persisted collection new sources are added to by default: the
    /// selected node's collection, unless it is the session collection.
    /// </summary>
    internal Guid? TargetCollectionId =>
        _selectedNode is { Collection.IsSession: false } node ? node.Collection.Id : null;

    /// <summary>A shared feed's last reachability, or <see langword="null"/> when it isn't known.</summary>
    /// <param name="source">The source.</param>
    internal Collections.Indexing.FeedHealth? HealthOf(CollectionSource source) => _status.FeedHealth?.Invoke(source);

    /// <summary>Re-reads every node's status line: counts and downloads.</summary>
    internal void RefreshStatus()
    {
        _status.ApplyCounts(Nodes);
        _status.ApplyDownloads(Nodes);
    }

    /// <summary>Re-reads the nodes' download lines only.</summary>
    internal void RefreshDownloadStatus() => _status.ApplyDownloads(Nodes);

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
                Nodes.Insert(i, LibraryNodeViewModel.ForCollection(collection, _status.FeedHealth, _runFormat));
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
        _status.ApplyCounts(Nodes);
        UpdateSecomIdentities();
        OnPropertyChanged(nameof(IsEmpty));
        ((RelayCommand)RemoveCommand).NotifyCanExecuteChanged();
        ((RelayCommand)KeepInLibraryCommand).NotifyCanExecuteChanged();
        ((RelayCommand)RenameCommand).NotifyCanExecuteChanged();
        Synced?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _library.Changed -= OnLibraryChanged;
        _status.Synced -= OnSynced;
    }

    private void OnSynced(object? sender, EventArgs e) => _dispatch(() => _status.ApplyCounts(Nodes));

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        // Coalesce bursts of indexing notifications into one UI update.
        lock (_gate)
        {
            if (_refreshPosted)
                return;
            _refreshPosted = true;
        }

        _dispatch(() =>
        {
            lock (_gate)
                _refreshPosted = false;
            Sync();
        });
    }

    private void OnTimeFormatChanged()
    {
        foreach (var node in Nodes)
            node.RefreshTimeFormat();
    }

    private void OpenSecomFix(LibraryNodeViewModel? node)
    {
        switch (node?.SecomFixKind)
        {
            case SecomFix.ChooseIdentity or SecomFix.ChooseAnotherIdentity when SecomDetails is not null:
                SelectedNode = node;
                SecomPickerOpening?.Invoke(this, EventArgs.Empty);
                SecomDetails.OpenPicker();
                break;
            case SecomFix.TrustedAuthorities:
                _openKeys?.Invoke(Keys.KeysTab.Authorities);
                break;
            case not null:
                _openKeys?.Invoke(Keys.KeysTab.Identities);
                break;
        }
    }

    /// <summary>Gives SECOM source nodes their identity line, and refreshes the details pane.</summary>
    private void UpdateSecomIdentities()
    {
        if (_identities is not { } identities)
            return;
        var hasIdentities = identities.HasIdentities;
        foreach (var node in Nodes.SelectMany(n => n.SelfAndDescendants()))
        {
            var only = node.IsGroup ? null : node.Source ?? (node.Collection.Sources is [var single] ? single : null);
            node.SecomIdentity = only?.Definition is SecomSource secom
                ? new SecomNodeIdentity(
                    identities.RowLabel(secom.Identity),
                    hasIdentities,
                    identities.Resolve(secom.Identity) is { } entry ? identities.EndsInDays(entry) : null)
                : null;
        }

        ShowSecomDetails();
    }

    /// <summary>Shows the selected node's SECOM source (if it is one) in the details pane.</summary>
    private void ShowSecomDetails()
    {
        if (SecomDetails is null)
            return;
        var node = _selectedNode;
        var source = node is null || node.IsGroup ? null : node.Source ?? (node.Collection.Sources is [var single] ? single : null);
        SecomDetails.Show(node?.Collection, source);
        SecomDetailsChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task AddCurrentsForAreaAsync()
    {
        if (!CanAddCurrentsForArea
            || GeoBounds.UnionAll(_selectedNode!.EnumerateItems().Select(p => p.Item.Bounds).OfType<GeoBounds>()) is not { } area)
        {
            return Task.CompletedTask;
        }

        return _importer.AddCurrentsForAreaAsync(area, targetCollectionId: null);
    }

    /// <summary>The sources a node-level action applies to: the source node's, or every source of a collection node.</summary>
    private LibrarySource[] SelectedSources() => _selectedNode switch
    {
        null or { IsGroup: true } => [],
        { Collection.IsSession: true } => [],
        { Source: { } source } => [source],
        { } node => [.. node.Collection.Sources],
    };

    private void ToggleShowOnMap()
    {
        var sources = SelectedSources();
        if (sources.Length == 0 || _selectedNode is not { } node)
            return;
        var show = !sources.All(s => s.Definition.ShowOnMap);
        foreach (var source in sources)
            _library.UpdateSource(node.Collection.Id, source.Definition with { ShowOnMap = show });
        OnPropertyChanged(nameof(SelectedShowsOnMap));
    }

    private LibrarySource[] SyncableSelectedSources() =>
        [.. SelectedSources().Where(s => _status.CanSync(s.Definition))];

    private void ToggleSync()
    {
        var sources = SyncableSelectedSources();
        if (sources.Length == 0 || _selectedNode is not { } node)
            return;
        var sync = !sources.All(s => s.Definition.Sync);
        foreach (var source in sources)
            _library.UpdateSource(node.Collection.Id, source.Definition with { Sync = sync });
        OnPropertyChanged(nameof(SelectedSyncs));
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
}
