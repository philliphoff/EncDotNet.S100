using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// The Library's rows as the user sees them (#1394): a row for any indexed
/// item, wired exactly as the Library panel's own rows are, so its state,
/// tags and details match. The panel, the Timeline and the MCP Library tools
/// all build their rows here.
/// </summary>
internal sealed class LibraryRows
{
    private readonly CollectionLibrary _library;
    private readonly ILibraryLoader _loader;
    private readonly ILibraryDownloader _downloader;
    private readonly DownloadLibraryItemsWorkflow _download;

    /// <summary>Creates the rows.</summary>
    /// <param name="library">The Library the items are indexed in.</param>
    /// <param name="loader">Says whether an item is open.</param>
    /// <param name="downloader">Says whether an item is downloaded, and how its download goes.</param>
    /// <param name="download">Downloads a row again (Retry).</param>
    /// <param name="time">The clock (forecast ages, staleness).</param>
    /// <param name="timeFormat">The Local/UTC setting.</param>
    public LibraryRows(
        CollectionLibrary library,
        ILibraryLoader loader,
        ILibraryDownloader downloader,
        DownloadLibraryItemsWorkflow download,
        TimeProvider time,
        ITimeFormatProvider timeFormat)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(timeFormat);
        _library = library;
        _loader = loader;
        _downloader = downloader;
        _download = download;
        Context = new LibraryRowContext(time, timeFormat, loader.StateOf, downloader, RetryAsync);
    }

    /// <summary>What the rows read their state through: the clock, the Local/UTC setting, load and download state, Retry.</summary>
    public LibraryRowContext Context { get; }

    /// <summary>Raised when an item opened, closed or downloaded (not when the Library re-indexed).</summary>
    public event EventHandler? StateChanged
    {
        add
        {
            _loader.Changed += value;
            _downloader.Changed += value;
        }
        remove
        {
            _loader.Changed -= value;
            _downloader.Changed -= value;
        }
    }

    /// <summary>Raised when a row's state may have changed: the Library re-indexed, or an item opened, closed or downloaded.</summary>
    public event EventHandler? Changed
    {
        add
        {
            _library.Changed += value;
            _loader.Changed += value;
            _downloader.Changed += value;
        }
        remove
        {
            _library.Changed -= value;
            _loader.Changed -= value;
            _downloader.Changed -= value;
        }
    }

    /// <summary>Raised (throttled) as download bytes arrive.</summary>
    public event EventHandler? ProgressChanged
    {
        add => _downloader.ProgressChanged += value;
        remove => _downloader.ProgressChanged -= value;
    }

    /// <summary>The Library's collections.</summary>
    public IReadOnlyList<LibraryCollection> Collections => _library.Collections;

    /// <summary>The running bulk download, or <see langword="null"/>.</summary>
    public LibraryDownloadProgress? Progress => _downloader.Progress;

    /// <summary>Where <paramref name="item"/> stands in a download, or <see langword="null"/> when it isn't downloading.</summary>
    /// <param name="item">The indexed item.</param>
    public LibraryDownloadItemStatus? StatusOf(CollectionItem item) => _downloader.StatusOf(item);

    /// <summary>True when <paramref name="item"/>'s data (its downloaded copy, for an online item) is open now.</summary>
    /// <param name="item">The indexed item.</param>
    public bool IsOpen(CollectionItem item) => _loader.StateOf(_downloader.Localize(item)) == LibraryLoadState.Loaded;

    /// <summary>A row for <paramref name="item"/>, named with the collection its source is in.</summary>
    /// <param name="item">The indexed item.</param>
    /// <param name="source">The source it was indexed from.</param>
    public LibraryItemViewModel Create(CollectionItem item, LibrarySource source) =>
        Create(item, source, _library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == source.Id))?.Definition.Name);

    /// <summary>A row for <paramref name="item"/>.</summary>
    /// <param name="item">The indexed item.</param>
    /// <param name="source">The source it was indexed from.</param>
    /// <param name="collectionName">The name of the collection the source is in.</param>
    public LibraryItemViewModel Create(CollectionItem item, LibrarySource source, string? collectionName) =>
        new(item, source, Context, collectionName);

    /// <summary>The row for the item with the id <paramref name="itemId"/> (<c>source:key</c>), or <see langword="null"/> when the Library has no such item.</summary>
    /// <param name="itemId">The item's id.</param>
    public LibraryItemViewModel? Find(string itemId)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        return LibraryQuery.FindById(_library.Collections, itemId.Trim()) is var (item, source) ? Create(item, source) : null;
    }

    private Task RetryAsync(LibraryItemViewModel row) => _download.RunAsync([(row.Item, row.Source)], load: false);
}
