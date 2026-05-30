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

    config.AddCommand<GateCommand>("gate")
        .WithDescription("Gate CI on regressions across all scenarios in baseline vs candidate directories.");

    config.AddCommand<ChromeTraceCommand>("chrome-trace")
        .WithDescription("Convert a .jsonl telemetry file to Chrome Trace JSON (chrome://tracing / Perfetto / Speedscope). Span timeline, not a CPU flamegraph.");
});

return app.Run(args);
