using EncDotNet.S100.Cli.Infrastructure.Updates;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Runs the command application and appends a bounded update notification.
/// </summary>
internal static class CliRunner
{
    internal static readonly TimeSpan UpdateCompletionGracePeriod = TimeSpan.FromSeconds(1);

    public static async Task<int> RunAsync(
        string[] args,
        CliVersionInfo version,
        ICliUpdateChecker updateChecker,
        TextWriter standardError,
        IAnsiConsole? commandConsole = null,
        TimeSpan? updateCompletionGracePeriod = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(updateChecker);
        ArgumentNullException.ThrowIfNull(standardError);

        var updateTask = updateChecker.CheckAsync();
        var exitCode = CliApp
            .Build(version.InformationalVersion, commandConsole)
            .Run(args);

        try
        {
            var notice = await updateTask
                .WaitAsync(updateCompletionGracePeriod ?? UpdateCompletionGracePeriod)
                .ConfigureAwait(false);
            if (notice is not null)
            {
                await standardError.WriteLineAsync(notice.Message).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            _ = updateTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (OperationCanceledException)
        {
            // A timed-out or cancelled update check is intentionally silent.
        }

        return exitCode;
    }
}
