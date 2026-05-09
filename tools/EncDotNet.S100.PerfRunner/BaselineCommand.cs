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
/// The <c>baseline</c> command: runs all registered scenarios in series
/// with fixed parameters and writes results into a git-SHA-keyed
/// subdirectory suitable for committing as a performance baseline.
/// </summary>
public sealed class BaselineCommand : AsyncCommand<BaselineCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--out <DIR>")]
        [System.ComponentModel.Description("Output root for baselines (default: tools/EncDotNet.S100.PerfRunner/baselines).")]
        [System.ComponentModel.DefaultValue("tools/EncDotNet.S100.PerfRunner/baselines")]
        public string OutputDir { get; set; } = "tools/EncDotNet.S100.PerfRunner/baselines";

        [CommandOption("--corpus <PATH>")]
        [System.ComponentModel.Description("Path to the test corpus directory.")]
        [System.ComponentModel.DefaultValue("tests/datasets")]
        public string Corpus { get; set; } = "tests/datasets";

        [CommandOption("--warmup <N>")]
        [System.ComponentModel.Description("Number of warmup iterations (default: 3).")]
        [System.ComponentModel.DefaultValue(3)]
        public int Warmup { get; set; } = 3;

        [CommandOption("--iterations <N>")]
        [System.ComponentModel.Description("Number of measured iterations (default: 20).")]
        [System.ComponentModel.DefaultValue(20)]
        public int Iterations { get; set; } = 20;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var corpus = Path.GetFullPath(settings.Corpus);
        if (!Directory.Exists(corpus))
        {
            AnsiConsole.MarkupLine($"[red]Corpus directory not found:[/] {corpus}");
            return 1;
        }

        // Determine git SHA.
        var gitSha = GetGitSha();
        var gitBranch = GetGitInfo("rev-parse", "--abbrev-ref", "HEAD");
        var gitSubject = GetGitInfo("log", "-1", "--format=%s");

        var outDir = Path.GetFullPath(Path.Combine(settings.OutputDir, gitSha));
        Directory.CreateDirectory(outDir);

        var corpusEnvVar = Environment.GetEnvironmentVariable("ENC_DOTNET_PERF_CORPUS");
        var isFullCorpus = !string.IsNullOrEmpty(corpusEnvVar);

        AnsiConsole.MarkupLine($"[bold]Baseline run[/]");
        AnsiConsole.MarkupLine($"  [bold]Git SHA:[/]    {gitSha}");
        AnsiConsole.MarkupLine($"  [bold]Branch:[/]     {gitBranch}");
        AnsiConsole.MarkupLine($"  [bold]Corpus:[/]     {corpus}");
        AnsiConsole.MarkupLine($"  [bold]Corpus mode:[/]{(isFullCorpus ? " full" : " synthetic-only")}");
        AnsiConsole.MarkupLine($"  [bold]Output:[/]     {outDir}");
        AnsiConsole.MarkupLine($"  [bold]Warmup:[/]     {settings.Warmup}  [bold]Iterations:[/] {settings.Iterations}");
        AnsiConsole.WriteLine();

        var scenarioNames = ScenarioRegistry.Names.OrderBy(n => n).ToList();
        var headlines = new List<(string name, double mean, double p95)>();
        var totalSw = Stopwatch.StartNew();

        foreach (var name in scenarioNames)
        {
            var scenario = ScenarioRegistry.Create(name)!;
            AnsiConsole.MarkupLine($"[bold]━━━ {scenario.Name} ━━━[/]");

            var jsonlPath = Path.Combine(outDir, scenario.Name + ".jsonl");
            var mdPath = Path.Combine(outDir, scenario.Name + ".md");

            // Wire up OTel with the file exporter for this scenario.
            Environment.SetEnvironmentVariable(FileExporterExtensions.FileExportEnvVar, jsonlPath);

            using var tracerProvider = Sdk.CreateTracerProviderBuilder()
                .AddSource("EncDotNet.S100.*")
                .AddFileExporter(jsonlPath)
                .Build();

            using var meterProvider = Sdk.CreateMeterProviderBuilder()
                .AddMeter("EncDotNet.S100.*")
                .AddFileExporter(jsonlPath)
                .Build();

            // Warmup.
            for (int i = 0; i < settings.Warmup; i++)
            {
                AnsiConsole.Markup($"  warmup {i + 1}/{settings.Warmup}…");
                var ctx = new PerfContext { CorpusPath = corpus, IsWarmup = true, Iteration = i };
                try
                {
                    await scenario.RunAsync(ctx, CancellationToken.None);
                    AnsiConsole.MarkupLine(" [grey]done[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($" [red]error: {Markup.Escape(ex.Message)}[/]");
                    break;
                }
            }

            // Measured iterations.
            var durations = new List<double>();
            for (int i = 0; i < settings.Iterations; i++)
            {
                AnsiConsole.Markup($"  iteration {i + 1}/{settings.Iterations}…");
                var sw = Stopwatch.StartNew();
                var ctx = new PerfContext { CorpusPath = corpus, IsWarmup = false, Iteration = i };
                try
                {
                    await scenario.RunAsync(ctx, CancellationToken.None);
                    sw.Stop();
                    durations.Add(sw.Elapsed.TotalMilliseconds);
                    AnsiConsole.MarkupLine($" [green]{sw.Elapsed.TotalMilliseconds:F1}ms[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($" [red]error: {Markup.Escape(ex.Message)}[/]");
                    break;
                }
            }

            // Write per-scenario summary.
            WriteScenarioSummary(mdPath, scenario, settings, durations, gitSha, gitBranch);

            if (durations.Count > 0)
            {
                var mean = durations.Average();
                durations.Sort();
                var p95 = RunCommand.Percentile(durations, 0.95);
                headlines.Add((scenario.Name, mean, p95));
                AnsiConsole.MarkupLine($"  [bold]Mean:[/] {mean:F1}ms  [bold]P95:[/] {p95:F1}ms");
            }
            else
            {
                headlines.Add((scenario.Name, 0, 0));
            }

            AnsiConsole.WriteLine();
        }

        totalSw.Stop();

        // Write SUMMARY.md.
        var summaryPath = Path.Combine(outDir, "SUMMARY.md");
        WriteSummary(summaryPath, settings, gitSha, gitBranch, gitSubject,
            isFullCorpus, headlines);

        AnsiConsole.MarkupLine($"[bold]Total:[/] {totalSw.Elapsed.TotalSeconds:F1}s");
        AnsiConsole.MarkupLine($"[bold]Summary:[/] {summaryPath}");

        return 0;
    }

    private static void WriteScenarioSummary(
        string path, IPerfScenario scenario, Settings settings,
        List<double> durations, string gitSha, string gitBranch)
    {
        durations.Sort();
        using var writer = new StreamWriter(path);
        writer.WriteLine($"# {scenario.Name}");
        writer.WriteLine();
        writer.WriteLine($"- **Git SHA:** `{gitSha}`");
        writer.WriteLine($"- **Branch:** `{gitBranch}`");
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
            writer.WriteLine($"| P50 | {RunCommand.Percentile(durations, 0.50):F2} |");
            writer.WriteLine($"| P90 | {RunCommand.Percentile(durations, 0.90):F2} |");
            writer.WriteLine($"| P95 | {RunCommand.Percentile(durations, 0.95):F2} |");
            writer.WriteLine($"| P99 | {RunCommand.Percentile(durations, 0.99):F2} |");
            writer.WriteLine($"| Max | {durations[^1]:F2} |");
            writer.WriteLine($"| Mean | {durations.Average():F2} |");
        }

        writer.WriteLine();
        writer.WriteLine("---");
        writer.WriteLine("*Generated by EncDotNet.S100.PerfRunner baseline command.*");
    }

    private static void WriteSummary(
        string path, Settings settings, string gitSha, string gitBranch,
        string gitSubject, bool isFullCorpus,
        List<(string name, double mean, double p95)> headlines)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("# Baseline Summary");
        writer.WriteLine();
        writer.WriteLine("## Environment");
        writer.WriteLine();
        writer.WriteLine($"- **Git SHA:** `{gitSha}`");
        writer.WriteLine($"- **Branch:** `{gitBranch}`");
        writer.WriteLine($"- **Commit:** {gitSubject}");
        writer.WriteLine($"- **Date:** {DateTime.UtcNow:u}");
        writer.WriteLine($"- **OS:** {RuntimeInformation.OSDescription}");
        writer.WriteLine($"- **Arch:** {RuntimeInformation.ProcessArchitecture}");
        writer.WriteLine($"- **CPUs:** {Environment.ProcessorCount}");
        writer.WriteLine($"- **Runtime:** {RuntimeInformation.FrameworkDescription}");
        writer.WriteLine($"- **Corpus mode:** {(isFullCorpus ? "full corpus" : "synthetic-only")}");
        writer.WriteLine($"- **Warmup:** {settings.Warmup}");
        writer.WriteLine($"- **Iterations:** {settings.Iterations}");
        writer.WriteLine();

        writer.WriteLine("## Per-scenario headline");
        writer.WriteLine();
        writer.WriteLine("| Scenario | Mean (ms) | P95 (ms) |");
        writer.WriteLine("|----------|-----------|----------|");
        foreach (var (name, mean, p95) in headlines)
        {
            writer.WriteLine($"| {name} | {mean:F2} | {p95:F2} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Files");
        writer.WriteLine();
        writer.WriteLine("Each scenario produces:");
        writer.WriteLine("- `<scenario>.jsonl` — newline-delimited JSON telemetry (spans + metrics).");
        writer.WriteLine("- `<scenario>.md` — markdown summary with iteration statistics.");
        writer.WriteLine();
        writer.WriteLine("Use `PerfReport summarise <file>.jsonl` to view detailed span/metric");
        writer.WriteLine("breakdowns, or `PerfReport diff <baseline>.jsonl <candidate>.jsonl`");
        writer.WriteLine("to compare two runs.");
        writer.WriteLine();
        writer.WriteLine("---");
        writer.WriteLine("*Generated by EncDotNet.S100.PerfRunner baseline command.*");
    }

    private static string GetGitSha()
    {
        var sha = GetGitInfo("rev-parse", "--short=12", "HEAD");
        return string.IsNullOrWhiteSpace(sha) ? "unknown" : sha;
    }

    private static string GetGitInfo(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd().Trim() ?? "";
            proc?.WaitForExit();
            return output;
        }
        catch
        {
            return "unknown";
        }
    }
}
