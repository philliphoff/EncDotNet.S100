namespace EncDotNet.S100.Cli.Infrastructure.Updates;

/// <summary>
/// The update checker when <see cref="EnvironmentVariable"/> turns checks off,
/// as the s100 container image does (issue #1464): it never contacts GitHub
/// and never reports a notice.
/// </summary>
internal sealed class DisabledCliUpdateChecker : ICliUpdateChecker
{
    /// <summary>The environment variable that turns update checks off: <c>S100_NO_UPDATE_CHECK</c>.</summary>
    public const string EnvironmentVariable = "S100_NO_UPDATE_CHECK";

    /// <summary>The one instance.</summary>
    public static DisabledCliUpdateChecker Instance { get; } = new();

    private DisabledCliUpdateChecker()
    {
    }

    /// <summary>
    /// Whether a value of <see cref="EnvironmentVariable"/> turns checks off:
    /// <c>1</c>, <c>true</c> or <c>yes</c>, in any case. Unset, empty or any
    /// other value leaves them on.
    /// </summary>
    public static bool IsDisabledBy(string? value) =>
        value?.Trim().ToLowerInvariant() is "1" or "true" or "yes";

    public Task<CliUpdateNotice?> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<CliUpdateNotice?>(null);
}
