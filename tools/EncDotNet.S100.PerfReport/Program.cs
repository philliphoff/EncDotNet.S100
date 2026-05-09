using EncDotNet.S100.PerfReport;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("perfreport");

    config.AddCommand<SummariseCommand>("summarise")
        .WithDescription("Summarise a single .jsonl telemetry file.");

    config.AddCommand<DiffCommand>("diff")
        .WithDescription("Compare baseline vs candidate .jsonl files.");
});

return app.Run(args);
