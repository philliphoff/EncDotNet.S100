namespace EncDotNet.S100.Cli.Infrastructure.Updates;

/// <summary>
/// Checks GitHub at most once per day and reports every known newer release.
/// </summary>
internal sealed class CliUpdateChecker : ICliUpdateChecker
{
    public static readonly TimeSpan ThrottleWindow = TimeSpan.FromHours(24);

    private readonly CliVersionInfo _currentVersion;
    private readonly ICliReleaseClient _releaseClient;
    private readonly ICliUpdateCache _cache;
    private readonly TimeProvider _timeProvider;

    public CliUpdateChecker(
        CliVersionInfo currentVersion,
        ICliReleaseClient releaseClient,
        ICliUpdateCache cache,
        TimeProvider timeProvider)
    {
        _currentVersion = currentVersion
            ?? throw new ArgumentNullException(nameof(currentVersion));
        _releaseClient = releaseClient
            ?? throw new ArgumentNullException(nameof(releaseClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<CliUpdateNotice?> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentVersion.IsDevelopmentBuild)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var cached = await _cache.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (cached is not null && now - cached.CheckedAtUtc < ThrottleWindow)
        {
            return CreateNotice(cached);
        }

        var release = await _releaseClient
            .GetLatestReleaseAsync(cancellationToken)
            .ConfigureAwait(false);
        CliUpdateCacheEntry refreshed;
        if (release is null)
        {
            refreshed = cached is null
                ? new CliUpdateCacheEntry(now, null, null)
                : cached with { CheckedAtUtc = now };
        }
        else
        {
            var latestVersion = NormalizeTag(release.TagName);
            refreshed = new CliUpdateCacheEntry(
                now,
                latestVersion,
                release.HtmlUrl ?? GitHubReleaseClient.ReleasesPageUrl);
        }

        await _cache.SaveAsync(refreshed, cancellationToken).ConfigureAwait(false);
        return CreateNotice(refreshed);
    }

    private CliUpdateNotice? CreateNotice(CliUpdateCacheEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.LatestVersion)
            || !ReleaseVersion.IsNewer(entry.LatestVersion, _currentVersion.Version))
        {
            return null;
        }

        return new CliUpdateNotice(
            _currentVersion.Version,
            entry.LatestVersion,
            entry.ReleaseUrl ?? GitHubReleaseClient.ReleasesPageUrl);
    }

    private static string NormalizeTag(string tag)
    {
        var trimmed = tag.Trim();
        return trimmed.StartsWith('v') || trimmed.StartsWith('V')
            ? trimmed[1..]
            : trimmed;
    }
}
