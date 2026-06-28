using System;
using System.Net.Http;
using System.Threading.Tasks;
using EncDotNet.S100.Viewer.Services.Updates;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Live integration test exercising the real <see cref="GitHubReleaseClient"/>
/// against the public GitHub Releases API for this repository (issue #379).
/// Network-gated: set <c>S100_UPDATE_INTEGRATION=1</c> to opt in, so CI and
/// offline runs skip it. The unit-level mapping is covered separately by the
/// non-network <c>GitHubReleaseClient.Parse</c> tests.
/// </summary>
public sealed class GitHubReleaseClientIntegrationTests
{
    [SkippableFact]
    public async Task GetLatestReleaseAsync_HitsRealRepository_ReturnsPublishedRelease()
    {
        Skip.If(
            Environment.GetEnvironmentVariable("S100_UPDATE_INTEGRATION") != "1",
            "S100_UPDATE_INTEGRATION not set; skipping live GitHub call.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var client = new GitHubReleaseClient(http);

        var release = await client.GetLatestReleaseAsync();

        Assert.NotNull(release);
        Assert.False(string.IsNullOrWhiteSpace(release!.TagName));
        Assert.Contains(
            $"{GitHubReleaseClient.RepositoryOwner}/{GitHubReleaseClient.RepositoryName}",
            release.HtmlUrl);
    }

    [SkippableFact]
    public async Task UpdateService_WithOldVersion_ReportsUpdateAvailable()
    {
        Skip.If(
            Environment.GetEnvironmentVariable("S100_UPDATE_INTEGRATION") != "1",
            "S100_UPDATE_INTEGRATION not set; skipping live GitHub call.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var client = new GitHubReleaseClient(http);
        var versions = new FixedVersionProvider("0.1.0");
        var settings = new ViewerSettings
        {
            SettingsFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"about-{Guid.NewGuid():N}.json"),
            IsReadOnly = true,
        };
        var service = new UpdateService(client, versions, settings, TimeProvider.System);

        var status = await service.CheckForUpdatesAsync(force: true);

        Assert.Equal(UpdateAvailability.UpdateAvailable, status.Availability);
        Assert.False(string.IsNullOrWhiteSpace(status.LatestVersion));
    }

    private sealed class FixedVersionProvider(string version) : IAppVersionProvider
    {
        public AppVersionInfo Current { get; } = new(version, version, null, null);
    }
}
