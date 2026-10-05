using System.Collections.Concurrent;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>The outcome of <see cref="ILibraryDownloader.DownloadAsync"/>.</summary>
/// <param name="Downloaded">Cells downloaded.</param>
/// <param name="Failed">Cells whose download failed.</param>
/// <param name="Cancelled">True when the user cancelled before every cell finished.</param>
internal sealed record LibraryDownloadResult(int Downloaded, int Failed, bool Cancelled);

/// <summary>Where one item stands in a download.</summary>
internal enum LibraryDownloadItemState
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
internal sealed record LibraryDownloadItemStatus(
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
internal sealed record LibraryDownloadProgress(int Completed, int Failed, int Total, long BytesDone, long BytesTotal)
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

/// <summary>Downloads online library items and resolves their downloaded copies.</summary>
internal interface ILibraryDownloader
{
    /// <summary>Raised when a download completes, fails or is cancelled (a copy appeared or changed).</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Raised (throttled) as bytes arrive, so rows and the bulk bar can show
    /// progress without re-resolving every item's availability.
    /// </summary>
    event EventHandler? ProgressChanged { add { } remove { } }

    /// <summary>The running bulk download, or <see langword="null"/> when none is running.</summary>
    LibraryDownloadProgress? Progress => null;

    /// <summary>Where <paramref name="item"/> stands in a download, or <see langword="null"/> when it is not queued, running or failed.</summary>
    LibraryDownloadItemStatus? StatusOf(CollectionItem item) => null;

    /// <summary>Cancels the running bulk download.</summary>
    void CancelAll()
    {
    }

    /// <summary>Cancels <paramref name="item"/>'s download (queued or running).</summary>
    void Cancel(CollectionItem item)
    {
    }

    /// <summary>
    /// Returns <paramref name="item"/> with a local location when it is an
    /// online item that has been downloaded; otherwise the item itself.
    /// </summary>
    CollectionItem Localize(CollectionItem item);

    /// <summary>True when the downloaded copy of <paramref name="item"/> is an older edition or update.</summary>
    bool IsOutdated(CollectionItem item);

    /// <summary>The edition of <paramref name="item"/>'s downloaded copy, or <see langword="null"/> when not downloaded (or unknown).</summary>
    int? LocalEditionOf(CollectionItem item) => null;

    /// <summary>
    /// When <paramref name="item"/>'s downloaded copy was published (for a
    /// forecast, its run time), or <see langword="null"/> when not downloaded (or unknown).
    /// </summary>
    DateTimeOffset? LocalPublishedAtOf(CollectionItem item) => null;

    /// <summary>True when <paramref name="item"/> can be downloaded.</summary>
    bool CanDownload(CollectionItem item);

    /// <summary>Downloads the downloadable items among <paramref name="items"/>, reporting progress in a notification.</summary>
    Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default);
}

/// <summary>
/// Downloads online ENC cells from the library (NOAA, USACE; issues #655,
/// #670) into the viewer's managed download folders, a few at a time, with a cancellable progress
/// notification; afterwards the cells behave as local items.
/// </summary>
internal sealed class LibraryDownloadService : ILibraryDownloader
{
    /// <summary>How many cells download at once.</summary>
    internal const int MaxConcurrentDownloads = 3;

    /// <summary>How often, at most, <see cref="ProgressChanged"/> is raised.</summary>
    internal static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly Func<RemoteItemLocation, EncCellDownloader?> _downloaderFor;
    private readonly INotificationService? _notifications;
    private readonly ConcurrentDictionary<string, DownloadedCell?> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LibraryDownloadItemStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _itemCancellation = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _batchCancellation;
    private LibraryDownloadProgress? _progress;
    private long _lastProgressTicks;

    /// <summary>Creates a service that downloads every cell with <paramref name="downloader"/>.</summary>
    public LibraryDownloadService(EncCellDownloader downloader, INotificationService? notifications = null)
        : this(_ => downloader, notifications)
    {
        ArgumentNullException.ThrowIfNull(downloader);
    }

    /// <summary>
    /// Creates a service that picks a downloader (and so a managed folder)
    /// per download location, e.g. one per provider or per
    /// <see cref="RemoteItemLocation.DownloadFolder"/>; <see langword="null"/>
    /// means the location is not downloadable.
    /// </summary>
    public LibraryDownloadService(
        Func<RemoteItemLocation, EncCellDownloader?> downloaderFor, INotificationService? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(downloaderFor);
        _downloaderFor = downloaderFor;
        _notifications = notifications;
    }

    public event EventHandler? Changed;

    public event EventHandler? ProgressChanged;

    public LibraryDownloadProgress? Progress => Volatile.Read(ref _progress);

    public LibraryDownloadItemStatus? StatusOf(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return StatusKey(item) is { } key && _status.TryGetValue(key, out var status) ? status : null;
    }

    public void CancelAll() => Volatile.Read(ref _batchCancellation)?.Cancel();

    public void Cancel(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (StatusKey(item) is { } key && _itemCancellation.TryGetValue(key, out var cancellation))
            cancellation.Cancel();
    }

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

    public bool IsOutdated(CollectionItem item) =>
        item.Location is RemoteItemLocation && Downloaded(item) is { } cell && cell.IsOlderThan(item);

    public int? LocalEditionOf(CollectionItem item) => Downloaded(item)?.Edition;

    public DateTimeOffset? LocalPublishedAtOf(CollectionItem item) => Downloaded(item)?.PublishedAt;

    public bool CanDownload(CollectionItem item) =>
        item.Location is RemoteItemLocation remote
        && item.Status != CollectionItemStatus.Cancelled
        && _downloaderFor(remote) is not null;

    public async Task<LibraryDownloadResult> DownloadAsync(
        IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
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
        var totalBytes = Math.Max(1, sizes.Sum());
        foreach (var item in toDownload)
        {
            _status[StatusKey(item)!] = new LibraryDownloadItemStatus(
                LibraryDownloadItemState.Queued, 0, ((RemoteItemLocation)item.Location).SizeBytes);
        }

        Volatile.Write(ref _progress, new LibraryDownloadProgress(0, 0, toDownload.Length, 0, sizes.Sum()));
        RaiseChanged();

        var handle = _notifications?.Create(Strings.Toast_LibraryDownloadingTitle)
            .WithSeverity(NotificationSeverity.Info)
            .WithContent(Describe(0, toDownload.Length, totalBytes))
            .AsProgress(0)
            .Persistent()
            .WithAction(Strings.Button_Cancel, cancellation.Cancel, dismissOnInvoke: false)
            .Show();

        var done = 0;
        var failed = 0;
        long completedBytes = 0;
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
            var bytesDone = Interlocked.Add(ref completedBytes, sizes[index]);
            UpdateProgress(Volatile.Read(ref done), Volatile.Read(ref failed), received, sizes);
            handle?.Report(Math.Min(1.0, bytesDone / (double)totalBytes));
            handle?.Update(message: Describe(Volatile.Read(ref done) + Volatile.Read(ref failed), toDownload.Length, totalBytes));
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

        if (handle is not null && !handle.IsDismissed)
        {
            handle.ClearProgress();
            handle.SetActions();
            var severity = failed > 0 ? NotificationSeverity.Warning : NotificationSeverity.Success;
            handle.Update(
                title: cancelled ? Strings.Toast_LibraryDownloadCancelledTitle : Strings.Toast_LibraryDownloadedTitle,
                message: string.Format(CultureInfo.CurrentCulture, Strings.Toast_LibraryDownloadedFormat, done, failed),
                severity: severity);
            handle.ScheduleAutoDismiss(NotificationService.DefaultDelayFor(severity));
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

    private static string Describe(int finished, int total, long totalBytes) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Toast_LibraryDownloadingFormat,
            finished, total, LibraryItemViewModel.FormatBytes(totalBytes));
}
