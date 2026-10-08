using System.Collections.ObjectModel;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;
using FluentIcons.Common;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A node of the Library panel's tree: a collection, one of its sources, or
/// (under a collection-manifest source, or a remote S-100 catalogue's areas,
/// with two or more groups) one group.
/// Nodes are updated in place from new <see cref="CollectionLibrary"/> snapshots
/// so tree expansion and selection survive background indexing.
/// </summary>
internal sealed class LibraryNodeViewModel : ViewModelBase
{
    private readonly Func<CollectionSource, FeedHealth?>? _health;
    private readonly Func<TimeFormat>? _timeFormat;
    private LibraryCollection _collection;
    private LibrarySource? _source;
    private SourceIndexGroup? _group;
    private string? _downloadStatus;
    private LibraryCatalogueCounts? _catalogueCounts;
    private LibrarySyncStatus? _syncStatus;
    private LibraryForecastCounts? _forecastCounts;
    private bool _isExpanded;
    private bool _isRenaming;
    private string _renameText = string.Empty;

    private LibraryNodeViewModel(
        LibraryCollection collection, LibrarySource? source, Func<CollectionSource, FeedHealth?>? health, SourceIndexGroup? group = null,
        Func<TimeFormat>? timeFormat = null)
    {
        _collection = collection;
        _source = source;
        _health = health;
        _timeFormat = timeFormat;
        _group = group;
        if (source is not null && group is null)
            SyncGroups();
    }

    /// <summary>Creates a collection node with a child per source.</summary>
    /// <param name="collection">The collection.</param>
    /// <param name="health">How a shared feed's server last answered, for its status line.</param>
    /// <param name="timeFormat">The user's Local/UTC setting, for run times (#730); UTC when null.</param>
    public static LibraryNodeViewModel ForCollection(LibraryCollection collection, Func<CollectionSource, FeedHealth?>? health = null, Func<TimeFormat>? timeFormat = null)
    {
        var node = new LibraryNodeViewModel(collection, null, health, timeFormat: timeFormat);
        foreach (var source in collection.Sources)
            node.Children.Add(new LibraryNodeViewModel(collection, source, health, timeFormat: timeFormat));
        return node;
    }

    /// <summary>Re-reads the status line after the user's Local/UTC setting changed (#730).</summary>
    internal void RefreshTimeFormat()
    {
        RaiseStatus();
        foreach (var child in Children)
            child.RefreshTimeFormat();
    }

    /// <summary>The source nodes of a collection node; a manifest source's group nodes; otherwise empty.</summary>
    public ObservableCollection<LibraryNodeViewModel> Children { get; } = [];

    /// <summary>The collection this node is (or belongs to).</summary>
    public LibraryCollection Collection => _collection;

    /// <summary>The source this node is, or <see langword="null"/> for a collection node.</summary>
    public LibrarySource? Source => _source;

    /// <summary>True for a collection node.</summary>
    public bool IsCollection => _source is null;

    /// <summary>True for a collection-manifest group node.</summary>
    public bool IsGroup => _group is not null;

    /// <summary>The manifest group id of a group node; otherwise <see langword="null"/>.</summary>
    public string? GroupId => _group?.Id;

    /// <summary>This node and every node below it.</summary>
    public IEnumerable<LibraryNodeViewModel> SelfAndDescendants() =>
        Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);

    /// <summary>The node's stable id (collection or source id).</summary>
    public Guid Id => _source?.Id ?? _collection.Id;

    /// <summary>Whether the node's children are shown.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>True while the node's name is being edited in place.</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetProperty(ref _isRenaming, value);
    }

    /// <summary>The name being typed while <see cref="IsRenaming"/>.</summary>
    public string RenameText
    {
        get => _renameText;
        set => SetProperty(ref _renameText, value ?? string.Empty);
    }

    /// <summary>True when the node can be renamed (anything but the session collection and its catalogues).</summary>
    public bool CanRename => !_collection.IsSession && !IsGroup;

    /// <summary>True for a collection-manifest source, whose groups can be chosen again.</summary>
    public bool CanChooseGroups => !IsGroup && _source?.Definition is LocalManifestSource && !_collection.IsSession;

    /// <summary>The display name.</summary>
    public string Name => _group?.Name ?? (_source is { } s ? LibraryNodeText.SourceName(s, _collection) : _collection.Definition.Name);

    /// <summary>
    /// A muted second name: a manifest source named like its collection shows
    /// its file name, so the two rows don't read as duplicates.
    /// </summary>
    public string? SecondaryName =>
        !IsGroup && _source?.Definition is LocalManifestSource manifest && Name == _collection.Definition.Name
            ? Path.GetFileName(manifest.Path)
            : null;

    /// <summary>True when <see cref="SecondaryName"/> is set.</summary>
    public bool HasSecondaryName => SecondaryName is not null;

    /// <summary>The node's icon.</summary>
    public Icon Icon => IsGroup ? Icon.Folder : _source?.Definition switch
    {
        null => _collection.IsSession ? Icon.History : Icon.Library,
        LocalManifestSource => Icon.DocumentBulletList,
        NoaaEncFeedSource or UsaceIencFeedSource or S100CatalogueFeedSource or S100ForecastFeedSource or SecomSource => Icon.Globe,
        S128CatalogueSource => Icon.BookOpen,
        ExchangeSetSource { Path: var p } when p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) => Icon.FolderZip,
        _ => Icon.Folder,
    };

    /// <summary>
    /// A small mono tag naming the kind of source: <c>DIR</c>, <c>ZIP</c>,
    /// <c>WEB</c> (an online catalogue), <c>AWS</c> (a catalogue on AWS Open
    /// Data, also on its area nodes), <c>LIST</c> (a community list),
    /// <c>FEED</c> (a shared feed), <c>SECOM</c> (a SECOM service) or <c>S-128</c>. A collection shows its
    /// sources' kind.
    /// </summary>
    public string KindTag => IsGroup
        ? _source?.Definition is S100CatalogueFeedSource catalogue ? LibraryNodeText.KindOf(catalogue) : string.Empty
        : _source is { } s
        ? LibraryNodeText.KindOf(s.Definition)
        : LibraryNodeText.KindOf(_collection);

    /// <summary>True when <see cref="KindTag"/> is shown (not for a group node).</summary>
    public bool HasKindTag => KindTag.Length > 0;

    /// <summary>The dataset count shown after the name, or "—" before there is an index.</summary>
    public string Status
    {
        get
        {
            if (IsGroup)
                return EnumerateItems().Count().ToString("N0", CultureInfo.CurrentCulture);

            var sources = Sources;
            return sources.All(x => x.Index is null)
                ? "—"
                : sources.Sum(x => x.Index?.Items.Count ?? 0).ToString("N0", CultureInfo.CurrentCulture);
        }
    }

    /// <summary>
    /// A second line shown only when something is off-normal: indexing,
    /// downloading, problems, a shared feed's reachability, or the session's
    /// pin hint. <see langword="null"/> keeps the node to one line.
    /// </summary>
    public string? StatusLine => ComputeStatus().Line;

    /// <summary>How <see cref="StatusLine"/> is marked.</summary>
    public LibraryNodeStatusKind StatusKind => ComputeStatus().Kind;

    public bool HasStatusLine => StatusLine is not null;

    public bool IsStatusBusy => StatusKind == LibraryNodeStatusKind.Busy;

    public bool IsStatusWarning => StatusKind == LibraryNodeStatusKind.Warning;

    public bool IsStatusError => StatusKind == LibraryNodeStatusKind.Error;

    public bool IsStatusOk => StatusKind == LibraryNodeStatusKind.Ok;

    public bool IsStatusInfo => StatusKind == LibraryNodeStatusKind.Info;

    /// <summary>
    /// "Downloading 2 of 5 · 4,3 MB left" while a download runs under this
    /// node, set by the panel; <see langword="null"/> otherwise.
    /// </summary>
    public string? DownloadStatus
    {
        get => _downloadStatus;
        set
        {
            if (SetProperty(ref _downloadStatus, value))
                RaiseStatus();
        }
    }

    /// <summary>
    /// A remote S-100 catalogue's local and update counts, set by the panel;
    /// <see langword="null"/> for other nodes (and group nodes).
    /// </summary>
    public LibraryCatalogueCounts? CatalogueCounts
    {
        get => _catalogueCounts;
        set
        {
            if (!Equals(_catalogueCounts, value))
            {
                _catalogueCounts = value;
                RaiseStatus();
            }
        }
    }

    /// <summary>
    /// A synced source's last sync (#807), set by the panel;
    /// <see langword="null"/> for other nodes, and before the first sync.
    /// </summary>
    public LibrarySyncStatus? SyncStatus
    {
        get => _syncStatus;
        set
        {
            if (!Equals(_syncStatus, value))
            {
                _syncStatus = value;
                RaiseStatus();
            }
        }
    }

    /// <summary>A forecast source's model counts, set by the panel; <see langword="null"/> for other nodes.</summary>
    public LibraryForecastCounts? ForecastCounts
    {
        get => _forecastCounts;
        set
        {
            if (!Equals(_forecastCounts, value))
            {
                _forecastCounts = value;
                RaiseStatus();
            }
        }
    }

    private IReadOnlyList<LibrarySource> Sources => _source is { } s ? [s] : _collection.Sources;

    private (string? Line, LibraryNodeStatusKind Kind) ComputeStatus() =>
        LibraryNodeText.Status(new LibraryNodeStatusInput(_collection, Sources)
        {
            Group = _group,
            Downloading = _downloadStatus,
            Health = _health,
            Sync = _syncStatus,
            CatalogueCounts = _catalogueCounts,
            ForecastCounts = _forecastCounts,
            TimeFormat = ForecastRunText.ToLibrary(_timeFormat?.Invoke() ?? TimeFormat.Utc),
        });

    private void RaiseStatus()
    {
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(HasStatusLine));
        OnPropertyChanged(nameof(IsStatusBusy));
        OnPropertyChanged(nameof(IsStatusWarning));
        OnPropertyChanged(nameof(IsStatusError));
        OnPropertyChanged(nameof(IsStatusOk));
        OnPropertyChanged(nameof(IsStatusInfo));
    }

    /// <summary>
    /// A multi-line description for a tooltip: the source path or URL, when it
    /// was indexed, and any problems.
    /// </summary>
    public string Tooltip
    {
        get
        {
            var sources = _source is { } s ? [s] : _collection.Sources;
            var lines = new List<string>();
            if (_group is { } group)
                lines.Add(group.Name);
            foreach (var source in sources)
            {
                lines.Add(LibraryNodeText.SourceLocation(source.Definition));
                if (source.Index is { } index)
                {
                    lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.Library_IndexedAtFormat,
                        index.Items.Count, index.IndexedAt.ToLocalTime()));
                    var problems = index.Diagnostics.Count(d => d.Severity >= IndexDiagnosticSeverity.Warning);
                    if (problems > 0)
                        lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.Library_ProblemsFormat, problems));
                }
                if (source.Error is { } error)
                    lines.Add(error);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>True when this node can be removed (anything but the session collection).</summary>
    public bool CanRemove => !_collection.IsSession && !IsGroup;

    /// <summary>True when this is a session catalogue that can be kept in the library.</summary>
    public bool CanKeep => _collection.IsSession && _source is not null && !IsGroup;

    /// <summary>
    /// Every item under this node with the source it came from, in source
    /// order.
    /// </summary>
    public IEnumerable<(CollectionItem Item, LibrarySource Source)> EnumerateItems()
    {
        var sources = _source is { } s ? [s] : _collection.Sources;
        var items = sources.SelectMany(src => (src.Index?.Items ?? []).Select(item => (item, src)));
        return _group is { } group
            ? items.Where(p => string.Equals(
                p.item.Properties.GetValueOrDefault(LocalManifestIndexer.GroupProperty), group.Id, StringComparison.OrdinalIgnoreCase))
            : items;
    }

    /// <summary>
    /// The indexes behind this node's items; a new list (by reference) means
    /// the items changed.
    /// </summary>
    public IReadOnlyList<SourceIndex?> ItemIndexes =>
        _source is { } s ? [s.Index] : _collection.Sources.Select(x => x.Index).ToArray();

    /// <summary>
    /// Updates this collection node (and its source children) from a newer
    /// snapshot of the same collection.
    /// </summary>
    public void Update(LibraryCollection collection)
    {
        _collection = collection;
        RaiseAll();

        // Sync children by source id, preserving existing child view models.
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (!collection.Sources.Any(s => s.Id == Children[i].Id))
                Children.RemoveAt(i);
        }

        for (var i = 0; i < collection.Sources.Count; i++)
        {
            var source = collection.Sources[i];
            var existing = Children.FirstOrDefault(c => c.Id == source.Id);
            if (existing is null)
            {
                Children.Insert(i, new LibraryNodeViewModel(collection, source, _health, timeFormat: _timeFormat));
            }
            else
            {
                existing._collection = collection;
                existing._source = source;
                existing.SyncGroups();
                existing.RaiseAll();
                var at = Children.IndexOf(existing);
                if (at != i)
                    Children.Move(at, i);
            }
        }
    }

    /// <summary>
    /// Brings a collection-manifest source's group children (or a remote S-100
    /// catalogue's area children) in line with its index (in index order, matched by group id so expansion and selection
    /// survive). Groups are shown only when there are two or more; none while
    /// the manifest cannot be read.
    /// </summary>
    private void SyncGroups()
    {
        var groups = _source is { Definition: LocalManifestSource or S100CatalogueFeedSource, Index.Groups: { Count: >= 2 } g } ? g : [];

        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (!groups.Any(g => string.Equals(g.Id, Children[i].GroupId, StringComparison.OrdinalIgnoreCase)))
                Children.RemoveAt(i);
        }

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var existing = Children.FirstOrDefault(c => string.Equals(c.GroupId, group.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Children.Insert(i, new LibraryNodeViewModel(_collection, _source, _health, group, _timeFormat));
                continue;
            }

            existing._collection = _collection;
            existing._source = _source;
            existing._group = group;
            existing.RaiseAll();
            var at = Children.IndexOf(existing);
            if (at != i)
                Children.Move(at, i);
        }
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(SecondaryName));
        OnPropertyChanged(nameof(HasSecondaryName));
        OnPropertyChanged(nameof(HasKindTag));
        OnPropertyChanged(nameof(CanChooseGroups));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Tooltip));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(KindTag));
        OnPropertyChanged(nameof(SourceUrl));
        OnPropertyChanged(nameof(HasSourceUrl));
        RaiseStatus();
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanKeep));
        OnPropertyChanged(nameof(CanRename));
    }

    /// <summary>
    /// Display names older builds gave every unscoped online source. They say
    /// nothing about the source, so the source is named as if it had none.
    /// </summary>
    /// <summary>The online source's full URL (for "Copy URL"), or <see langword="null"/> for a local source.</summary>
    public Uri? SourceUrl => _source is { } source ? LibraryNodeText.SourceUrl(source.Definition) : null;

    public bool HasSourceUrl => SourceUrl is not null;

    /// <summary>A shared feed's URL with its access token masked (see <see cref="LibraryTextFormat.MaskToken"/>).</summary>
    internal static string MaskToken(Uri feedUri) => LibraryTextFormat.MaskToken(feedUri);
}
