namespace EncDotNet.S100.Cli.Infrastructure.Updates;

/// <summary>
/// Loads and saves the per-user release-check cache.
/// </summary>
internal interface ICliUpdateCache
{
    Task<CliUpdateCacheEntry?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CliUpdateCacheEntry entry, CancellationToken cancellationToken = default);
}
