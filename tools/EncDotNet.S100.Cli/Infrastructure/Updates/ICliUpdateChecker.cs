namespace EncDotNet.S100.Cli.Infrastructure.Updates;

/// <summary>
/// Checks whether the running CLI has a newer release.
/// </summary>
internal interface ICliUpdateChecker
{
    Task<CliUpdateNotice?> CheckAsync(CancellationToken cancellationToken = default);
}
