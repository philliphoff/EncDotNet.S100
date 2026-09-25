using System.Collections.Concurrent;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Noaa;
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
/// Downloads NOAA ENC cells from the library (issue #655) into the viewer's
/// managed download folder, a few at a time, with a cancellable progress
/// notification; afterwards the cells behave as local items.
/// </summary>
internal sealed class LibraryDownloadService : ILibraryDownloader
{
    /// <summary>How many cells download at once.</summary>
    internal const int MaxConcurrentDownloads = 3;

    private readonly NoaaEncCellDownloader _downloader;
    private readonly INotificationService? _notifications;
    private readonly ConcurrentDictionary<string, DownloadedCell?> _known = new(StringComparer.OrdinalIgnoreCase);

    public LibraryDownloadService(NoaaEncCellDownloader downloader, INotificationService? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        _downloader = downloader;
        _notifications = notifications;
    }

    public event EventHandler? Changed;

    public CollectionItem Localize(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Location is RemoteItemLocation && Downloaded(item) is { } cell
            ? item with { Location = cell.Location }
            : item;
    }

    public bool IsOutdated(CollectionItem item) =>
        item.Location is RemoteItemLocation && Downloaded(item) is { } cell && cell.IsOlderThan(item);

    public bool CanDownload(CollectionItem item) =>
        item.Location is RemoteItemLocation && item.Status != CollectionItemStatus.Cancelled;

    public async Task<LibraryDownloadResult> DownloadAsync(
        IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var toDownload = items.Where(CanDownload).DistinctBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToArray();
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
                var cell = await _downloader.DownloadAsync(item, cancellationToken: cancellation.Token).ConfigureAwait(false);
                _known[item.Name] = cell;
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

    private DownloadedCell? Downloaded(CollectionItem item) =>
        _known.GetOrAdd(item.Name, name => _downloader.TryGetDownloaded(name));

    private static string Describe(int finished, int total, long totalBytes) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Toast_LibraryDownloadingFormat,
            finished, total, LibraryItemViewModel.FormatBytes(totalBytes));
}
