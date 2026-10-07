using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>Downloads online library items and resolves their downloaded copies.</summary>
internal interface ILibraryDownloader : ILibraryLocalCopies
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

    /// <summary>True when <paramref name="item"/> can be downloaded.</summary>
    bool CanDownload(CollectionItem item);

    /// <summary>Downloads the downloadable items among <paramref name="items"/>, reporting progress in a notification.</summary>
    Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default);
}

/// <summary>
/// The viewer's <see cref="ILibraryDownloader"/>: the host-neutral
/// <see cref="LibraryDownloads"/> (issues #655, #670, #792) plus a cancellable
/// progress notification for each batch.
/// </summary>
internal sealed class LibraryDownloadService : ILibraryDownloader
{
    /// <summary>How many cells download at once.</summary>
    internal const int MaxConcurrentDownloads = LibraryDownloads.MaxConcurrentDownloads;

    private readonly LibraryDownloads _downloads;
    private readonly INotificationService? _notifications;

    /// <summary>Creates a service that downloads every cell with <paramref name="downloader"/>.</summary>
    public LibraryDownloadService(EncCellDownloader downloader, INotificationService? notifications = null)
        : this(new LibraryDownloads(downloader), notifications)
    {
    }

    /// <summary>
    /// Creates a service that picks a downloader (and so a managed folder)
    /// per download location, e.g. one per provider or per
    /// <see cref="RemoteItemLocation.DownloadFolder"/>; <see langword="null"/>
    /// means the location is not downloadable.
    /// </summary>
    public LibraryDownloadService(
        Func<RemoteItemLocation, EncCellDownloader?> downloaderFor, INotificationService? notifications = null)
        : this(new LibraryDownloads(downloaderFor), notifications)
    {
    }

    /// <summary>Creates a service over <paramref name="downloads"/>.</summary>
    public LibraryDownloadService(LibraryDownloads downloads, INotificationService? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        _downloads = downloads;
        _notifications = notifications;
    }

    /// <summary>The host-neutral downloads behind the service.</summary>
    public LibraryDownloads Downloads => _downloads;

    public event EventHandler? Changed
    {
        add => _downloads.Changed += value;
        remove => _downloads.Changed -= value;
    }

    public event EventHandler? ProgressChanged
    {
        add => _downloads.ProgressChanged += value;
        remove => _downloads.ProgressChanged -= value;
    }

    public LibraryDownloadProgress? Progress => _downloads.Progress;

    public LibraryDownloadItemStatus? StatusOf(CollectionItem item) => _downloads.StatusOf(item);

    public void CancelAll() => _downloads.CancelAll();

    public void Cancel(CollectionItem item) => _downloads.Cancel(item);

    public CollectionItem Localize(CollectionItem item) => _downloads.Localize(item);

    public bool IsOutdated(CollectionItem item) => _downloads.IsOutdated(item);

    public int? LocalEditionOf(CollectionItem item) => _downloads.LocalEditionOf(item);

    public DateTimeOffset? LocalPublishedAtOf(CollectionItem item) => _downloads.LocalPublishedAtOf(item);

    public bool CanDownload(CollectionItem item) => _downloads.CanDownload(item);

    public async Task<LibraryDownloadResult> DownloadAsync(
        IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (_notifications is null)
            return await _downloads.DownloadAsync(items, null, cancellationToken).ConfigureAwait(false);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        INotificationHandle? handle = null;
        var progress = new SyncProgress(p =>
        {
            var totalBytes = Math.Max(1, p.BytesTotal);
            if (handle is null)
            {
                handle = _notifications.Create(Strings.Toast_LibraryDownloadingTitle)
                    .WithSeverity(NotificationSeverity.Info)
                    .WithContent(Describe(0, p.Total, totalBytes))
                    .AsProgress(0)
                    .Persistent()
                    .WithAction(Strings.Button_Cancel, cancellation.Cancel, dismissOnInvoke: false)
                    .Show();
                return;
            }

            handle.Report(p.Fraction);
            handle.Update(message: Describe(p.Completed + p.Failed, p.Total, totalBytes));
        });

        var result = await _downloads.DownloadAsync(items, progress, cancellation.Token).ConfigureAwait(false);

        if (handle is not null && !handle.IsDismissed)
        {
            handle.ClearProgress();
            handle.SetActions();
            var severity = result.Failed > 0 ? NotificationSeverity.Warning : NotificationSeverity.Success;
            handle.Update(
                title: result.Cancelled ? Strings.Toast_LibraryDownloadCancelledTitle : Strings.Toast_LibraryDownloadedTitle,
                message: string.Format(CultureInfo.CurrentCulture, Strings.Toast_LibraryDownloadedFormat, result.Downloaded, result.Failed),
                severity: severity);
            handle.ScheduleAutoDismiss(NotificationService.DefaultDelayFor(severity));
        }

        return result;
    }

    private static string Describe(int finished, int total, long totalBytes) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Toast_LibraryDownloadingFormat,
            finished, total, LibraryItemViewModel.FormatBytes(totalBytes));

    /// <summary>Reports on the reporting thread, as the notification handle is thread-safe.</summary>
    private sealed class SyncProgress(Action<LibraryDownloadProgress> report) : IProgress<LibraryDownloadProgress>
    {
        public void Report(LibraryDownloadProgress value) => report(value);
    }
}
