using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Updates;
using EncDotNet.S100.Core.Scheduling;

// The CLI is a batch host: chart portrayal and raster jobs may use every core.
WorkScheduler.ConfigureShared(WorkSchedulerOptions.ForBatch());

var version = CliVersionInfo.FromAssembly(typeof(CliApp).Assembly);
using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(3),
};
var updateChecker = new CliUpdateChecker(
    version,
    new GitHubReleaseClient(httpClient),
    new JsonCliUpdateCache(),
    TimeProvider.System);

return await CliRunner.RunAsync(args, version, updateChecker, Console.Error);
