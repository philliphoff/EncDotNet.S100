using EncDotNet.S100.PerfRunner;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("perfrunner");

    config.AddCommand<RunCommand>("run")
        .WithDescription("Run a single named performance scenario.")
        .WithExample("run", "s101-portray-cold")
        .WithExample("run", "list");

    config.AddCommand<BaselineCommand>("baseline")
        .WithDescription("Run all scenarios with fixed parameters and produce a baseline.")
        .WithExample("baseline")
        .WithExample("baseline", "--out", "tools/EncDotNet.S100.PerfRunner/baselines");
});

return await app.RunAsync(args);
