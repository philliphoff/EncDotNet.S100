using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Keeps the local datasets of sources with <see cref="CollectionSource.ShowOnMap"/>
/// on the map (issue #809): after every index of such a source, its local,
/// current items are opened to load as the map pans to them — under one
/// Datasets row for the source — and those it no longer has (gone, cancelled,
/// or no longer downloaded) are closed. Turning the option off, or removing
/// the source, closes them all.
/// </summary>
/// <remarks>
/// The option is remembered with the source, so shown sources come back after
/// a restart: the library indexes every source at start-up. When datasets are
/// closed, a synced source is synced again, so copies kept for being open can
/// be pruned now.
/// </remarks>
internal sealed class LibraryShowOnMapService : IDisposable
{
    private readonly CollectionLibrary _library;
    private readonly LibraryLoadService _loader;
    private readonly ILibraryDownloader _downloader;
    private readonly Func<Guid, string> _nameOf;
    private readonly LibrarySync? _sync;
    private readonly Action<Action> _dispatch;
    private readonly Dictionary<Guid, Dictionary<string, CollectionItem>> _shown = [];

    public LibraryShowOnMapService(
        CollectionLibrary library,
        LibraryLoadService loader,
        ILibraryDownloader downloader,
        Func<Guid, string> nameOf,
        LibrarySync? sync = null,
        Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(nameOf);
        _library = library;
        _loader = loader;
        _downloader = downloader;
        _nameOf = nameOf;
        _sync = sync;
        _dispatch = dispatch ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        _library.SourceIndexed += OnSourceIndexed;
        _library.Changed += OnLibraryChanged;
    }

    /// <summary>Completes when the last apply has finished (for tests).</summary>
    internal Task LastApply { get; private set; } = Task.CompletedTask;

    /// <summary>The sources whose datasets are kept on the map now, with how many.</summary>
    internal IReadOnlyDictionary<Guid, int> Shown => _shown.ToDictionary(p => p.Key, p => p.Value.Count);

    private void OnSourceIndexed(object? sender, LibrarySourceIndexedEventArgs e) =>
        _dispatch(() => LastApply = ApplyAsync(e.Source.Id));

    /// <summary>A source turned off or removed is closed without waiting for an index.</summary>
    private void OnLibraryChanged(object? sender, EventArgs e) => _dispatch(() =>
    {
        var current = _library.Collections.SelectMany(c => c.Sources)
            .Where(s => s.Definition.ShowOnMap)
            .Select(s => s.Id)
            .ToHashSet();
        foreach (var id in _shown.Keys.Where(id => !current.Contains(id)).ToArray())
            LastApply = ApplyAsync(id);
    });

    /// <summary>Opens what the source should show and closes what it should not.</summary>
    internal async Task ApplyAsync(Guid sourceId)
    {
        var source = _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        var wanted = new Dictionary<string, CollectionItem>(StringComparer.OrdinalIgnoreCase);
        if (source is { Definition.ShowOnMap: true, Index: { } index })
        {
            foreach (var item in index.Items)
            {
                if (item.Status == CollectionItemStatus.Cancelled)
                    continue;
                var local = _downloader.Localize(item);
                if (local.Location is LocalItemLocation location && local.ProductSpec != "Unknown")
                    wanted.TryAdd(Key(location), local);
            }
        }
        else if (source is { Definition.ShowOnMap: true })
        {
            return;  // not indexed yet; its index will apply it
        }

        var previous = _shown.TryGetValue(sourceId, out var shown) ? shown : [];
        var gone = previous.Where(p => !wanted.ContainsKey(p.Key)).Select(p => p.Value).ToArray();
        if (wanted.Count > 0)
            _shown[sourceId] = wanted;
        else
            _shown.Remove(sourceId);

        if (gone.Length > 0 && _loader.Close(gone) > 0 && _sync is not null && source is not null && _sync.IsSynced(source.Definition))
            _ = _sync.SyncAsync(sourceId);

        var toOpen = wanted.Values.Where(i => _loader.StateOf(i) == LibraryLoadState.None).ToArray();
        if (toOpen.Length == 0)
            return;
        var label = new LibrarySourceLabel(sourceId, _nameOf(sourceId));
        await _loader.LoadAsync(toOpen, defer: true, _ => label, notify: false).ConfigureAwait(true);
    }

    private static string Key(LocalItemLocation location) => location.RootPath + "|" + location.RelativePath;

    public void Dispose()
    {
        _library.SourceIndexed -= OnSourceIndexed;
        _library.Changed -= OnLibraryChanged;
    }
}
