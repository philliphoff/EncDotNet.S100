using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.Services.Updates;
using EncDotNet.S100.Viewer.Tests.Notifications;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class UpdateNotificationCoordinatorTests
{
    private sealed class StubUpdateService(UpdateStatus status) : IUpdateService
    {
        public int CheckCount { get; private set; }

        public string? SkippedVersion { get; private set; }

        public bool? ChecksEnabled { get; private set; }

        public bool UpdateChecksEnabled => ChecksEnabled ?? true;

        public Task<UpdateStatus> CheckForUpdatesAsync(
            bool force,
            CancellationToken cancellationToken = default)
        {
            CheckCount++;
            return Task.FromResult(status);
        }

        public void SkipVersion(string version) => SkippedVersion = version;

        public void SetUpdateChecksEnabled(bool enabled) => ChecksEnabled = enabled;
    }

    private sealed class StubUrlOpener : IUrlOpener
    {
        public string? OpenedUrl { get; private set; }

        public void Open(string url) => OpenedUrl = url;
    }

    [Fact]
    public async Task CheckAndNotify_FreshUpdate_PublishesActionableNotification()
    {
        var (coordinator, notifications, service, _) = Create(AvailableStatus());

        await coordinator.CheckAndNotifyAsync();

        var notification = Assert.Single(notifications.Active);
        Assert.Equal(NotificationSeverity.Info, notification.Severity);
        Assert.True(notification.IsPersistent);
        Assert.Equal("Viewer update available", notification.Title);
        Assert.Contains("2.5.0", notification.Message);
        Assert.Equal(
            new[] { "View release", "Remind me later", "Skip this version", "Stop checking" },
            notification.Actions.Select(action => action.Label));
        Assert.Equal(1, service.CheckCount);
    }

    [Theory]
    [InlineData((int)UpdateAvailability.Disabled, false, false)]
    [InlineData((int)UpdateAvailability.UpToDate, false, false)]
    [InlineData((int)UpdateAvailability.CheckFailed, false, false)]
    [InlineData((int)UpdateAvailability.UpdateAvailable, true, false)]
    [InlineData((int)UpdateAvailability.UpdateAvailable, false, true)]
    public async Task CheckAndNotify_IneligibleResult_RemainsSilent(
        int availabilityValue,
        bool isSkipped,
        bool isCached)
    {
        var availability = (UpdateAvailability)availabilityValue;
        var status = new UpdateStatus
        {
            Availability = availability,
            LatestVersion = availability == UpdateAvailability.UpdateAvailable ? "2.5.0" : null,
            IsSkipped = isSkipped,
            IsCached = isCached,
        };
        var (coordinator, notifications, _, _) = Create(status);

        await coordinator.CheckAndNotifyAsync();

        Assert.Empty(notifications.Active);
    }

    [Fact]
    public async Task CheckAndNotify_RepeatedCall_ChecksAndPublishesOnce()
    {
        var (coordinator, notifications, service, _) = Create(AvailableStatus());

        await coordinator.CheckAndNotifyAsync();
        await coordinator.CheckAndNotifyAsync();

        Assert.Equal(1, service.CheckCount);
        Assert.Single(notifications.Active);
    }

    [Fact]
    public async Task ViewRelease_OpensReleaseUrlAndDismisses()
    {
        var (coordinator, notifications, _, opener) = Create(AvailableStatus());
        await coordinator.CheckAndNotifyAsync();

        notifications.Active.Single().Actions[0].Command.Execute(null);

        Assert.Equal("https://example/v2.5.0", opener.OpenedUrl);
        Assert.Empty(notifications.Active);
    }

    [Fact]
    public async Task ViewRelease_MissingReleaseUrl_OpensReleasesPage()
    {
        var status = new UpdateStatus
        {
            Availability = UpdateAvailability.UpdateAvailable,
            LatestVersion = "2.5.0",
            LatestRelease = new GitHubRelease(
                "v2.5.0",
                "Version 2.5.0",
                null,
                null,
                DateTimeOffset.UtcNow,
                false,
                null),
        };
        var (coordinator, notifications, _, opener) = Create(status);
        await coordinator.CheckAndNotifyAsync();

        notifications.Active.Single().Actions[0].Command.Execute(null);

        Assert.Equal(GitHubReleaseClient.ReleasesPageUrl, opener.OpenedUrl);
    }

    [Fact]
    public async Task RemindLater_DismissesWithoutPersistingSuppression()
    {
        var (coordinator, notifications, service, _) = Create(AvailableStatus());
        await coordinator.CheckAndNotifyAsync();

        notifications.Active.Single().Actions[1].Command.Execute(null);

        Assert.Null(service.SkippedVersion);
        Assert.Null(service.ChecksEnabled);
        Assert.Empty(notifications.Active);
    }

    [Fact]
    public async Task SkipVersion_PersistsVersionAndDismisses()
    {
        var (coordinator, notifications, service, _) = Create(AvailableStatus());
        await coordinator.CheckAndNotifyAsync();

        notifications.Active.Single().Actions[2].Command.Execute(null);

        Assert.Equal("2.5.0", service.SkippedVersion);
        Assert.Empty(notifications.Active);
    }

    [Fact]
    public async Task StopChecking_DisablesChecksAndDismisses()
    {
        var (coordinator, notifications, service, _) = Create(AvailableStatus());
        await coordinator.CheckAndNotifyAsync();

        notifications.Active.Single().Actions[3].Command.Execute(null);

        Assert.False(service.ChecksEnabled);
        Assert.Empty(notifications.Active);
    }

    private static UpdateStatus AvailableStatus() => new()
    {
        Availability = UpdateAvailability.UpdateAvailable,
        LatestVersion = "2.5.0",
        LatestRelease = new GitHubRelease(
            "v2.5.0",
            "Version 2.5.0",
            "https://example/v2.5.0",
            null,
            DateTimeOffset.UtcNow,
            false,
            null),
    };

    private static (
        UpdateNotificationCoordinator Coordinator,
        NotificationService Notifications,
        StubUpdateService UpdateService,
        StubUrlOpener UrlOpener) Create(UpdateStatus status)
    {
        var notifications = new NotificationService(
            new ImmediateUiDispatcher(),
            TimeProvider.System);
        var updateService = new StubUpdateService(status);
        var urlOpener = new StubUrlOpener();
        var coordinator = new UpdateNotificationCoordinator(
            updateService,
            notifications,
            urlOpener);
        return (coordinator, notifications, updateService, urlOpener);
    }
}
