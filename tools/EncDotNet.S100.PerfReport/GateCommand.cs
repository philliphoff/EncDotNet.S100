using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.PerfReport;

/// <summary>
/// The <c>gate</c> command: compares all <c>.jsonl</c> files in a baseline
/// directory against matching files in a candidate directory and exits with
/// a non-zero code when any scenario regresses beyond the configured
/// threshold.
/// </summary>
/// <remarks>
/// <para>
/// When per-iteration samples (<c>perf.iteration</c> spans) are present
/// in both files, the gate uses a noise-robust comparison built on
/// median + median absolute deviation (MAD). A scenario is only flagged
/// as a regression when <em>all</em> of:
/// <list type="bullet">
///   <item>relative delta of medians ≥ <c>--threshold</c>;</item>
///   <item>delta-over-baseline-MAD (z-like) ≥ <c>--mad-k</c>;</item>
///   <item>baseline median ≥ <c>--min-abs</c>.</item>
/// </list>
/// Scenarios where the delta is between 1× and <c>--retry-zone-mult</c>×
/// the threshold are categorised as <em>suspicious</em> and listed in a
/// sidecar file at <c>{out}.suspicious.txt</c>. A workflow orchestrator
/// can then re-run only those scenarios with extra rounds before issuing
/// a final verdict.
/// </para>
/// <para>
/// When iteration samples are absent (older baselines, ad-hoc runs), the
/// gate falls back to the original behaviour of summing all spans /
/// metrics with the same flat threshold.
/// </para>
/// </remarks>
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

        [CommandOption("--mad-k <K>")]
        [Description("Multiplier on baseline MAD that the candidate-baseline median delta must exceed before a regression is flagged (default: 3.0). Only applied when per-iteration samples are present.")]
        [DefaultValue(3.0)]
        public double MadK { get; set; } = 3.0;

        [CommandOption("--retry-zone-mult <F>")]
        [Description("Scenarios whose relative delta is between 1\u00d7 and F\u00d7 the threshold are marked 'suspicious' (and written to {out}.suspicious.txt) instead of failing the gate. Set to 1.0 to disable suspicion handling and fail immediately at the threshold (default: 2.0).")]
        [DefaultValue(2.0)]
        public double RetryZoneMult { get; set; } = 2.0;

        [CommandOption("--min-samples <N>")]
        [Description("Minimum per-side iteration samples required to use median/MAD gating. Falls back to span-sum totals when fewer samples are present (default: 5).")]
        [DefaultValue(5)]
        public int MinSamples { get; set; } = 5;

        [CommandOption("--out <PATH>")]
        [Description("Write the gate summary to a markdown file instead of stdout. A sibling file '<PATH>.suspicious.txt' lists scenarios in the suspicious zone.")]
        public string? OutputPath { get; set; }
    }

    /// <summary>Exit code returned when one or more scenarios regress.</summary>
    internal const int ExitCodeRegression = 2;

    /// <summary>Verdict for a single scenario after gating.</summary>
    public enum ScenarioStatus
    {
        /// <summary>Within thresholds — no action required.</summary>
        Clean,

        /// <summary>Above the threshold but below <c>retry-zone-mult</c>× threshold; eligible for re-test.</summary>
        Suspicious,

        /// <summary>Above the suspicion zone — fail the gate.</summary>
        Regressed,
    }

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
            var result = EvaluateScenario(scenario, baseline, candidate,
                settings.Threshold, settings.MinAbsolute,
                settings.MadK, settings.RetryZoneMult, settings.MinSamples);
            results.Add(result);
        }

        var writer = settings.OutputPath is not null
            ? new StreamWriter(settings.OutputPath)
            : new StreamWriter(Console.OpenStandardOutput());

        try
        {
            WriteGateSummary(writer, results, settings.Threshold, settings.MadK, settings.RetryZoneMult);
        }
        finally
        {
            if (settings.OutputPath is not null) writer.Dispose();
        }

        // Sidecar file listing scenarios that should be re-tested.
        if (settings.OutputPath is not null)
        {
            var suspectPath = settings.OutputPath + ".suspicious.txt";
            var suspects = results.Where(r => r.Status == ScenarioStatus.Suspicious).Select(r => r.Name).ToList();
            File.WriteAllLines(suspectPath, suspects);
            AnsiConsole.MarkupLine($"[green]Gate summary written to:[/] {settings.OutputPath}");
            AnsiConsole.MarkupLine($"[green]Suspicious list written to:[/] {suspectPath} ({suspects.Count} scenario(s))");
        }

        var hasRegression = results.Any(r => r.Status == ScenarioStatus.Regressed);
        var hasSuspicious = results.Any(r => r.Status == ScenarioStatus.Suspicious);

        if (hasRegression)
        {
            AnsiConsole.MarkupLine("[red]Performance gate FAILED — regression(s) detected.[/]");
            return ExitCodeRegression;
        }

        if (hasSuspicious)
        {
            AnsiConsole.MarkupLine("[yellow]Performance gate PASSED with suspicions — re-test recommended.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine("[green]Performance gate PASSED — no regressions.[/]");
        return 0;
    }

    /// <summary>
    /// Back-compat overload preserving the original signature used by
    /// existing tests. Forwards to the full overload with default
    /// <c>mad-k</c>, <c>retry-zone-mult</c>, and <c>min-samples</c>.
    /// </summary>
    internal static ScenarioResult EvaluateScenario(
        string name,
        TelemetryFileReader baseline,
        TelemetryFileReader candidate,
        double threshold,
        double minAbsolute = 50.0)
        => EvaluateScenario(name, baseline, candidate, threshold, minAbsolute,
            madK: 3.0, retryZoneMult: 2.0, minSamples: 5);

    internal static ScenarioResult EvaluateScenario(
        string name,
        TelemetryFileReader baseline,
        TelemetryFileReader candidate,
        double threshold,
        double minAbsolute,
        double madK,
        double retryZoneMult,
        int minSamples)
    {
        var result = new ScenarioResult { Name = name };

        // Prefer per-iteration median/MAD comparison when both sides have
        // enough samples. The iteration-level decision becomes the
        // headline scenario verdict; span-sum / metric details are still
        // surfaced below for diagnostic value but do not influence the
        // final status.
        var baseIterations = baseline.Iterations
            .Where(s => s.Scenario == name || string.IsNullOrEmpty(s.Scenario))
            .Select(s => s.DurationMs)
            .ToList();
        var candIterations = candidate.Iterations
            .Where(s => s.Scenario == name || string.IsNullOrEmpty(s.Scenario))
            .Select(s => s.DurationMs)
            .ToList();

        var hasIterationData = baseIterations.Count >= minSamples && candIterations.Count >= minSamples;

        if (hasIterationData)
        {
            var baseMed = Statistics.Median(baseIterations);
            var candMed = Statistics.Median(candIterations);
            var baseMad = Statistics.MedianAbsoluteDeviation(baseIterations, baseMed);
            var madFloor = Math.Max(baseMad, 1e-9);
            var pct = baseMed == 0 ? 0 : (candMed - baseMed) / baseMed * 100;
            var z = (candMed - baseMed) / madFloor;

            var status = ClassifyScenario(pct, z, baseMed,
                threshold, madK, retryZoneMult, minAbsolute);

            result.Status = status;
            result.IterationStats = new IterationStats(
                BaselineCount: baseIterations.Count,
                CandidateCount: candIterations.Count,
                BaselineMedian: baseMed,
                CandidateMedian: candMed,
                BaselineMad: baseMad,
                PercentDelta: pct,
                ZScore: z);
        }

        // Span totals by name (always emitted as detail rows, even when
        // iteration data drives the verdict — useful for spotting which
        // sub-stage moved).
        var baseSpans = baseline.Spans.GroupBy(s => s.Name)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.DurationMs));
        var candSpans = candidate.Spans.GroupBy(s => s.Name)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.DurationMs));

        foreach (var spanName in baseSpans.Keys.Union(candSpans.Keys).OrderBy(n => n))
        {
            // Skip the perf.iteration framing span itself — its duration
            // is already represented in the iteration-stats row and would
            // double-count if shown again.
            if (spanName == "perf.iteration") continue;

            var bVal = baseSpans.GetValueOrDefault(spanName);
            var cVal = candSpans.GetValueOrDefault(spanName);
            var (deltaStr, statusGlyph) = DiffCommand.FormatDelta(bVal, cVal);
            var pct = bVal == 0 ? 0 : (cVal - bVal) / bVal * 100;

            var regressed = !hasIterationData
                ? (pct >= threshold && bVal >= minAbsolute)
                : false;

            result.Details.Add(new DetailRow
            {
                Kind = "span",
                Metric = spanName,
                Baseline = bVal,
                Candidate = cVal,
                DeltaStr = deltaStr,
                Status = statusGlyph,
                Regressed = regressed,
            });
        }

        // Metric totals.
        static string MetricKey(MetricRecord m) =>
            m.Name + "|" + string.Join(",", m.Tags.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value}"));

        var baseMetrics = baseline.Metrics
            .GroupBy(MetricKey)
            .ToDictionary(g => g.Key, g => g.Last());
        var candMetrics = candidate.Metrics
            .GroupBy(MetricKey)
            .ToDictionary(g => g.Key, g => g.Last());

        foreach (var key in baseMetrics.Keys.Union(candMetrics.Keys).OrderBy(k => k))
        {
            var bm = baseMetrics.GetValueOrDefault(key);
            var cm = candMetrics.GetValueOrDefault(key);
            var bVal = GetMetricValue(bm);
            var cVal = GetMetricValue(cm);
            var metricName = (bm ?? cm)!.Name;
            var (deltaStr, statusGlyph) = DiffCommand.FormatDelta(bVal, cVal);
            var pct = bVal == 0 ? 0 : (cVal - bVal) / bVal * 100;

            var regressed = !hasIterationData
                ? (pct >= threshold && bVal >= minAbsolute)
                : false;

            result.Details.Add(new DetailRow
            {
                Kind = "metric",
                Metric = metricName,
                Baseline = bVal,
                Candidate = cVal,
                DeltaStr = deltaStr,
                Status = statusGlyph,
                Regressed = regressed,
            });
        }

        // When no iteration data is available, derive the scenario-level
        // status from the legacy regressed-detail flag for back-compat.
        if (!hasIterationData)
        {
            result.Status = result.Details.Any(d => d.Regressed)
                ? ScenarioStatus.Regressed
                : ScenarioStatus.Clean;
        }

        return result;
    }

    /// <summary>
    /// Classifies a scenario into <see cref="ScenarioStatus"/> using the
    /// percentage delta of medians, the MAD-relative z-like score, the
    /// baseline median, and the configured thresholds. Below
    /// <paramref name="minAbsolute"/> the scenario is always
    /// <see cref="ScenarioStatus.Clean"/> — small absolute timings are
    /// dominated by noise.
    /// </summary>
    internal static ScenarioStatus ClassifyScenario(
        double pctDelta, double zScore, double baselineMedian,
        double threshold, double madK, double retryZoneMult, double minAbsolute)
    {
        if (baselineMedian < minAbsolute) return ScenarioStatus.Clean;
        if (pctDelta < threshold || zScore < madK) return ScenarioStatus.Clean;

        var hardPct = threshold * retryZoneMult;
        var hardZ = madK * retryZoneMult;
        if (pctDelta >= hardPct && zScore >= hardZ) return ScenarioStatus.Regressed;

        return ScenarioStatus.Suspicious;
    }

    internal static void WriteGateSummary(
        TextWriter writer,
        List<ScenarioResult> results,
        double threshold,
        double madK,
        double retryZoneMult)
    {
        var anyRegressed = results.Any(r => r.Status == ScenarioStatus.Regressed);
        var anySuspicious = results.Any(r => r.Status == ScenarioStatus.Suspicious);

        writer.WriteLine("# Performance Gate");
        writer.WriteLine();
        if (anyRegressed)
            writer.WriteLine("❌ **FAILED** — regression(s) detected.");
        else if (anySuspicious)
            writer.WriteLine("⚠️ **PASSED with suspicions** — re-test recommended.");
        else
            writer.WriteLine("✅ **PASSED** — no regressions.");
        writer.WriteLine();
        writer.WriteLine($"Threshold: **{threshold:F1}%**, MAD multiplier (k): **{madK:F1}**, retry-zone mult: **{retryZoneMult:F1}\u00d7**");
        writer.WriteLine();

        // Summary table.
        writer.WriteLine("## Scenario summary");
        writer.WriteLine();
        writer.WriteLine("| Scenario | Status | Δ median (%) | z (Δ/MAD) | Base median (ms) | Samples (b/c) |");
        writer.WriteLine("|----------|--------|--------------|-----------|------------------|---------------|");
        foreach (var r in results)
        {
            var glyph = r.Status switch
            {
                ScenarioStatus.Regressed => "❌ regressed",
                ScenarioStatus.Suspicious => "⚠️ suspicious",
                _ => "✅ pass",
            };
            if (r.IterationStats is { } st)
            {
                writer.WriteLine($"| {r.Name} | {glyph} | {st.PercentDelta:+0.0;-0.0;0.0} | {st.ZScore:+0.00;-0.00;0.00} | {st.BaselineMedian:F2} | {st.BaselineCount}/{st.CandidateCount} |");
            }
            else
            {
                writer.WriteLine($"| {r.Name} | {glyph} | — | — | — | (span-sum fallback) |");
            }
        }
        writer.WriteLine();

        // Per-scenario detail.
        foreach (var r in results)
        {
            writer.WriteLine($"## {r.Name}");
            writer.WriteLine();

            if (r.IterationStats is { } stats)
            {
                writer.WriteLine("### Iteration statistics");
                writer.WriteLine();
                writer.WriteLine("| Stat | Baseline | Candidate |");
                writer.WriteLine("|------|----------|-----------|");
                writer.WriteLine($"| Samples | {stats.BaselineCount} | {stats.CandidateCount} |");
                writer.WriteLine($"| Median (ms) | {stats.BaselineMedian:F2} | {stats.CandidateMedian:F2} |");
                writer.WriteLine($"| Baseline MAD (ms) | {stats.BaselineMad:F2} | — |");
                writer.WriteLine($"| Δ median | — | {stats.PercentDelta:+0.0;-0.0;0.0}% |");
                writer.WriteLine($"| z (Δ/MAD) | — | {stats.ZScore:+0.00;-0.00;0.00} |");
                writer.WriteLine();
            }

            var spans = r.Details.Where(d => d.Kind == "span").ToList();
            if (spans.Count > 0)
            {
                writer.WriteLine("### Spans (sum of all iterations)");
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

    /// <summary>Statistical summary of per-iteration samples for a scenario.</summary>
    public sealed record IterationStats(
        int BaselineCount,
        int CandidateCount,
        double BaselineMedian,
        double CandidateMedian,
        double BaselineMad,
        double PercentDelta,
        double ZScore);

    internal sealed class ScenarioResult
    {
        public string Name { get; init; } = "";
        public List<DetailRow> Details { get; } = [];
        public ScenarioStatus Status { get; set; } = ScenarioStatus.Clean;
        public IterationStats? IterationStats { get; set; }

        /// <summary>Back-compat shim used by existing tests.</summary>
        public bool Regressed => Status == ScenarioStatus.Regressed
            || (IterationStats is null && Details.Any(d => d.Regressed));
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
