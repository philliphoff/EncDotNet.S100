using EncDotNet.S100.PerfRunner;
using Spectre.Console.Cli;

var app = new CommandApp<RunCommand>();
app.Configure(config =>
{
    config.SetApplicationName("perfrunner");
    config.AddExample("run", "s101-portray-cold");
    config.AddExample("run", "list");
});

return await app.RunAsync(args);
