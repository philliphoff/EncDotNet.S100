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

/// <summary>
/// A node of the Library panel's tree: a collection, or one of its sources.
/// Nodes are updated in place from new <see cref="LibraryService"/> snapshots
/// so tree expansion and selection survive background indexing.
/// </summary>
internal sealed class LibraryNodeViewModel : ViewModelBase
{
    private readonly Func<CollectionSource, FeedHealth?>? _health;
    private LibraryCollection _collection;
    private LibrarySource? _source;
    private string? _downloadStatus;
    private bool _isExpanded;
    private bool _isRenaming;
    private string _renameText = string.Empty;

    private LibraryNodeViewModel(LibraryCollection collection, LibrarySource? source, Func<CollectionSource, FeedHealth?>? health)
    {
        _collection = collection;
        _source = source;
        _health = health;
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

    /// <summary>The source nodes of a collection node; empty for a source node.</summary>
    public ObservableCollection<LibraryNodeViewModel> Children { get; } = [];

    /// <summary>The collection this node is (or belongs to).</summary>
    public LibraryCollection Collection => _collection;

    /// <summary>The source this node is, or <see langword="null"/> for a collection node.</summary>
    public LibrarySource? Source => _source;

    /// <summary>True for a collection node.</summary>
    public bool IsCollection => _source is null;

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
    public bool CanRename => !_collection.IsSession;

    /// <summary>The display name.</summary>
    public string Name => _source is { } s ? DescribeSource(s.Definition) : _collection.Definition.Name;

    /// <summary>The node's icon.</summary>
    public Icon Icon => _source?.Definition switch
    {
        null => _collection.IsSession ? Icon.History : Icon.Library,
        NoaaEncFeedSource or UsaceIencFeedSource => Icon.Globe,
        S128CatalogueSource => Icon.BookOpen,
        ExchangeSetSource { Path: var p } when p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) => Icon.FolderZip,
        _ => Icon.Folder,
    };

    /// <summary>
    /// A small mono tag naming the kind of source: <c>DIR</c>, <c>ZIP</c>,
    /// <c>WEB</c> (an online catalogue), <c>LIST</c> (a community list),
    /// <c>FEED</c> (a shared feed) or <c>S-128</c>. A collection shows its
    /// sources' kind.
    /// </summary>
    public string KindTag => _source is { } s
        ? KindOf(s.Definition)
        : _collection.IsSession ? "S-128" : _collection.Sources.Select(x => KindOf(x.Definition)).FirstOrDefault() ?? "DIR";

    /// <summary>The dataset count shown after the name, or "—" before there is an index.</summary>
    public string Status
    {
        get
        {
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

    private IReadOnlyList<LibrarySource> Sources => _source is { } s ? [s] : _collection.Sources;

    private (string? Line, LibraryNodeStatusKind Kind) ComputeStatus()
    {
        var c = CultureInfo.CurrentCulture;
        var sources = Sources;
        if (_downloadStatus is { } downloading)
            return (downloading, LibraryNodeStatusKind.Busy);
        if (sources.Any(x => x.State == LibrarySourceState.Indexing))
            return (Strings.Library_Status_Indexing, LibraryNodeStatusKind.Busy);
        if (sources.FirstOrDefault(x => x.State == LibrarySourceState.Failed && x.Index is null) is { } failed)
            return (string.Format(c, Strings.Library_StatusLine_FailedFormat, failed.Error ?? string.Empty), LibraryNodeStatusKind.Error);

        // A shared feed says whether its server is reachable (for a source, or a one-source collection).
        if (sources is [{ Definition: S100FeedSource feed }] && _health?.Invoke(feed) is { } health)
            return FeedStatus(feed, health, c);

        var problems = sources.Sum(x => x.Index?.Diagnostics.Count(d => d.Severity >= IndexDiagnosticSeverity.Warning) ?? 0);
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
        ChartCatalogsFeedSource => "LIST",
        S100FeedSource => "FEED",
        S128CatalogueSource => "S-128",
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
    public bool CanRemove => !_collection.IsSession;

    /// <summary>True when this is a session catalogue that can be kept in the library.</summary>
    public bool CanKeep => _collection.IsSession && _source is not null;

    /// <summary>
    /// Every item under this node with the source it came from, in source
    /// order.
    /// </summary>
    public IEnumerable<(CollectionItem Item, LibrarySource Source)> EnumerateItems()
    {
        var sources = _source is { } s ? [s] : _collection.Sources;
        return sources.SelectMany(src => (src.Index?.Items ?? []).Select(item => (item, src)));
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
                existing.RaiseAll();
                var at = Children.IndexOf(existing);
                if (at != i)
                    Children.Move(at, i);
            }
        }
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Name));
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

    private static string DescribeSource(CollectionSource source) => source.DisplayName ?? source switch
    {
        LocalFolderSource f => LeafName(f.Path),
        ExchangeSetSource e => LeafName(e.Path),
        S128CatalogueSource c => LeafName(c.Path),
        NoaaEncFeedSource => Strings.Library_NoaaFeed,
        UsaceIencFeedSource => Strings.Library_UsaceFeed,
        ChartCatalogsFeedSource c => c.CatalogUri.Host,
        S100FeedSource f => f.FeedUri.Host,
        _ => source.GetType().Name,
    };

    private static string SourceLocation(CollectionSource source) => source switch
    {
        LocalFolderSource f => f.Path,
        ExchangeSetSource e => e.Path,
        S128CatalogueSource c => c.Path,
        NoaaEncFeedSource n => n.CatalogUri.AbsoluteUri,
        UsaceIencFeedSource u => u.CatalogUri.AbsoluteUri,
        ChartCatalogsFeedSource c => c.CatalogUri.AbsoluteUri,
        S100FeedSource f => MaskToken(f.FeedUri),
        _ => string.Empty,
    };

    /// <summary>The online source's full URL (for "Copy URL"), or <see langword="null"/> for a local source.</summary>
    public Uri? SourceUrl => _source?.Definition switch
    {
        NoaaEncFeedSource n => n.CatalogUri,
        UsaceIencFeedSource u => u.CatalogUri,
        ChartCatalogsFeedSource c => c.CatalogUri,
        S100FeedSource f => f.FeedUri,
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

    private static string LeafName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
