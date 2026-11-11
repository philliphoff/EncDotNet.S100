namespace EncDotNet.S100.Cli.Infrastructure.Updates;

/// <summary>
/// A newer-release notice suitable for standard error.
/// </summary>
internal sealed record CliUpdateNotice
{
    public CliUpdateNotice(string currentVersion, string latestVersion, string releaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(latestVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseUrl);

        CurrentVersion = currentVersion;
        LatestVersion = latestVersion;
        ReleaseUrl = releaseUrl;
    }

    public string CurrentVersion { get; }

    public string LatestVersion { get; }

    public string ReleaseUrl { get; }

    public string Message =>
        $"Update available: s100 {LatestVersion} (current {CurrentVersion}): {ReleaseUrl}";
}
