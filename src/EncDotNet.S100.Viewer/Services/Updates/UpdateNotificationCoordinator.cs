using System.Globalization;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.Services.Updates;

/// <summary>
/// Runs the Viewer's non-forced startup update check and publishes the
/// actionable notification for a fresh, unskipped release.
/// </summary>
internal interface IUpdateNotificationCoordinator
{
    /// <summary>
    /// Checks once for this application window and notifies when an eligible
    /// release is available.
    /// </summary>
    Task CheckAndNotifyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IUpdateNotificationCoordinator"/> implementation.
/// </summary>
internal sealed class UpdateNotificationCoordinator : IUpdateNotificationCoordinator
{
    private readonly IUpdateService _updateService;
    private readonly INotificationService _notifications;
    private readonly IUrlOpener _urlOpener;
    private int _started;

    public UpdateNotificationCoordinator(
        IUpdateService updateService,
        INotificationService notifications,
        IUrlOpener urlOpener)
    {
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _urlOpener = urlOpener ?? throw new ArgumentNullException(nameof(urlOpener));
    }

    /// <inheritdoc />
    public async Task CheckAndNotifyAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        UpdateStatus status;
        try
        {
            status = await _updateService
                .CheckForUpdatesAsync(force: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (status is not
            {
                Availability: UpdateAvailability.UpdateAvailable,
                IsSkipped: false,
                IsCached: false,
                LatestVersion.Length: > 0,
            })
        {
            return;
        }

        var version = status.LatestVersion;
        var releaseUrl = status.LatestRelease?.HtmlUrl ?? GitHubReleaseClient.ReleasesPageUrl;

        _notifications.Create(Strings.Toast_UpdateAvailableTitle)
            .WithSeverity(NotificationSeverity.Info)
            .WithContent(string.Format(
                CultureInfo.CurrentCulture,
                Strings.Toast_UpdateAvailableBodyFormat,
                version))
            .WithAction(
                Strings.Toast_UpdateViewRelease,
                () => _urlOpener.Open(releaseUrl),
                isPrimary: true)
            .WithAction(Strings.Toast_UpdateRemindLater, static () => { })
            .WithAction(
                Strings.Toast_UpdateSkipVersion,
                () => _updateService.SkipVersion(version))
            .WithAction(
                Strings.Toast_UpdateStopChecking,
                () => _updateService.SetUpdateChecksEnabled(false))
            .Persistent()
            .Show();
    }
}
