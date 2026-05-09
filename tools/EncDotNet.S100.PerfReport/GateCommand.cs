using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.PerfReport;

/// <summary>
/// The <c>gate</c> command: compares all <c>.jsonl</c> files in a baseline
/// directory against matching files in a candidate directory and exits with
/// a non-zero code when any scenario regresses beyond the threshold.
/// </summary>
public sealed class GateCommand : Command<GateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<BASELINE_DIR>")]
        [Description("Path to the baseline directory containing .jsonl files.")]
        public string BaselineDir { get; set; } = "";

        [CommandArgument(1, "<CANDIDATE_DIR>")]
        [Description("Path to the candidate directory containing .jsonl files.")]
        public string CandidateDir { get; set; } = "";

        [CommandOption("--threshold <PCT>")]
        [Description("Regression threshold percentage (default: 5).")]
        [DefaultValue(5.0)]
        public double Threshold { get; set; } = 5.0;

        [CommandOption("--min-abs <VALUE>")]
        [Description("Minimum baseline absolute value to consider for regression gating. Spans/metrics below this floor are reported but never flag a regression (default: 50).")]
        [DefaultValue(50.0)]
        public double MinAbsolute { get; set; } = 50.0;

        [CommandOption("--out <PATH>")]
        [Description("Write the gate summary to a markdown file instead of stdout.")]
        public string? OutputPath { get; set; }
    }

    /// <summary>Exit code returned when one or more scenarios regress.</summary>
    internal const int ExitCodeRegression = 2;

    public override int Execute(CommandContext context, Settings settings)
    {
        if (!Directory.Exists(settings.BaselineDir))
        {
            AnsiConsole.MarkupLine($"[red]Baseline directory not found:[/] {settings.BaselineDir}");
            return 1;
        }

        if (!Directory.Exists(settings.CandidateDir))
        {
            AnsiConsole.MarkupLine($"[red]Candidate directory not found:[/] {settings.CandidateDir}");
            return 1;
        }

        var baselineFiles = Directory.GetFiles(settings.BaselineDir, "*.jsonl")
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => f);
        var candidateFiles = Directory.GetFiles(settings.CandidateDir, "*.jsonl")
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => f);

        var matched = baselineFiles.Keys.Intersect(candidateFiles.Keys).OrderBy(n => n).ToList();

        if (matched.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No matching .jsonl files found between baseline and candidate directories.[/]");
            return 1;
        }

        var results = new List<ScenarioResult>();

        foreach (var scenario in matched)
        {
            var baseline = TelemetryFileReader.Read(baselineFiles[scenario]);
            var candidate = TelemetryFileReader.Read(candidateFiles[scenario]);
            var result = EvaluateScenario(scenario, baseline, candidate, settings.Threshold, settings.MinAbsolute);
            results.Add(result);
        }

        var writer = settings.OutputPath is not null
            ? new StreamWriter(settings.OutputPath)
            : new StreamWriter(Console.OpenStandardOutput());

        try
        {
            WriteGateSummary(writer, results, settings.Threshold);
        }
        finally
        {
            if (settings.OutputPath is not null) writer.Dispose();
        }

        if (settings.OutputPath is not null)
            AnsiConsole.MarkupLine($"[green]Gate summary written to:[/] {settings.OutputPath}");

        var hasRegression = results.Any(r => r.Regressed);

        if (hasRegression)
        {
            AnsiConsole.MarkupLine("[red]Performance gate FAILED — regression(s) detected.[/]");
            return ExitCodeRegression;
        }

        AnsiConsole.MarkupLine("[green]Performance gate PASSED — no regressions.[/]");
        return 0;
    }

    internal static ScenarioResult EvaluateScenario(
        string name,
        TelemetryFileReader baseline,
        TelemetryFileReader candidate,
        double threshold,
        double minAbsolute = 50.0)
    {
        var result = new ScenarioResult { Name = name };

        // Span totals by name.
        var baseSpans = baseline.Spans.GroupBy(s => s.Name)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.DurationMs));
        var candSpans = candidate.Spans.GroupBy(s => s.Name)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.DurationMs));

        foreach (var spanName in baseSpans.Keys.Union(candSpans.Keys).OrderBy(n => n))
        {
            var bVal = baseSpans.GetValueOrDefault(spanName);
            var cVal = candSpans.GetValueOrDefault(spanName);
            var (deltaStr, status) = DiffCommand.FormatDelta(bVal, cVal);
            var pct = bVal == 0 ? 0 : (cVal - bVal) / bVal * 100;

            result.Details.Add(new DetailRow
            {
                Kind = "span",
                Metric = spanName,
                Baseline = bVal,
                Candidate = cVal,
                DeltaStr = deltaStr,
                Status = status,
                Regressed = pct >= threshold && bVal >= minAbsolute,
            });
        }

        // Metric totals.
        var baseMetrics = baseline.Metrics.ToDictionary(
            m => m.Name + "|" + string.Join(",", m.Tags.Select(t => $"{t.Key}={t.Value}")));
        var candMetrics = candidate.Metrics.ToDictionary(
            m => m.Name + "|" + string.Join(",", m.Tags.Select(t => $"{t.Key}={t.Value}")));

        foreach (var key in baseMetrics.Keys.Union(candMetrics.Keys).OrderBy(k => k))
        {
            var bm = baseMetrics.GetValueOrDefault(key);
            var cm = candMetrics.GetValueOrDefault(key);
            var bVal = GetMetricValue(bm);
            var cVal = GetMetricValue(cm);
            var metricName = (bm ?? cm)!.Name;
            var (deltaStr, status) = DiffCommand.FormatDelta(bVal, cVal);
            var pct = bVal == 0 ? 0 : (cVal - bVal) / bVal * 100;

            result.Details.Add(new DetailRow
            {
                Kind = "metric",
                Metric = metricName,
                Baseline = bVal,
                Candidate = cVal,
                DeltaStr = deltaStr,
                Status = status,
                Regressed = pct >= threshold && bVal >= minAbsolute,
            });
        }

        return result;
    }

    internal static void WriteGateSummary(
        TextWriter writer,
        List<ScenarioResult> results,
        double threshold)
    {
        var anyRegressed = results.Any(r => r.Regressed);

        writer.WriteLine("# Performance Gate");
        writer.WriteLine();
        writer.WriteLine(anyRegressed
            ? "❌ **FAILED** — regression(s) detected."
            : "✅ **PASSED** — no regressions.");
        writer.WriteLine();
        writer.WriteLine($"Threshold: **{threshold:F1}%**");
        writer.WriteLine();

        // Summary table.
        writer.WriteLine("## Scenario summary");
        writer.WriteLine();
        writer.WriteLine("| Scenario | Status |");
        writer.WriteLine("|----------|--------|");
        foreach (var r in results)
        {
            writer.WriteLine($"| {r.Name} | {(r.Regressed ? "❌ regressed" : "✅ pass")} |");
        }
        writer.WriteLine();

        // Per-scenario detail.
        foreach (var r in results)
        {
            writer.WriteLine($"## {r.Name}");
            writer.WriteLine();

            var spans = r.Details.Where(d => d.Kind == "span").ToList();
            if (spans.Count > 0)
            {
                writer.WriteLine("### Spans");
                writer.WriteLine();
                writer.WriteLine("| Span | Baseline (ms) | Candidate (ms) | Delta | Status |");
                writer.WriteLine("|------|--------------|----------------|-------|--------|");
                foreach (var d in spans)
                {
                    writer.WriteLine($"| {d.Metric} | {d.Baseline:F2} | {d.Candidate:F2} | {d.DeltaStr} | {d.Status} |");
                }
                writer.WriteLine();
            }

            var metrics = r.Details.Where(d => d.Kind == "metric").ToList();
            if (metrics.Count > 0)
            {
                writer.WriteLine("### Metrics");
                writer.WriteLine();
                writer.WriteLine("| Metric | Baseline | Candidate | Delta | Status |");
                writer.WriteLine("|--------|----------|-----------|-------|--------|");
                foreach (var d in metrics)
                {
                    writer.WriteLine($"| {d.Metric} | {d.Baseline:F2} | {d.Candidate:F2} | {d.DeltaStr} | {d.Status} |");
                }
                writer.WriteLine();
            }
        }

        writer.WriteLine("---");
        writer.WriteLine("*Generated by EncDotNet.S100.PerfReport gate command*");
        writer.Flush();
    }

    private static double GetMetricValue(MetricRecord? m)
    {
        if (m is null) return 0;
        return m.BucketSum ?? m.Value ?? 0;
    }

    internal sealed class ScenarioResult
    {
        public string Name { get; init; } = "";
        public List<DetailRow> Details { get; } = [];
        public bool Regressed => Details.Any(d => d.Regressed);
    }

    internal sealed class DetailRow
    {
        public string Kind { get; init; } = "";
        public string Metric { get; init; } = "";
        public double Baseline { get; init; }
        public double Candidate { get; init; }
        public string DeltaStr { get; init; } = "";
        public string Status { get; init; } = "";
        public bool Regressed { get; init; }
    }
}
