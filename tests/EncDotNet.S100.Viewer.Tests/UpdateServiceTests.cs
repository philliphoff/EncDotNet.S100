using System.Text.Json;
using EncDotNet.S100.Viewer.Services.Updates;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the update-notification logic that backs the About dialog
/// (issue #379): version parsing, SemVer comparison, the
/// <see cref="UpdateService"/> decision/skip/throttle behaviour, and the
/// GitHub release JSON mapping.
/// </summary>
public sealed class UpdateServiceTests
{
    private sealed class FakeReleaseClient : IGitHubReleaseClient
    {
        private readonly Func<GitHubRelease?> _factory;
        public int CallCount { get; private set; }

        public FakeReleaseClient(GitHubRelease? release) => _factory = () => release;
        public FakeReleaseClient(Func<GitHubRelease?> factory) => _factory = factory;

        public Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_factory());
        }
    }

    private sealed class FakeVersionProvider(AppVersionInfo current) : IAppVersionProvider
    {
        public AppVersionInfo Current { get; } = current;
    }

    private sealed class BlockingReleaseClient(GitHubRelease release) : IGitHubReleaseClient
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _continue =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public int CallCount => _callCount;

        public Task Entered => _entered.Task;

        public async Task<GitHubRelease?> GetLatestReleaseAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            _entered.TrySetResult();
            await _continue.Task.WaitAsync(cancellationToken);
            return release;
        }

        public void Complete() => _continue.TrySetResult();
    }

    private static AppVersionInfo Version(string version) => new(version, version, null, null);

    private static ViewerSettings InMemorySettings() => new()
    {
        SettingsFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"about-{Guid.NewGuid():N}.json"),
        IsReadOnly = true,
    };

    private static GitHubRelease Release(
        string tag,
        string? body = null,
        DateTimeOffset? publishedAt = null,
        long? assetBytes = null) =>
        new(tag, tag, $"https://example/{tag}", body, publishedAt, false, assetBytes);

    private static UpdateService CreateService(
        IGitHubReleaseClient client,
        string currentVersion,
        ViewerSettings settings,
        FakeTimeProvider time) =>
        new(client, new FakeVersionProvider(Version(currentVersion)), settings, time);

    // ---- ReleaseVersion ---------------------------------------------------

    [Theory]
    [InlineData("2.5.0", "2.4.1", true)]
    [InlineData("v2.5.0", "2.4.1", true)]
    [InlineData("2.4.1", "2.4.1", false)]
    [InlineData("2.4.0", "2.4.1", false)]
    [InlineData("2.5", "2.4.9", true)]
    [InlineData("2.5.0-rc.1", "2.4.1", true)]
    [InlineData("garbage", "2.4.1", false)]
    [InlineData("2.5.0", "", false)]
    public void IsNewer_ComparesSemanticVersions(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, ReleaseVersion.IsNewer(candidate, current));
    }

    // ---- Version provider parsing ----------------------------------------

    [Theory]
    [InlineData("2.4.1+a1f9c20abcdef", "2.4.1", "a1f9c20")]
    [InlineData("2.4.1", "2.4.1", null)]
    [InlineData("0.0.0-dev", "0.0.0-dev", null)]
    public void ParseInformationalVersion_SplitsVersionAndShortSha(string input, string version, string? sha)
    {
        var (parsedVersion, parsedSha) = AssemblyAppVersionProvider.ParseInformationalVersion(input);
        Assert.Equal(version, parsedVersion);
        Assert.Equal(sha, parsedSha);
    }

    [Theory]
    [InlineData("0.0.0-dev", true)]
    [InlineData("0.0.0", true)]
    [InlineData("2.4.1", false)]
    public void IsDevelopmentBuild_DetectsDefaultVersion(string version, bool expected)
    {
        Assert.Equal(expected, Version(version).IsDevelopmentBuild);
    }

    // ---- UpdateService decisions -----------------------------------------

    [Fact]
    public async Task CheckForUpdates_DevBuild_ReturnsDisabledWithoutNetwork()
    {
        var client = new FakeReleaseClient(Release("v9.9.9"));
        var time = new FakeTimeProvider();
        var service = CreateService(client, "0.0.0-dev", InMemorySettings(), time);

        var status = await service.CheckForUpdatesAsync(force: true);

        Assert.Equal(UpdateAvailability.Disabled, status.Availability);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task CheckForUpdates_DevBuildWithForceEnv_PerformsLiveCheck()
    {
        var prior = Environment.GetEnvironmentVariable("S100_UPDATE_FORCE");
        Environment.SetEnvironmentVariable("S100_UPDATE_FORCE", "1");
        try
        {
            var client = new FakeReleaseClient(Release("v9.9.9"));
            var time = new FakeTimeProvider();
            var service = CreateService(client, "0.0.0-dev", InMemorySettings(), time);

            var status = await service.CheckForUpdatesAsync(force: true);

            Assert.Equal(UpdateAvailability.UpdateAvailable, status.Availability);
            Assert.Equal(1, client.CallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("S100_UPDATE_FORCE", prior);
        }
    }

    [Fact]
    public async Task CheckForUpdates_NewerRelease_ReportsUpdateAvailable()
    {
        var client = new FakeReleaseClient(Release("v2.5.0", body: "* New thing"));
        var time = new FakeTimeProvider();
        var service = CreateService(client, "2.4.1", InMemorySettings(), time);

        var status = await service.CheckForUpdatesAsync(force: true);

        Assert.Equal(UpdateAvailability.UpdateAvailable, status.Availability);
        Assert.Equal("2.5.0", status.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_SameVersion_ReportsUpToDate()
    {
        var client = new FakeReleaseClient(Release("v2.4.1"));
        var time = new FakeTimeProvider();
        var service = CreateService(client, "2.4.1", InMemorySettings(), time);

        var status = await service.CheckForUpdatesAsync(force: true);

        Assert.Equal(UpdateAvailability.UpToDate, status.Availability);
    }

    [Fact]
    public async Task CheckForUpdates_NullRelease_ReportsCheckFailed()
    {
        var client = new FakeReleaseClient((GitHubRelease?)null);
        var time = new FakeTimeProvider();
        var service = CreateService(client, "2.4.1", InMemorySettings(), time);

        var status = await service.CheckForUpdatesAsync(force: true);

        Assert.Equal(UpdateAvailability.CheckFailed, status.Availability);
    }

    [Fact]
    public async Task CheckForUpdates_ClientThrows_ReportsCheckFailed()
    {
        var client = new FakeReleaseClient(() => throw new InvalidOperationException("boom"));
        var time = new FakeTimeProvider();
        var service = CreateService(client, "2.4.1", InMemorySettings(), time);

        var status = await service.CheckForUpdatesAsync(force: true);

        Assert.Equal(UpdateAvailability.CheckFailed, status.Availability);
    }

    [Fact]
    public async Task SkipVersion_KeepsUpdateVisibleButMarksSkipped_LaterReleaseUnskipped()
    {
        var settings = InMemorySettings();
        var time = new FakeTimeProvider();

        // 2.5.0 is available; user skips it.
        var service = CreateService(new FakeReleaseClient(Release("v2.5.0")), "2.4.1", settings, time);
        var available = await service.CheckForUpdatesAsync(force: true);
        Assert.Equal(UpdateAvailability.UpdateAvailable, available.Availability);
        Assert.False(available.IsSkipped);

        service.SkipVersion("2.5.0");
        Assert.Equal("2.5.0", settings.SkippedUpdateVersion);

        // Re-checking 2.5.0 still reports it as available (the dialog stays
        // truthful), but flagged as skipped so notifications stay muted.
        var afterSkip = await service.CheckForUpdatesAsync(force: true);
        Assert.Equal(UpdateAvailability.UpdateAvailable, afterSkip.Availability);
        Assert.True(afterSkip.IsSkipped);

        // A later release is available and not skipped.
        var newerService = CreateService(new FakeReleaseClient(Release("v2.6.0")), "2.4.1", settings, time);
        var newer = await newerService.CheckForUpdatesAsync(force: true);
        Assert.Equal(UpdateAvailability.UpdateAvailable, newer.Availability);
        Assert.False(newer.IsSkipped);
        Assert.Equal("2.6.0", newer.LatestVersion);
    }

    [Fact]
    public async Task SetUpdateChecksEnabled_False_DisablesChecks()
    {
        var settings = InMemorySettings();
        var time = new FakeTimeProvider();
        var client = new FakeReleaseClient(Release("v2.5.0"));
        var service = CreateService(client, "2.4.1", settings, time);

        service.SetUpdateChecksEnabled(false);

        Assert.False(settings.UpdateCheckEnabled);
        var status = await service.CheckForUpdatesAsync(force: true);
        Assert.Equal(UpdateAvailability.Disabled, status.Availability);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task CheckForUpdates_Throttles_NonForcedChecksWithinWindow()
    {
        var settings = InMemorySettings();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = new FakeReleaseClient(Release("v2.5.0"));
        var service = CreateService(client, "2.4.1", settings, time);

        await service.CheckForUpdatesAsync(force: true);
        Assert.Equal(1, client.CallCount);

        // Within the throttle window, a non-forced check reuses the cache.
        time.Advance(TimeSpan.FromHours(1));
        await service.CheckForUpdatesAsync(force: false);
        Assert.Equal(1, client.CallCount);

        // Past the window, a non-forced check hits the network again.
        time.Advance(UpdateService.ThrottleWindow);
        await service.CheckForUpdatesAsync(force: false);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task CheckForUpdates_PersistedRecentUpdate_RehydratesWithoutNetwork()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = InMemorySettings();
        settings.LastUpdateCheckUtc = now;
        settings.LastKnownLatestVersion = "2.5.0";
        settings.SkippedUpdateVersion = "2.5.0";
        var client = new FakeReleaseClient(Release("v9.9.9"));
        var service = CreateService(
            client,
            "2.4.1",
            settings,
            new FakeTimeProvider(now.AddHours(1)));

        var status = await service.CheckForUpdatesAsync(force: false);

        Assert.Equal(UpdateAvailability.UpdateAvailable, status.Availability);
        Assert.Equal("2.5.0", status.LatestVersion);
        Assert.True(status.IsSkipped);
        Assert.True(status.IsCached);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task CheckForUpdates_PersistedRecentFailure_RehydratesWithoutNetwork()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = InMemorySettings();
        settings.LastUpdateCheckUtc = now;
        settings.LastKnownLatestVersion = null;
        var client = new FakeReleaseClient(Release("v9.9.9"));
        var service = CreateService(
            client,
            "2.4.1",
            settings,
            new FakeTimeProvider(now.AddHours(1)));

        var status = await service.CheckForUpdatesAsync(force: false);

        Assert.Equal(UpdateAvailability.CheckFailed, status.Availability);
        Assert.True(status.IsCached);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task CheckForUpdates_ForceBypassesPersistedThrottle()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = InMemorySettings();
        settings.LastUpdateCheckUtc = now;
        settings.LastKnownLatestVersion = "2.5.0";
        var client = new FakeReleaseClient(Release("v2.6.0"));
        var service = CreateService(
            client,
            "2.4.1",
            settings,
            new FakeTimeProvider(now.AddHours(1)));

        var status = await service.CheckForUpdatesAsync(force: true);

        Assert.Equal(UpdateAvailability.UpdateAvailable, status.Availability);
        Assert.Equal("2.6.0", status.LatestVersion);
        Assert.False(status.IsCached);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task CheckForUpdates_ExpiredPersistedThrottle_PerformsNetworkCheck()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = InMemorySettings();
        settings.LastUpdateCheckUtc = now - UpdateService.ThrottleWindow - TimeSpan.FromMinutes(1);
        settings.LastKnownLatestVersion = "2.5.0";
        var client = new FakeReleaseClient(Release("v2.6.0"));
        var service = CreateService(client, "2.4.1", settings, new FakeTimeProvider(now));

        var status = await service.CheckForUpdatesAsync(force: false);

        Assert.Equal("2.6.0", status.LatestVersion);
        Assert.False(status.IsCached);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task CheckForUpdates_ConcurrentNonForcedCalls_ShareNetworkCheck()
    {
        var client = new BlockingReleaseClient(Release("v2.5.0"));
        var service = CreateService(
            client,
            "2.4.1",
            InMemorySettings(),
            new FakeTimeProvider(DateTimeOffset.UtcNow));

        var first = service.CheckForUpdatesAsync(force: false);
        await client.Entered;
        var second = service.CheckForUpdatesAsync(force: false);
        client.Complete();

        var statuses = await Task.WhenAll(first, second);

        Assert.Equal(1, client.CallCount);
        Assert.Contains(statuses, status => !status.IsCached);
        Assert.Contains(statuses, status => status.IsCached);
    }

    // ---- JSON mapping ----------------------------------------------------

    [Fact]
    public void GitHubReleaseClient_Parse_MapsCoreFieldsAndLargestAsset()
    {
        const string json = """
        {
            "tag_name": "v2.5.0",
            "name": "v2.5.0",
            "html_url": "https://github.com/owner/repo/releases/tag/v2.5.0",
            "body": "* Item",
            "published_at": "2026-06-25T12:00:00Z",
            "prerelease": false,
            "assets": [
                { "size": 1048576 },
                { "size": 50331648 }
            ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var release = GitHubReleaseClient.Parse(doc.RootElement);

        Assert.NotNull(release);
        Assert.Equal("v2.5.0", release!.TagName);
        Assert.Equal("https://github.com/owner/repo/releases/tag/v2.5.0", release.HtmlUrl);
        Assert.False(release.IsPrerelease);
        Assert.Equal(50331648, release.LargestAssetBytes);
        Assert.Equal(new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero), release.PublishedAt);
    }

    [Fact]
    public void GitHubReleaseClient_Parse_MissingTag_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("""{ "name": "no tag" }""");
        Assert.Null(GitHubReleaseClient.Parse(doc.RootElement));
    }
}
