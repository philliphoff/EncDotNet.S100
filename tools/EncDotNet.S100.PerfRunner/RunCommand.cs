using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using EncDotNet.S100.Diagnostics.Export;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// The <c>run</c> command that drives a named performance scenario.
/// </summary>
public sealed class RunCommand : AsyncCommand<RunCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<scenario>")]
        [Description("Scenario name (e.g. s101-portray-cold). Use 'list' to see available scenarios.")]
        public string Scenario { get; set; } = string.Empty;

        [CommandOption("--corpus <PATH>")]
        [Description("Path to the test corpus directory.")]
        [DefaultValue("tests/datasets")]
        public string Corpus { get; set; } = "tests/datasets";

        [CommandOption("--out <DIR>")]
        [Description("Output directory for .jsonl and .md files.")]
        [DefaultValue("./perf-runs")]
        public string OutputDir { get; set; } = "./perf-runs";

        [CommandOption("--warmup <N>")]
        [Description("Number of warmup iterations (discarded).")]
        [DefaultValue(3)]
        public int Warmup { get; set; } = 3;

        [CommandOption("--iterations <N>")]
        [Description("Number of measured iterations.")]
        [DefaultValue(20)]
        public int Iterations { get; set; } = 20;

        [CommandOption("--tag <KEY=VALUE>")]
        [Description("Extra metadata tags (repeatable).")]
        public string[]? Tags { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.Equals(settings.Scenario, "list", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[bold]Available scenarios:[/]");
            foreach (var name in ScenarioRegistry.Names)
            {
                var s = ScenarioRegistry.Create(name)!;
                AnsiConsole.MarkupLine($"  [green]{s.Name}[/] — {s.Description}");
            }
            return 0;
        }

        var scenario = ScenarioRegistry.Create(settings.Scenario);
        if (scenario is null)
        {
            AnsiConsole.MarkupLine($"[red]Unknown scenario:[/] {settings.Scenario}");
            AnsiConsole.MarkupLine("Available: " + string.Join(", ", ScenarioRegistry.Names));
            return 1;
        }

        // Resolve corpus path.
        var corpus = Path.GetFullPath(settings.Corpus);
        if (!Directory.Exists(corpus))
        {
            AnsiConsole.MarkupLine($"[red]Corpus directory not found:[/] {corpus}");
            return 1;
        }

        // Set up output.
        Directory.CreateDirectory(settings.OutputDir);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var baseName = $"{timestamp}-{scenario.Name}";
        var jsonlPath = Path.Combine(settings.OutputDir, baseName + ".jsonl");
        var mdPath = Path.Combine(settings.OutputDir, baseName + ".md");

        // Wire up OTel with the file exporter.
        Environment.SetEnvironmentVariable(FileExporterExtensions.FileExportEnvVar, jsonlPath);

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("EncDotNet.S100.*")
            .AddFileExporter(jsonlPath)
            .Build();

        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter("EncDotNet.S100.*")
            .AddFileExporter(jsonlPath)
            .Build();

        AnsiConsole.MarkupLine($"[bold]Scenario:[/] {scenario.Name}");
        AnsiConsole.MarkupLine($"[bold]Corpus:[/]   {corpus}");
        AnsiConsole.MarkupLine($"[bold]Output:[/]   {jsonlPath}");
        AnsiConsole.MarkupLine($"[bold]Warmup:[/]   {settings.Warmup}  [bold]Iterations:[/] {settings.Iterations}");
        AnsiConsole.WriteLine();

        var totalSw = Stopwatch.StartNew();

        // Warmup iterations.
        for (int i = 0; i < settings.Warmup; i++)
        {
            AnsiConsole.Markup($"  warmup {i + 1}/{settings.Warmup}…");
            var ctx = new PerfContext { CorpusPath = corpus, IsWarmup = true, Iteration = i };
            await scenario.RunAsync(ctx, CancellationToken.None);
            AnsiConsole.MarkupLine(" [grey]done[/]");
        }

        // Measured iterations.
        var durations = new List<double>();
        for (int i = 0; i < settings.Iterations; i++)
        {
            AnsiConsole.Markup($"  iteration {i + 1}/{settings.Iterations}…");
            var sw = Stopwatch.StartNew();
            var ctx = new PerfContext { CorpusPath = corpus, IsWarmup = false, Iteration = i };
            await scenario.RunAsync(ctx, CancellationToken.None);
            sw.Stop();
            durations.Add(sw.Elapsed.TotalMilliseconds);
            AnsiConsole.MarkupLine($" [green]{sw.Elapsed.TotalMilliseconds:F1}ms[/]");
        }

        totalSw.Stop();
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Total:[/] {totalSw.Elapsed.TotalSeconds:F1}s");

        // Write a simple markdown summary alongside.
        WriteSummary(mdPath, scenario, settings, durations);

        AnsiConsole.MarkupLine($"[bold]Summary:[/] {mdPath}");
        return 0;
    }

    private static void WriteSummary(
        string path, IPerfScenario scenario, Settings settings, List<double> durations)
    {
        durations.Sort();
        using var writer = new StreamWriter(path);
        writer.WriteLine($"# {scenario.Name}");
        writer.WriteLine();
        writer.WriteLine($"- **Date:** {DateTime.UtcNow:u}");
        writer.WriteLine($"- **OS:** {RuntimeInformation.OSDescription}");
        writer.WriteLine($"- **Arch:** {RuntimeInformation.ProcessArchitecture}");
        writer.WriteLine($"- **CPUs:** {Environment.ProcessorCount}");
        writer.WriteLine($"- **Runtime:** {RuntimeInformation.FrameworkDescription}");
        writer.WriteLine($"- **Warmup:** {settings.Warmup}");
        writer.WriteLine($"- **Iterations:** {settings.Iterations}");
        writer.WriteLine($"- **Corpus:** {settings.Corpus}");
        writer.WriteLine();

        if (durations.Count > 0)
        {
            writer.WriteLine("## Iteration durations (ms)");
            writer.WriteLine();
            writer.WriteLine("| Stat | Value |");
            writer.WriteLine("|------|-------|");
            writer.WriteLine($"| Min | {durations[0]:F2} |");
            writer.WriteLine($"| P50 | {Percentile(durations, 0.50):F2} |");
            writer.WriteLine($"| P90 | {Percentile(durations, 0.90):F2} |");
            writer.WriteLine($"| P95 | {Percentile(durations, 0.95):F2} |");
            writer.WriteLine($"| P99 | {Percentile(durations, 0.99):F2} |");
            writer.WriteLine($"| Max | {durations[^1]:F2} |");
            writer.WriteLine($"| Mean | {durations.Average():F2} |");
        }

        if (settings.Tags is { Length: > 0 })
        {
            writer.WriteLine();
            writer.WriteLine("## Tags");
            writer.WriteLine();
            foreach (var tag in settings.Tags)
            {
                writer.WriteLine($"- `{tag}`");
            }
        }

        writer.WriteLine();
        writer.WriteLine("---");
        writer.WriteLine("*Generated by EncDotNet.S100.PerfRunner — example only, not a baseline.*");
    }

    internal static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var index = p * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (index - lower) * (sorted[upper] - sorted[lower]);
    }
}
