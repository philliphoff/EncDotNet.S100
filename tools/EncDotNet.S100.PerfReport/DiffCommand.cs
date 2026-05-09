using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.PerfReport;

/// <summary>
/// The <c>diff</c> command: compares a baseline <c>.jsonl</c> against a
/// candidate and highlights regressions and improvements.
/// </summary>
public sealed class DiffCommand : Command<DiffCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<BASELINE>")]
        [Description("Path to the baseline .jsonl file.")]
        public string Baseline { get; set; } = "";

        [CommandArgument(1, "<CANDIDATE>")]
        [Description("Path to the candidate .jsonl file.")]
        public string Candidate { get; set; } = "";

        [CommandOption("--out <PATH>")]
        [Description("Write the diff report to a markdown file instead of stdout.")]
        public string? OutputPath { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (!File.Exists(settings.Baseline))
        {
            AnsiConsole.MarkupLine($"[red]Baseline not found:[/] {settings.Baseline}");
            return 1;
        }
        if (!File.Exists(settings.Candidate))
        {
            AnsiConsole.MarkupLine($"[red]Candidate not found:[/] {settings.Candidate}");
            return 1;
        }

        var baseline = TelemetryFileReader.Read(settings.Baseline);
        var candidate = TelemetryFileReader.Read(settings.Candidate);

        var writer = settings.OutputPath is not null
            ? new StreamWriter(settings.OutputPath)
            : new StreamWriter(Console.OpenStandardOutput());

        try
        {
            WriteDiff(writer, baseline, candidate, settings.Baseline, settings.Candidate);
        }
        finally
        {
            if (settings.OutputPath is not null) writer.Dispose();
        }

        if (settings.OutputPath is not null)
            AnsiConsole.MarkupLine($"[green]Diff written to:[/] {settings.OutputPath}");

        return 0;
    }

    internal static void WriteDiff(
        TextWriter writer,
        TelemetryFileReader baseline,
        TelemetryFileReader candidate,
        string baselinePath,
        string candidatePath)
    {
        writer.WriteLine("# Performance Diff");
        writer.WriteLine();
        writer.WriteLine($"- **Baseline:** `{Path.GetFileName(baselinePath)}`");
        writer.WriteLine($"- **Candidate:** `{Path.GetFileName(candidatePath)}`");
        writer.WriteLine();

        // Span diffs.
        var baseSpans = baseline.Spans.GroupBy(s => s.Name)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.DurationMs));
        var candSpans = candidate.Spans.GroupBy(s => s.Name)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.DurationMs));

        var allSpanNames = baseSpans.Keys.Union(candSpans.Keys).OrderBy(n => n).ToList();
        if (allSpanNames.Count > 0)
        {
            writer.WriteLine("## Span duration totals");
            writer.WriteLine();
            writer.WriteLine("| Span | Baseline (ms) | Candidate (ms) | Delta | Status |");
            writer.WriteLine("|------|--------------|----------------|-------|--------|");

            foreach (var name in allSpanNames)
            {
                var bVal = baseSpans.GetValueOrDefault(name);
                var cVal = candSpans.GetValueOrDefault(name);
                var (deltaStr, status) = FormatDelta(bVal, cVal);
                writer.WriteLine($"| {name} | {bVal:F2} | {cVal:F2} | {deltaStr} | {status} |");
            }

            writer.WriteLine();
        }

        // Metric diffs (histograms by sum, counters by value).
        var baseMetrics = baseline.Metrics.ToDictionary(m => m.Name + "|" + string.Join(",", m.Tags.Select(t => $"{t.Key}={t.Value}")));
        var candMetrics = candidate.Metrics.ToDictionary(m => m.Name + "|" + string.Join(",", m.Tags.Select(t => $"{t.Key}={t.Value}")));

        var allMetricKeys = baseMetrics.Keys.Union(candMetrics.Keys).OrderBy(k => k).ToList();
        if (allMetricKeys.Count > 0)
        {
            writer.WriteLine("## Metric comparison");
            writer.WriteLine();
            writer.WriteLine("| Metric | Type | Baseline | Candidate | Delta | Status |");
            writer.WriteLine("|--------|------|----------|-----------|-------|--------|");

            foreach (var key in allMetricKeys)
            {
                var bm = baseMetrics.GetValueOrDefault(key);
                var cm = candMetrics.GetValueOrDefault(key);
                var bVal = GetMetricValue(bm);
                var cVal = GetMetricValue(cm);
                var name = (bm ?? cm)!.Name;
                var instrument = (bm ?? cm)!.Instrument;
                var (deltaStr, status) = FormatDelta(bVal, cVal);
                writer.WriteLine($"| {name} | {instrument} | {bVal:F2} | {cVal:F2} | {deltaStr} | {status} |");
            }

            writer.WriteLine();
        }

        writer.WriteLine("---");
        writer.WriteLine("*Generated by EncDotNet.S100.PerfReport*");
        writer.Flush();
    }

    private static double GetMetricValue(MetricRecord? m)
    {
        if (m is null) return 0;
        return m.BucketSum ?? m.Value ?? 0;
    }

    internal static (string delta, string status) FormatDelta(double baseline, double candidate)
    {
        if (baseline == 0) return ("N/A", "▫️");
        var pct = (candidate - baseline) / baseline * 100;
        var deltaStr = $"{pct:+0.0;-0.0}%";

        // Higher is worse (duration/count regression).
        if (pct >= 5) return (deltaStr, "❌");
        if (pct <= -10) return (deltaStr, "✅");
        return (deltaStr, "▫️");
    }
}
