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

    config.AddCommand<ViewerStressCommand>("viewer-stress")
        .WithDescription("Drive pan/zoom pressure against a running viewer MCP endpoint.")
        .WithExample(
            "viewer-stress",
            "--port-file", "/tmp/viewer/mcp.url",
            "--bbox", "49.8,-6.5,59.0,2.0");
});

return await app.RunAsync(args);
