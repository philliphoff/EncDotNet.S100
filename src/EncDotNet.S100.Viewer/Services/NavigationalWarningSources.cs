using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The Library source a loaded S-124 dataset came from, and how fresh its
/// warnings are (#932).
/// </summary>
/// <param name="Id">The Library source's id.</param>
/// <param name="Name">The source's display name, e.g. "AMSA".</param>
/// <param name="ReceivedAt">
/// When the source last answered: the warnings on screen are the ones received
/// then. <see langword="null"/> when unknown.
/// </param>
/// <param name="IsStale">True when the source's latest refresh failed, so the warnings are older than that refresh.</param>
/// <param name="IsRefreshing">True while the source is being read again.</param>
internal sealed record NavigationalWarningSource(
    Guid Id,
    string Name,
    DateTimeOffset? ReceivedAt,
    bool IsStale,
    bool IsRefreshing = false)
{
    /// <summary>The source's key in the seen-warnings store.</summary>
    public string Key => Id.ToString("N");
}

/// <summary>
/// Finds the Library source of a loaded S-124 dataset, and refreshes sources
/// through the Library's own sync path (#932).
/// </summary>
internal interface INavigationalWarningSources
{
    /// <summary>
    /// The Library source <paramref name="entry"/> came from, when it is a
    /// SECOM service or online feed; <see langword="null"/> for a file opened
    /// directly, which no refresh updates.
    /// </summary>
    NavigationalWarningSource? SourceOf(DatasetEntry entry);

    /// <summary>
    /// Reads the sources again; each is synced after it is indexed, as the
    /// Library's Refresh does.
    /// </summary>
    void Refresh(IReadOnlyCollection<Guid> sourceIds);

    /// <summary>Raised on the UI thread when a source's freshness may have changed.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// <see cref="INavigationalWarningSources"/> over the Library: a dataset's
/// source is the one its Datasets row is grouped under (a Library load), else
/// the online source whose local copy is the dataset's file. Freshness is the
/// source's reachability (<see cref="FeedHealth"/>), which SECOM services and
/// shared feeds both report; the Library's Refresh re-reads a source and its
/// sync downloads what changed.
/// </summary>
internal sealed class LibraryNavigationalWarningSources : INavigationalWarningSources, IDisposable
{
    private readonly CollectionLibrary _library;
    private readonly DatasetsViewModel _datasets;
    private readonly ILibraryDownloader _downloader;
    private readonly Func<CollectionSource, FeedHealth?> _health;
    private readonly Func<Guid, string> _nameOf;
    private readonly Action<Action> _dispatch;
    private Dictionary<string, Guid>? _byPath;
    private int _posted;

    public LibraryNavigationalWarningSources(
        CollectionLibrary library,
        DatasetsViewModel datasets,
        ILibraryDownloader downloader,
        Func<CollectionSource, FeedHealth?> health,
        Func<Guid, string> nameOf,
        Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(nameOf);
        _library = library;
        _datasets = datasets;
        _downloader = downloader;
        _health = health;
        _nameOf = nameOf;
        _dispatch = dispatch ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        _library.Changed += OnLibraryChanged;
        _library.SourceIndexed += OnSourceIndexed;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public NavigationalWarningSource? SourceOf(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var header = entry.Source is null ? null : _datasets.ExchangeSetHeaders.FirstOrDefault(h => h.Contains(entry.Source));
        Guid? id = header?.GroupKey is { } key && Guid.TryParseExact(key, "N", out var grouped) ? grouped : null;
        if (id is null)
        {
            var path = header is not null && entry.RelativePath is { } relative
                ? Path.Combine(header.SourcePath, relative)
                : entry.FilePath;
            if (ByPath().TryGetValue(Normalize(path), out var found))
                id = found;
        }

        if (id is not { } sourceId
            || _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId) is not { } source
            || !LibrarySync.CanSyncByDefault(source.Definition))
        {
            return null;
        }

        return Describe(source);
    }

    /// <inheritdoc />
    public void Refresh(IReadOnlyCollection<Guid> sourceIds)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        foreach (var id in sourceIds.Distinct())
            _library.Refresh(sourceId: id);
    }

    private NavigationalWarningSource Describe(LibrarySource source)
    {
        var name = _nameOf(source.Id);
        var refreshing = source.State is LibrarySourceState.Indexing or LibrarySourceState.Pending;
        return _health(source.Definition) switch
        {
            { IsReachable: true } reached => new(source.Id, name, reached.CheckedAt, IsStale: false, refreshing),
            // Not answering: the warnings shown are the ones received with the copy still served.
            { } failing => new(source.Id, name, failing.CopyFetchedAt ?? source.Index?.IndexedAt, IsStale: true, refreshing),
            // Not read yet this session (its cached index is shown), or the last index threw.
            _ => new(source.Id, name, source.Index?.IndexedAt, IsStale: source.State == LibrarySourceState.Failed, refreshing),
        };
    }

    /// <summary>The local copies of the online sources' S-124 items, by full path.</summary>
    private Dictionary<string, Guid> ByPath()
    {
        if (_byPath is { } cached)
            return cached;

        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _library.Collections.SelectMany(c => c.Sources))
        {
            if (source.Index is not { } index || !LibrarySync.CanSyncByDefault(source.Definition))
                continue;
            foreach (var item in index.Items)
            {
                if (!string.Equals(item.ProductSpec, NavigationalWarningsService.WarningProduct, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (_downloader.Localize(item).Location is LocalItemLocation local)
                    map.TryAdd(Normalize(Path.Combine(local.RootPath, local.RelativePath)), source.Id);
            }
        }

        return _byPath = map;
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private void OnLibraryChanged(object? sender, EventArgs e) => Post();

    private void OnSourceIndexed(object? sender, LibrarySourceIndexedEventArgs e) => Post();

    /// <summary>Raises <see cref="Changed"/> once on the UI thread for a burst of Library events.</summary>
    private void Post()
    {
        if (Interlocked.Exchange(ref _posted, 1) == 1)
            return;
        _dispatch(() =>
        {
            Interlocked.Exchange(ref _posted, 0);
            _byPath = null;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Dispose()
    {
        _library.Changed -= OnLibraryChanged;
        _library.SourceIndexed -= OnSourceIndexed;
    }
}
