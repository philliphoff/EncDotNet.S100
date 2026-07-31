namespace EncDotNet.S100.Cli.Infrastructure.Updates;

/// <summary>
/// Retrieves the latest published release.
/// </summary>
internal interface ICliReleaseClient
{
    Task<GitHubReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default);
}
