namespace EncDotNet.S100.Cli.Infrastructure.Updates;

/// <summary>
/// Persisted result of the latest completed release check.
/// </summary>
internal sealed record CliUpdateCacheEntry(
    DateTimeOffset CheckedAtUtc,
    string? LatestVersion,
    string? ReleaseUrl);
