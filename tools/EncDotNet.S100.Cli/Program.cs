using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Updates;

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
