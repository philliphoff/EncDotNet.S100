namespace EncDotNet.S100.Cli.Infrastructure.Updates;

/// <summary>
/// Minimal metadata from GitHub's latest-release response.
/// </summary>
internal sealed record GitHubReleaseInfo(string TagName, string? HtmlUrl);
