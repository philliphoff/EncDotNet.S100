using System.Collections.Concurrent;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Library;

/// <summary>The outcome of <see cref="LibraryDownloads.DownloadAsync"/>.</summary>
/// <param name="Downloaded">Downloads that completed.</param>
/// <param name="Failed">Downloads that failed (or were cancelled on their own).</param>
/// <param name="Cancelled">True when the batch was cancelled before every download finished.</param>
public sealed record LibraryDownloadResult(int Downloaded, int Failed, bool Cancelled);

/// <summary>Where one item stands in a download.</summary>
public enum LibraryDownloadItemState
{
    /// <summary>Waiting in a bulk download.</summary>
    Queued,

    /// <summary>Downloading now.</summary>
    Running,

    /// <summary>The last download failed (any existing copy is kept).</summary>
    Failed,
}

/// <summary>One item's download status.</summary>
/// <param name="State">Queued, running or failed.</param>
/// <param name="BytesReceived">Bytes received so far (running).</param>
/// <param name="TotalBytes">The download's size, if known.</param>
/// <param name="Error">Why it failed (failed).</param>
public sealed record LibraryDownloadItemStatus(
    LibraryDownloadItemState State, long BytesReceived = 0, long? TotalBytes = null, string? Error = null)
{
    /// <summary>The fraction received (0–1), when the size is known.</summary>
    public double? Fraction => TotalBytes is > 0 and var total ? Math.Clamp(BytesReceived / (double)total, 0, 1) : null;
}

/// <summary>The running bulk download: how many items are done and how many bytes remain.</summary>
/// <param name="Completed">Items downloaded.</param>
/// <param name="Failed">Items that failed.</param>
/// <param name="Total">Items in the batch.</param>
/// <param name="BytesDone">Bytes received, including finished items.</param>
/// <param name="BytesTotal">The batch's total size, as far as known.</param>
public sealed record LibraryDownloadProgress(int Completed, int Failed, int Total, long BytesDone, long BytesTotal)
{
    /// <summary>Items still to finish.</summary>
    public int Remaining => Math.Max(0, Total - Completed - Failed);

    /// <summary>Bytes still to receive.</summary>
    public long BytesLeft => Math.Max(0, BytesTotal - BytesDone);

    /// <summary>The fraction done (0–1), by bytes when sizes are known, else by items.</summary>
    public double Fraction => BytesTotal > 0
        ? Math.Clamp(BytesDone / (double)BytesTotal, 0, 1)
        : Total == 0 ? 1 : (Completed + Failed) / (double)Total;
}

/// <summary>
/// Downloads online library items (NOAA and USACE ENCs, community packages,
/// S-100 feeds and the NOAA S-102/S-104/S-111 forecast catalogues; issues
/// #655, #670, #680, #685) into managed folders, a few at a time, and
/// resolves their downloaded copies so they behave as local items. UI-free:
/// the viewer adds its progress notification on top, and a headless host
/// (#792) reads <see cref="Progress"/> and <see cref="StatusOf"/> directly.
/// </summary>
/// <remarks>Thread-safe. Events are raised on whichever thread did the work.</remarks>
public sealed class LibraryDownloads : ILibraryLocalCopies
{
    /// <summary>How many downloads run at once.</summary>
    public const int MaxConcurrentDownloads = 3;

    /// <summary>How often, at most, <see cref="ProgressChanged"/> is raised.</summary>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly Func<RemoteItemLocation, EncCellDownloader?> _downloaderFor;
    private readonly ConcurrentDictionary<string, DownloadedCell?> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LibraryDownloadItemStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _itemCancellation = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _batchCancellation;
    private LibraryDownloadProgress? _progress;
    private long _lastProgressTicks;

    /// <summary>Creates downloads that save every item with <paramref name="downloader"/>.</summary>
    /// <param name="downloader">The downloader (and so the managed folder) for every item.</param>
    public LibraryDownloads(EncCellDownloader downloader)
        : this(_ => downloader)
    {
        ArgumentNullException.ThrowIfNull(downloader);
    }

    /// <summary>
    /// Creates downloads that pick a downloader (and so a managed folder) per
    /// download location, e.g. one per provider or per
    /// <see cref="RemoteItemLocation.DownloadFolder"/>.
    /// </summary>
    /// <param name="downloaderFor">
    /// The downloader for a location; <see langword="null"/> means the location
    /// is not downloadable. See <see cref="ManagedFolders"/> for the hosts' layout.
    /// </param>
    public LibraryDownloads(Func<RemoteItemLocation, EncCellDownloader?> downloaderFor)
    {
        ArgumentNullException.ThrowIfNull(downloaderFor);
        _downloaderFor = downloaderFor;
    }

    /// <summary>
    /// The managed download layout the viewer uses under its downloads root,
    /// so every host keeps downloads in the same place: an item that names its
    /// own folder (community lists, S-100 feeds and catalogues) gets that
    /// folder; otherwise USACE cells go to <c>usace-ienc</c> and everything else
    /// to <c>noaa-enc</c>.
    /// </summary>
    /// <param name="http">The client downloads use (with a generous timeout).</param>
    /// <param name="downloadsRoot">The host's downloads root.</param>
    /// <param name="secomHttp">
    /// The client SECOM objects are downloaded with (see <see cref="EncCellDownloader.SecomHttpClient"/>);
    /// <see langword="null"/> uses <paramref name="http"/>.
    /// </param>
    /// <param name="revocation">Checks SECOM signers for revocation (see <see cref="EncCellDownloader.Revocation"/>); <see langword="null"/> skips it.</param>
    /// <returns>A downloader selector for <see cref="LibraryDownloads(Func{RemoteItemLocation, EncCellDownloader})"/>.</returns>
    public static Func<RemoteItemLocation, EncCellDownloader?> ManagedFolders(
        HttpClient http, string downloadsRoot, HttpClient? secomHttp = null, SecomRevocation? revocation = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrEmpty(downloadsRoot);
        var noaa = new EncCellDownloader(http, Path.Combine(downloadsRoot, "noaa-enc"));
        var usace = new EncCellDownloader(http, Path.Combine(downloadsRoot, "usace-ienc"));
        var byFolder = new ConcurrentDictionary<string, EncCellDownloader>(StringComparer.Ordinal);
        return remote => remote.DownloadFolder is { } folder
            ? byFolder.GetOrAdd(folder, f => new EncCellDownloader(http, Path.Combine(downloadsRoot, f)) { SecomHttpClient = secomHttp, Revocation = revocation })
            : remote.Uri.Host.EndsWith("ienccloud.us", StringComparison.OrdinalIgnoreCase) ? usace : noaa;
    }

    /// <summary>Raised when a download completes, fails or is cancelled (a copy appeared or changed), and when a batch starts or ends.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised (throttled to <see cref="ProgressInterval"/>) as bytes arrive, so
    /// a host can show progress without re-resolving every item.
    /// </summary>
    public event EventHandler? ProgressChanged;

    /// <summary>The running bulk download, or <see langword="null"/> when none is running.</summary>
    public LibraryDownloadProgress? Progress => Volatile.Read(ref _progress);

    /// <summary>Where <paramref name="item"/> stands in a download, or <see langword="null"/> when it is not queued, running or failed.</summary>
    /// <param name="item">The library item.</param>
    public LibraryDownloadItemStatus? StatusOf(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return StatusKey(item) is { } key && _status.TryGetValue(key, out var status) ? status : null;
    }

    /// <summary>Cancels the running bulk download.</summary>
    public void CancelAll() => Volatile.Read(ref _batchCancellation)?.Cancel();

    /// <summary>Cancels <paramref name="item"/>'s download (queued or running); the rest of the batch carries on.</summary>
    /// <param name="item">The library item.</param>
    public void Cancel(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (StatusKey(item) is { } key && _itemCancellation.TryGetValue(key, out var cancellation))
            cancellation.Cancel();
    }

    /// <inheritdoc />
    public CollectionItem Localize(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Location is not RemoteItemLocation remote || Downloaded(item) is not { } cell)
            return item;

        // A package's cells each resolve to their own copy; a package entry
        // not yet re-indexed into its cells stays online.
        if (remote.Package is null)
            return item with { Location = cell.Location };
        return cell.Datasets.TryGetValue(item.Name, out var location) ? item with { Location = location } : item;
    }

    /// <inheritdoc />
    public bool IsOutdated(CollectionItem item) =>
        item.Location is RemoteItemLocation && Downloaded(item) is { } cell && cell.IsOlderThan(item);

    /// <inheritdoc />
    public int? LocalEditionOf(CollectionItem item) => Downloaded(item)?.Edition;

    /// <inheritdoc />
    public DateTimeOffset? LocalPublishedAtOf(CollectionItem item) => Downloaded(item)?.PublishedAt;

    /// <summary>
    /// The names of the copies downloaded into the managed folder that
    /// <paramref name="location"/> downloads to (cells or packages); empty
    /// when the location is not downloadable.
    /// </summary>
    /// <param name="location">Any download location of that folder.</param>
    public IReadOnlyList<string> DownloadedNames(RemoteItemLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return _downloaderFor(location)?.ListDownloaded() ?? [];
    }

    /// <summary>The managed folder that <paramref name="location"/> downloads to, or <see langword="null"/> when it is not downloadable.</summary>
    /// <param name="location">Any download location of that folder.</param>
    public string? FolderOf(RemoteItemLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return _downloaderFor(location)?.Root;
    }

    /// <summary>
    /// Deletes the downloaded copy named <paramref name="name"/> from the
    /// managed folder that <paramref name="location"/> downloads to, and
    /// forgets it, so its items are online again.
    /// </summary>
    /// <param name="location">Any download location of that folder.</param>
    /// <param name="name">The cell or package name (as <see cref="DownloadedNames"/> lists it).</param>
    /// <returns>True when a copy was deleted.</returns>
    public bool Delete(RemoteItemLocation location, string name)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_downloaderFor(location) is not { } downloader)
            return false;

        bool deleted;
        try
        {
            deleted = downloader.Delete(name);
        }
        finally
        {
            _known.TryRemove(Key(downloader, name), out _);
        }

        if (deleted)
            RaiseChanged();
        return deleted;
    }

    /// <summary>True when <paramref name="item"/> can be downloaded (online, not cancelled, and a downloader takes it).</summary>
    /// <param name="item">The library item.</param>
    public bool CanDownload(CollectionItem item) =>
        item.Location is RemoteItemLocation remote
        && item.Status != CollectionItemStatus.Cancelled
        && _downloaderFor(remote) is not null;

    /// <summary>
    /// Downloads the downloadable items among <paramref name="items"/>, at most
    /// <see cref="MaxConcurrentDownloads"/> at a time. A package downloads once
    /// however many of its datasets are asked for. Never throws for a failed
    /// or cancelled download: the result counts them.
    /// </summary>
    /// <param name="items">The items to download; others are ignored.</param>
    /// <param name="progress">
    /// Told the batch's progress when it starts and each time an item finishes
    /// (reported on the finishing thread, not posted), or <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the whole batch.</param>
    /// <returns>How many downloaded and failed, and whether the batch was cancelled.</returns>
    public async Task<LibraryDownloadResult> DownloadAsync(
        IReadOnlyList<CollectionItem> items,
        IProgress<LibraryDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        // A package downloads once however many of its cells are asked for; the
        // same name from different sources (folders) downloads once per source.
        var toDownload = items.Where(CanDownload).DistinctBy(i => StatusKey(i)!, StringComparer.OrdinalIgnoreCase).ToArray();
        if (toDownload.Length == 0)
            return new LibraryDownloadResult(0, 0, false);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Volatile.Write(ref _batchCancellation, cancellation);
        var sizes = toDownload.Select(i => ((RemoteItemLocation)i.Location).SizeBytes ?? 0).ToArray();
        foreach (var item in toDownload)
        {
            _status[StatusKey(item)!] = new LibraryDownloadItemStatus(
                LibraryDownloadItemState.Queued, 0, ((RemoteItemLocation)item.Location).SizeBytes);
        }

        var start = new LibraryDownloadProgress(0, 0, toDownload.Length, 0, sizes.Sum());
        Volatile.Write(ref _progress, start);
        RaiseChanged();
        progress?.Report(start);

        var done = 0;
        var failed = 0;
        var received = new long[toDownload.Length];
        using var gate = new SemaphoreSlim(MaxConcurrentDownloads);

        async Task DownloadOneAsync(CollectionItem item, int index)
        {
            var key = StatusKey(item)!;
            using var itemCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            _itemCancellation[key] = itemCancellation;
            try
            {
                await gate.WaitAsync(itemCancellation.Token).ConfigureAwait(false);
                try
                {
                    var size = ((RemoteItemLocation)item.Location).SizeBytes;
                    _status[key] = new LibraryDownloadItemStatus(LibraryDownloadItemState.Running, 0, size);
                    RaiseProgress(force: true);
                    var bytes = new Progress<long>(n =>
                    {
                        // Progress<T> posts each report, so one can arrive after the
                        // item has finished or failed. Only update an item that is
                        // still running: a late report must not turn a failure back
                        // into Running (the batch would then drop it, error and all)
                        // or re-add an item that already completed.
                        if (!_status.TryGetValue(key, out var current)
                            || current.State != LibraryDownloadItemState.Running
                            || !_status.TryUpdate(key, current with { BytesReceived = n }, current))
                        {
                            return;
                        }

                        Interlocked.Exchange(ref received[index], n);
                        UpdateProgress(done, failed, received, sizes);
                        RaiseProgress(force: false);
                    });

                    var downloader = _downloaderFor((RemoteItemLocation)item.Location)!;
                    var cell = await downloader.DownloadAsync(item, bytes, itemCancellation.Token).ConfigureAwait(false);
                    _known[Key(downloader, DownloadName(item))] = cell;
                    _status.TryRemove(key, out _);
                    Interlocked.Increment(ref done);
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && itemCancellation.IsCancellationRequested)
            {
                // This item alone was cancelled: the batch carries on.
                _status.TryRemove(key, out _);
                Interlocked.Increment(ref failed);
            }
            catch (OperationCanceledException)
            {
                _status.TryRemove(key, out _);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _status[key] = new LibraryDownloadItemStatus(LibraryDownloadItemState.Failed, 0, null, ex.Message);
                Interlocked.Increment(ref failed);
            }
            finally
            {
                _itemCancellation.TryRemove(key, out _);
            }

            Interlocked.Exchange(ref received[index], sizes[index]);
            UpdateProgress(Volatile.Read(ref done), Volatile.Read(ref failed), received, sizes);
            if (progress is not null && Progress is { } now)
                progress.Report(now);
            RaiseChanged();
        }

        var cancelled = false;
        try
        {
            await Task.WhenAll(toDownload.Select(DownloadOneAsync)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            // Anything still queued when the batch stops is no longer waiting.
            foreach (var item in toDownload)
            {
                if (StatusKey(item) is { } key && _status.TryGetValue(key, out var status) && status.State != LibraryDownloadItemState.Failed)
                    _status.TryRemove(key, out _);
            }

            Interlocked.CompareExchange(ref _batchCancellation, null, cancellation);
            Volatile.Write(ref _progress, null);
            RaiseChanged();
        }

        return new LibraryDownloadResult(done, failed, cancelled);
    }

    private void UpdateProgress(int done, int failed, long[] received, long[] sizes)
    {
        if (Volatile.Read(ref _progress) is not { } current)
            return;
        long bytes = 0;
        for (var i = 0; i < received.Length; i++)
            bytes += Math.Min(Interlocked.Read(ref received[i]), sizes[i] > 0 ? sizes[i] : long.MaxValue);
        Volatile.Write(ref _progress, current with { Completed = done, Failed = failed, BytesDone = bytes });
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="ProgressChanged"/> at most every <see cref="ProgressInterval"/> (unless forced).</summary>
    private void RaiseProgress(bool force)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastProgressTicks);
        if (!force && now - last < ProgressInterval.Ticks)
            return;
        if (Interlocked.CompareExchange(ref _lastProgressTicks, now, last) != last && !force)
            return;
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The key an item's download status is kept under (its downloader and download name).</summary>
    private string? StatusKey(CollectionItem item) =>
        item.Location is RemoteItemLocation remote && _downloaderFor(remote) is { } downloader
            ? Key(downloader, DownloadName(item))
            : null;

    private DownloadedCell? Downloaded(CollectionItem item)
    {
        if (item.Location is not RemoteItemLocation remote || _downloaderFor(remote) is not { } downloader)
            return null;
        var name = DownloadName(item);
        return _known.GetOrAdd(Key(downloader, name), _ => downloader.TryGetDownloaded(name));
    }

    /// <summary>What a download is saved as: its package, or the cell itself.</summary>
    private static string DownloadName(CollectionItem item) =>
        (item.Location as RemoteItemLocation)?.Package ?? item.Name;

    private static string Key(EncCellDownloader downloader, string cellName) => downloader.Root + "|" + cellName;
}
