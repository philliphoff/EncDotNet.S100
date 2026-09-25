using System.Collections.ObjectModel;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;
using FluentIcons.Common;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A node of the Library panel's tree: a collection, or one of its sources.
/// Nodes are updated in place from new <see cref="LibraryService"/> snapshots
/// so tree expansion and selection survive background indexing.
/// </summary>
internal sealed class LibraryNodeViewModel : ViewModelBase
{
    private LibraryCollection _collection;
    private LibrarySource? _source;
    private bool _isExpanded;

    private LibraryNodeViewModel(LibraryCollection collection, LibrarySource? source)
    {
        _collection = collection;
        _source = source;
    }

    /// <summary>Creates a collection node with a child per source.</summary>
    public static LibraryNodeViewModel ForCollection(LibraryCollection collection)
    {
        var node = new LibraryNodeViewModel(collection, null);
        foreach (var source in collection.Sources)
            node.Children.Add(new LibraryNodeViewModel(collection, source));
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

    /// <summary>The item count, or indexing/error state, shown after the name.</summary>
    public string Status
    {
        get
        {
            var sources = _source is { } s ? [s] : _collection.Sources;
            if (sources.Any(x => x.State == LibrarySourceState.Indexing))
                return Strings.Library_Status_Indexing;
            if (sources.Any(x => x.State == LibrarySourceState.Failed) && sources.All(x => x.Index is null))
                return Strings.Library_Status_Failed;
            if (sources.All(x => x.Index is null))
                return Strings.Library_Status_Pending;
            return sources.Sum(x => x.Index?.Items.Count ?? 0).ToString("N0", CultureInfo.CurrentCulture);
        }
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
                Children.Insert(i, new LibraryNodeViewModel(collection, source));
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
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanKeep));
    }

    private static string DescribeSource(CollectionSource source) => source.DisplayName ?? source switch
    {
        LocalFolderSource f => LeafName(f.Path),
        ExchangeSetSource e => LeafName(e.Path),
        S128CatalogueSource c => LeafName(c.Path),
        NoaaEncFeedSource n => n.Filter.IsUnscoped ? Strings.Library_NoaaAll : Strings.Library_NoaaFeed,
        UsaceIencFeedSource u => u.Filter.IsUnscoped ? Strings.Library_UsaceAll : Strings.Library_UsaceFeed,
        _ => source.GetType().Name,
    };

    private static string SourceLocation(CollectionSource source) => source switch
    {
        LocalFolderSource f => f.Path,
        ExchangeSetSource e => e.Path,
        S128CatalogueSource c => c.Path,
        NoaaEncFeedSource n => n.CatalogUri.AbsoluteUri,
        UsaceIencFeedSource u => u.CatalogUri.AbsoluteUri,
        _ => string.Empty,
    };

    private static string LeafName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
