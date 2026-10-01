using System.Collections.ObjectModel;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;
using FluentIcons.Common;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>How a tree node's status line is marked (the colour of its dot).</summary>
internal enum LibraryNodeStatusKind
{
    /// <summary>No status line.</summary>
    None,

    /// <summary>Work in progress: indexing, downloading.</summary>
    Busy,

    /// <summary>Something needs attention but still works (problems, an unreachable feed with a cached index).</summary>
    Warning,

    /// <summary>Not working (failed, access denied).</summary>
    Error,

    /// <summary>Healthy, worth saying (a reachable shared feed).</summary>
    Ok,

    /// <summary>A hint (the session collection).</summary>
    Info,
}

/// <summary>How many of a remote catalogue's datasets are on disk, and how many of those have a newer edition online.</summary>
/// <param name="Total">The datasets the source lists.</param>
/// <param name="Local">Those with a downloaded copy.</param>
/// <param name="Outdated">Those whose copy is an older edition than the catalogue's.</param>
internal sealed record LibraryCatalogueCounts(int Total, int Local, int Outdated);

/// <summary>
/// A node of the Library panel's tree: a collection, one of its sources, or
/// (under a collection-manifest source, or a remote S-100 catalogue's areas,
/// with two or more groups) one group.
/// Nodes are updated in place from new <see cref="LibraryService"/> snapshots
/// so tree expansion and selection survive background indexing.
/// </summary>
internal sealed class LibraryNodeViewModel : ViewModelBase
{
    private readonly Func<CollectionSource, FeedHealth?>? _health;
    private LibraryCollection _collection;
    private LibrarySource? _source;
    private SourceIndexGroup? _group;
    private string? _downloadStatus;
    private LibraryCatalogueCounts? _catalogueCounts;
    private bool _isExpanded;
    private bool _isRenaming;
    private string _renameText = string.Empty;

    private LibraryNodeViewModel(
        LibraryCollection collection, LibrarySource? source, Func<CollectionSource, FeedHealth?>? health, SourceIndexGroup? group = null)
    {
        _collection = collection;
        _source = source;
        _health = health;
        _group = group;
        if (source is not null && group is null)
            SyncGroups();
    }

    /// <summary>Creates a collection node with a child per source.</summary>
    /// <param name="collection">The collection.</param>
    /// <param name="health">How a shared feed's server last answered, for its status line.</param>
    public static LibraryNodeViewModel ForCollection(LibraryCollection collection, Func<CollectionSource, FeedHealth?>? health = null)
    {
        var node = new LibraryNodeViewModel(collection, null, health);
        foreach (var source in collection.Sources)
            node.Children.Add(new LibraryNodeViewModel(collection, source, health));
        return node;
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
    public string Name => _group?.Name ?? (_source is { } s ? SourceName(s) : _collection.Definition.Name);

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
        NoaaEncFeedSource or UsaceIencFeedSource or S100CatalogueFeedSource => Icon.Globe,
        S128CatalogueSource => Icon.BookOpen,
        ExchangeSetSource { Path: var p } when p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) => Icon.FolderZip,
        _ => Icon.Folder,
    };

    /// <summary>
    /// A small mono tag naming the kind of source: <c>DIR</c>, <c>ZIP</c>,
    /// <c>WEB</c> (an online catalogue), <c>AWS</c> (a catalogue on AWS Open
    /// Data, also on its area nodes), <c>LIST</c> (a community list),
    /// <c>FEED</c> (a shared feed) or <c>S-128</c>. A collection shows its
    /// sources' kind.
    /// </summary>
    public string KindTag => IsGroup
        ? _source?.Definition is S100CatalogueFeedSource catalogue ? KindOf(catalogue) : string.Empty
        : _source is { } s
        ? KindOf(s.Definition)
        : _collection.IsSession ? "S-128" : _collection.Sources.Select(x => KindOf(x.Definition)).FirstOrDefault() ?? "DIR";

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

    private IReadOnlyList<LibrarySource> Sources => _source is { } s ? [s] : _collection.Sources;

    private (string? Line, LibraryNodeStatusKind Kind) ComputeStatus()
    {
        var c = CultureInfo.CurrentCulture;
        var sources = Sources;
        if (_downloadStatus is { } downloading)
            return (downloading, LibraryNodeStatusKind.Busy);
        if (sources.Any(x => x.State == LibrarySourceState.Indexing))
            return (Strings.Library_Status_Indexing, LibraryNodeStatusKind.Busy);
        if (_group is { MissingPathCount: > 0 })
            return (Strings.Library_StatusLine_PathNotFound, LibraryNodeStatusKind.Warning);
        if (IsGroup)
            return (null, LibraryNodeStatusKind.None);
        if (sources.FirstOrDefault(x => x.State == LibrarySourceState.Failed && x.Index is null) is { } failed)
            return (string.Format(c, Strings.Library_StatusLine_FailedFormat, failed.Error ?? string.Empty), LibraryNodeStatusKind.Error);

        // A shared feed says whether its server is reachable (for a source, or a one-source collection).
        if (sources is [{ Definition: S100FeedSource feed }] && _health?.Invoke(feed) is { } health)
            return FeedStatus(feed, health, c);

        var problems = sources.Sum(x => x.Index?.Diagnostics.Count(d => d.Severity >= IndexDiagnosticSeverity.Warning) ?? 0);
        if (sources is [{ Definition: S100CatalogueFeedSource catalogue, Index: { } catalogueIndex }] && problems == 0)
            return CatalogueStatus(catalogueIndex, _health?.Invoke(catalogue), _catalogueCounts, c);
        if (problems > 0)
            return (string.Format(c, Strings.Library_StatusLine_ProblemsFormat, problems), LibraryNodeStatusKind.Warning);
        if (_collection.IsSession)
            return (Strings.Library_StatusLine_Session, LibraryNodeStatusKind.Info);
        return (null, LibraryNodeStatusKind.None);
    }

    private static (string, LibraryNodeStatusKind) FeedStatus(S100FeedSource feed, FeedHealth health, CultureInfo c)
    {
        if (health.IsReachable)
            return (string.Format(c, Strings.Library_StatusLine_ReachableFormat, feed.FeedUri.Authority), LibraryNodeStatusKind.Ok);
        if (System.Text.RegularExpressions.Regex.IsMatch(health.Failure!, @"\b(401|403|404)\b"))
            return (Strings.Library_StatusLine_AccessDenied, LibraryNodeStatusKind.Error);
        if (health.CopyFetchedAt is { } copied)
        {
            var since = (health.FailingSince ?? health.CheckedAt).ToLocalTime();
            return (string.Format(c, Strings.Library_StatusLine_UnreachableFormat, since, FormatAge(health.CheckedAt - copied)),
                LibraryNodeStatusKind.Warning);
        }

        return (string.Format(c, Strings.Library_StatusLine_UnreachableNoCopyFormat, health.Failure), LibraryNodeStatusKind.Error);
    }

    /// <summary>
    /// A remote S-100 catalogue's bookkeeping (handoff B1): "Catalogue
    /// 30.09.2026 · 140 updates · not for navigation" (amber), "… · 152 of 307
    /// local …" (green) or "… · nothing local …" (grey); or "Offline ·
    /// catalogue cached 30.09.2026 · 152 local" while its server cannot be reached.
    /// </summary>
    private static (string, LibraryNodeStatusKind) CatalogueStatus(
        SourceIndex index, FeedHealth? health, LibraryCatalogueCounts? counts, CultureInfo c)
    {
        var notForNavigation = index.Items.Count > 0
            && index.Items.All(i => i.Properties.GetValueOrDefault("notForNavigation") == "true");
        if (health is { IsReachable: false, CopyFetchedAt: { } cached })
        {
            var offline = string.Format(c, Strings.Library_StatusLine_CatalogueOfflineFormat, FormatWhen(cached.ToLocalTime(), c));
            if (counts is { Local: > 0 })
                offline += " · " + string.Format(c, Strings.Library_StatusLine_LocalFormat, counts.Local);
            return (offline, LibraryNodeStatusKind.Info);
        }

        var dated = (index.PublishedAt ?? index.IndexedAt).ToLocalTime();
        var parts = new List<string>(3) { string.Format(c, Strings.Library_StatusLine_CatalogueFormat, dated.ToString("d", c)) };
        var kind = LibraryNodeStatusKind.Info;
        switch (counts)
        {
            case { Outdated: > 0 }:
                parts.Add(string.Format(c, Strings.Library_StatusLine_UpdatesFormat, counts.Outdated));
                kind = LibraryNodeStatusKind.Warning;
                break;
            case { Local: 0 }:
                parts.Add(Strings.Library_StatusLine_NothingLocal);
                break;
            case { } some:
                parts.Add(some.Local == some.Total
                    ? string.Format(c, Strings.Library_StatusLine_AllLocalFormat, some.Total)
                    : string.Format(c, Strings.Library_StatusLine_SomeLocalFormat, some.Local, some.Total));
                kind = LibraryNodeStatusKind.Ok;
                break;
        }

        if (notForNavigation)
            parts.Add(Strings.Library_StatusLine_NotForNavigation);
        return (string.Join(" · ", parts), kind);
    }

    /// <summary>The time when <paramref name="when"/> is today, else the date.</summary>
    private static string FormatWhen(DateTimeOffset when, CultureInfo c) =>
        when.Date == DateTime.Today ? when.ToString("t", c) : when.ToString("d", c);

    /// <summary>"12 min", "2 h", "3 days".</summary>
    internal static string FormatAge(TimeSpan age) => age switch
    {
        { TotalHours: < 1 } => string.Format(CultureInfo.CurrentCulture, Strings.Library_AgeMinutesFormat, Math.Max(1, (int)age.TotalMinutes)),
        { TotalHours: < 48 } => string.Format(CultureInfo.CurrentCulture, Strings.Library_AgeHoursFormat, (int)age.TotalHours),
        _ => string.Format(CultureInfo.CurrentCulture, Strings.Library_AgeDaysFormat, (int)age.TotalDays),
    };

    private static string KindOf(CollectionSource source) => source switch
    {
        ExchangeSetSource { Path: var p } when p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) => "ZIP",
        NoaaEncFeedSource or UsaceIencFeedSource => "WEB",
        S100CatalogueFeedSource { CatalogUri.Host: var host }
            when host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase) => "AWS",
        S100CatalogueFeedSource => "WEB",
        ChartCatalogsFeedSource => "LIST",
        S100FeedSource => "FEED",
        S128CatalogueSource => "S-128",
        LocalManifestSource => "JSON",
        _ => "DIR",
    };

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
                lines.Add(SourceLocation(source.Definition));
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
                Children.Insert(i, new LibraryNodeViewModel(collection, source, _health));
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
                Children.Insert(i, new LibraryNodeViewModel(_collection, _source, _health, group));
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
    private static readonly HashSet<string> LegacyGenericNames =
        new(["All ENCs", "All rivers", "All downloads", "All products"], StringComparer.Ordinal);

    /// <summary>
    /// The source's name: the user's (or the dialog's) display name, unless it
    /// only repeats something generic — a legacy "All …" name, the collection's
    /// name or the catalogue's — in which case it is derived: a community list
    /// holding a single package is named by that package's description.
    /// </summary>
    private string SourceName(LibrarySource source)
    {
        var definition = source.Definition;
        var catalogue = CatalogueUri(definition) is { } uri
            ? EncDotNet.S100.Collections.KnownSources.KnownCatalogueSources.All.FirstOrDefault(k => k.CatalogUri == uri)?.Name
            : null;
        var generic = definition.DisplayName is { } name
            && CatalogueUri(definition) is not null
            && (LegacyGenericNames.Contains(name) || name == _collection.Definition.Name || name == catalogue);
        if (definition.DisplayName is { } display && !generic)
            return display;

        if (definition is ChartCatalogsFeedSource && SinglePackageTitle(source.Index) is { } package)
            return package;
        return catalogue ?? DescribeSource(definition with { DisplayName = null });
    }

    /// <summary>The one package a community list's index holds, by description, or <see langword="null"/>.</summary>
    private static string? SinglePackageTitle(SourceIndex? index)
    {
        if (index is null)
            return null;
        var titles = index.Items
            .Select(i => i.Properties.GetValueOrDefault("packageTitle") ?? (i.Properties.ContainsKey("package") ? i.Title : null))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return titles.Length == 1 ? PackageTitles.Clean(titles[0]) : null;
    }

    private static Uri? CatalogueUri(CollectionSource source) => source switch
    {
        NoaaEncFeedSource n => n.CatalogUri,
        UsaceIencFeedSource u => u.CatalogUri,
        ChartCatalogsFeedSource c => c.CatalogUri,
        S100CatalogueFeedSource r => r.CatalogUri,
        _ => null,
    };

    private static string DescribeSource(CollectionSource source) => source.DisplayName ?? source switch
    {
        LocalFolderSource f => LeafName(f.Path),
        ExchangeSetSource e => LeafName(e.Path),
        S128CatalogueSource c => LeafName(c.Path),
        LocalManifestSource m => ManifestName(m.Path),
        NoaaEncFeedSource => Strings.Library_NoaaFeed,
        UsaceIencFeedSource => Strings.Library_UsaceFeed,
        ChartCatalogsFeedSource c => c.CatalogUri.Host,
        S100FeedSource f => f.FeedUri.Host,
        S100CatalogueFeedSource r => r.CatalogUri.Host,
        _ => source.GetType().Name,
    };

    private static string SourceLocation(CollectionSource source) => source switch
    {
        LocalFolderSource f => f.Path,
        ExchangeSetSource e => e.Path,
        S128CatalogueSource c => c.Path,
        LocalManifestSource m => m.Path,
        NoaaEncFeedSource n => n.CatalogUri.AbsoluteUri,
        UsaceIencFeedSource u => u.CatalogUri.AbsoluteUri,
        ChartCatalogsFeedSource c => c.CatalogUri.AbsoluteUri,
        S100FeedSource f => MaskToken(f.FeedUri),
        S100CatalogueFeedSource r => r.CatalogUri.AbsoluteUri,
        _ => string.Empty,
    };

    /// <summary>The online source's full URL (for "Copy URL"), or <see langword="null"/> for a local source.</summary>
    public Uri? SourceUrl => _source?.Definition switch
    {
        NoaaEncFeedSource n => n.CatalogUri,
        UsaceIencFeedSource u => u.CatalogUri,
        ChartCatalogsFeedSource c => c.CatalogUri,
        S100FeedSource f => f.FeedUri,
        S100CatalogueFeedSource r => r.CatalogUri,
        _ => null,
    };

    public bool HasSourceUrl => SourceUrl is not null;

    /// <summary>
    /// A shared feed's URL with its access token (the path before
    /// <c>feed.json</c>) masked to its last four characters:
    /// <c>http://bridge-pc:8100/••••3f9a/feed.json</c>. The token is never shown
    /// in full; "Copy URL" copies it.
    /// </summary>
    internal static string MaskToken(Uri feedUri)
    {
        var segments = feedUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return feedUri.AbsoluteUri;
        var masked = segments[..^1].Select(s => "••••" + (s.Length > 4 ? s[^4..] : string.Empty)).Append(segments[^1]);
        return $"{feedUri.Scheme}://{feedUri.Authority}/{string.Join('/', masked)}";
    }

    /// <summary>A manifest's file name without <c>.s100collection.json</c> (or <c>.json</c>).</summary>
    private static string ManifestName(string path)
    {
        var name = LeafName(path);
        return name.EndsWith(EncDotNet.S100.Collections.Manifests.CollectionManifest.FileSuffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^EncDotNet.S100.Collections.Manifests.CollectionManifest.FileSuffix.Length]
            : Path.GetFileNameWithoutExtension(name);
    }

    private static string LeafName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
