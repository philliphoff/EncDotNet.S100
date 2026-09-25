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

/// <summary>Downloads online library items and resolves their downloaded copies.</summary>
internal interface ILibraryDownloader
{
    /// <summary>Raised when a download completes (a copy appeared or changed).</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Returns <paramref name="item"/> with a local location when it is an
    /// online item that has been downloaded; otherwise the item itself.
    /// </summary>
    CollectionItem Localize(CollectionItem item);

    /// <summary>True when the downloaded copy of <paramref name="item"/> is an older edition or update.</summary>
    bool IsOutdated(CollectionItem item);

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

    private readonly Func<RemoteItemLocation, EncCellDownloader?> _downloaderFor;
    private readonly INotificationService? _notifications;
    private readonly ConcurrentDictionary<string, DownloadedCell?> _known = new(StringComparer.OrdinalIgnoreCase);

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

    public bool CanDownload(CollectionItem item) =>
        item.Location is RemoteItemLocation remote
        && item.Status != CollectionItemStatus.Cancelled
        && _downloaderFor(remote) is not null;

    public async Task<LibraryDownloadResult> DownloadAsync(
        IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        // A package downloads once however many of its cells are asked for.
        var toDownload = items.Where(CanDownload).DistinctBy(DownloadName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (toDownload.Length == 0)
            return new LibraryDownloadResult(0, 0, false);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var totalBytes = Math.Max(1, toDownload.Sum(i => ((RemoteItemLocation)i.Location).SizeBytes ?? 0));
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
        using var gate = new SemaphoreSlim(MaxConcurrentDownloads);

        async Task DownloadOneAsync(CollectionItem item)
        {
            await gate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                var downloader = _downloaderFor((RemoteItemLocation)item.Location)!;
                var cell = await downloader.DownloadAsync(item, cancellationToken: cancellation.Token).ConfigureAwait(false);
                _known[Key(downloader, DownloadName(item))] = cell;
                Interlocked.Increment(ref done);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref failed);
            }
            finally
            {
                gate.Release();
            }

            var bytes = Interlocked.Add(ref completedBytes, ((RemoteItemLocation)item.Location).SizeBytes ?? 0);
            handle?.Report(Math.Min(1.0, bytes / (double)totalBytes));
            handle?.Update(message: Describe(Volatile.Read(ref done) + Volatile.Read(ref failed), toDownload.Length, totalBytes));
            Changed?.Invoke(this, EventArgs.Empty);
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
