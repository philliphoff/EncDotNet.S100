using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.PerfReport;

/// <summary>
/// Produces a tile-focused latency and attribution report from viewer telemetry.
/// </summary>
public sealed class TileReportCommand : Command<TileReportCommand.Settings>
{
    private const string TileJobSpan = "s100.render.tile.job";
    private const string RasterSpan = "s100.render.tile.rasterize";
    private const string DiskReadSpan = "s100.render.tile.stage.disk_read";
    private const string DiskWriteSpan = "s100.render.tile.stage.disk_write";
    private const string PublishSpan = "s100.render.tile.stage.publish";
    private const string PersistenceSpan = "s100.render.tile.cache.persist";

    /// <summary>Command-line settings for <see cref="TileReportCommand"/>.</summary>
    public sealed class Settings : CommandSettings
    {
        /// <summary>Telemetry JSONL file to analyze.</summary>
        [CommandArgument(0, "<FILE>")]
        [Description("Path to the viewer .jsonl telemetry file.")]
        public string File { get; set; } = string.Empty;

        /// <summary>Optional Markdown output path.</summary>
        [CommandOption("--out <PATH>")]
        [Description("Write the tile report to a markdown file instead of stdout.")]
        public string? OutputPath { get; set; }

        /// <summary>Maximum number of slow jobs included in the detail table.</summary>
        [CommandOption("--top <N>")]
        [Description("Number of slowest tile jobs to include.")]
        [DefaultValue(20)]
        public int Top { get; set; } = 20;

        /// <inheritdoc />
        public override ValidationResult Validate() =>
            Top > 0
                ? ValidationResult.Success()
                : ValidationResult.Error("--top must be positive.");
    }

    /// <inheritdoc />
    public override int Execute(CommandContext context, Settings settings)
    {
        if (!File.Exists(settings.File))
        {
            AnsiConsole.MarkupLine(
                $"[red]File not found:[/] {Markup.Escape(settings.File)}");
            return 1;
        }

        var data = TelemetryFileReader.Read(settings.File);
        var writer = settings.OutputPath is null
            ? new StreamWriter(Console.OpenStandardOutput())
            : new StreamWriter(settings.OutputPath);

        try
        {
            WriteReport(writer, data, settings.File, settings.Top);
        }
        finally
        {
            if (settings.OutputPath is not null)
            {
                writer.Dispose();
            }
        }

        if (settings.OutputPath is not null)
        {
            AnsiConsole.MarkupLine(
                $"[green]Tile report written to:[/] {Markup.Escape(settings.OutputPath)}");
        }

        return 0;
    }

    internal static void WriteReport(
        TextWriter writer,
        TelemetryFileReader data,
        string sourcePath,
        int top)
    {
        var analyses = Analyze(data);
        writer.WriteLine("# Tile Render Report");
        writer.WriteLine();
        writer.WriteLine($"**Source:** `{Path.GetFileName(sourcePath)}`");
        writer.WriteLine($"**Tile jobs:** {analyses.Count}");
        writer.WriteLine();

        if (analyses.Count == 0)
        {
            writer.WriteLine(
                "No `s100.render.tile.job` spans were found. Capture the viewer with " +
                "`ENC_DOTNET_OTEL_FILE` set and use the TiledScene render subsystem.");
            writer.Flush();
            return;
        }

        var latencies = analyses
            .Select(static analysis => analysis.TotalLatencyMs)
            .OrderBy(static value => value)
            .ToArray();
        writer.WriteLine("## Latency");
        writer.WriteLine();
        writer.WriteLine("| Statistic | Milliseconds |");
        writer.WriteLine("|---|---:|");
        writer.WriteLine(Invariant(
            $"| P50 | {Statistics.Percentile(latencies, 0.50):F2} |"));
        writer.WriteLine(Invariant(
            $"| P95 | {Statistics.Percentile(latencies, 0.95):F2} |"));
        writer.WriteLine(Invariant(
            $"| P99 | {Statistics.Percentile(latencies, 0.99):F2} |"));
        writer.WriteLine(Invariant($"| Max | {latencies[^1]:F2} |"));
        writer.WriteLine();

        writer.WriteLine("## Dominant cause");
        writer.WriteLine();
        writer.WriteLine("| Cause | Jobs | Percent |");
        writer.WriteLine("|---|---:|---:|");
        foreach (var group in analyses
                     .GroupBy(static analysis => analysis.DominantCause)
                     .OrderByDescending(static group => group.Count()))
        {
            writer.WriteLine(InvariantFormat(
                "| {0} | {1} | {2:F1}% |",
                group.Key,
                group.Count(),
                100.0 * group.Count() / analyses.Count));
        }
        writer.WriteLine();

        var persistence = data.Spans
            .Where(static span => span.Name == PersistenceSpan)
            .Select(static span => span.DurationMs)
            .OrderBy(static value => value)
            .ToArray();
        if (persistence.Length > 0)
        {
            writer.WriteLine("## Background persistence");
            writer.WriteLine();
            writer.WriteLine("| Statistic | Value |");
            writer.WriteLine("|---|---:|");
            writer.WriteLine($"| Completed writes | {persistence.Length} |");
            writer.WriteLine(Invariant(
                $"| P50 duration | {Statistics.Percentile(persistence, 0.50):F2} ms |"));
            writer.WriteLine(Invariant(
                $"| P95 duration | {Statistics.Percentile(persistence, 0.95):F2} ms |"));
            writer.WriteLine(Invariant($"| Max duration | {persistence[^1]:F2} ms |"));
            writer.WriteLine();
        }

        writer.WriteLine($"## Slowest {Math.Min(top, analyses.Count)} jobs");
        writer.WriteLine();
        writer.WriteLine(
            "| Tiles | Priority | Outcome | Total | Queue | Raster | Disk read | " +
            "Disk write | Publish | Other | Ops | Cause |");
        writer.WriteLine(
            "|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var analysis in analyses
                     .OrderByDescending(static value => value.TotalLatencyMs)
                     .Take(top))
        {
            writer.WriteLine(InvariantFormat(
                "| `{0}` | {1} | {2} | {3:F2} | {4:F2} | {5:F2} | {6:F2} | " +
                "{7:F2} | {8:F2} | {9:F2} | {10} | {11} |",
                analysis.Keys,
                analysis.Priority,
                analysis.Outcome,
                analysis.TotalLatencyMs,
                analysis.QueueWaitMs,
                analysis.RasterMs,
                analysis.DiskReadMs,
                analysis.DiskWriteMs,
                analysis.PublishMs,
                analysis.OtherMs,
                analysis.CandidateOperations,
                analysis.DominantCause));
        }

        writer.WriteLine();
        writer.WriteLine(
            "`Total` is first-visible enqueue to publish: queue wait plus tile-job " +
            "execution. `Other` is job time not covered by child stage spans. " +
            "Current traces report asynchronous cache persistence separately; " +
            "`Disk write` is retained for older inline-write traces.");
        writer.Flush();
    }

    private static IReadOnlyList<TileJobAnalysis> Analyze(TelemetryFileReader data)
    {
        var children = data.Spans
            .Where(static span => span.ParentSpanId is not null)
            .GroupBy(static span => (span.TraceId, Parent: span.ParentSpanId!))
            .ToDictionary(static group => group.Key, static group => group.ToList());
        var analyses = new List<TileJobAnalysis>();

        foreach (var job in data.Spans.Where(static span => span.Name == TileJobSpan))
        {
            var descendants = Descendants(job, children);
            var queueWaitMs = TagDouble(job, "s100.render.tile.queue_wait_ms");
            var rasterMs = SumNonNested(descendants, RasterSpan);
            var diskReadMs = Sum(descendants, DiskReadSpan);
            var diskWriteMs = Sum(descendants, DiskWriteSpan);
            var publishMs = Sum(descendants, PublishSpan);
            var attributedJobMs = rasterMs + diskReadMs + diskWriteMs + publishMs;
            var otherMs = Math.Max(0, job.DurationMs - attributedJobMs);
            var causes = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["queue"] = queueWaitMs,
                ["raster"] = rasterMs,
                ["disk-read"] = diskReadMs,
                ["disk-write"] = diskWriteMs,
                ["publish"] = publishMs,
                ["other"] = otherMs,
            };
            var dominantCause = causes.MaxBy(static pair => pair.Value).Key;
            var candidateOperations = descendants
                .Where(static span => span.Name == RasterSpan)
                .Select(static span => TagInt(span, "s100.render.tile.candidate_operations"))
                .DefaultIfEmpty(0)
                .Max();

            analyses.Add(new TileJobAnalysis
            {
                Keys = Tag(job, "s100.render.tile.keys", "(unknown)"),
                Priority = Tag(job, "s100.render.tile.priority", "(unknown)"),
                Outcome = Tag(job, "s100.render.tile.outcome", "(unknown)"),
                TotalLatencyMs = queueWaitMs + job.DurationMs,
                QueueWaitMs = queueWaitMs,
                RasterMs = rasterMs,
                DiskReadMs = diskReadMs,
                DiskWriteMs = diskWriteMs,
                PublishMs = publishMs,
                OtherMs = otherMs,
                CandidateOperations = candidateOperations,
                DominantCause = dominantCause,
            });
        }

        return analyses;
    }

    private static IReadOnlyList<SpanRecord> Descendants(
        SpanRecord root,
        IReadOnlyDictionary<(string TraceId, string Parent), List<SpanRecord>> children)
    {
        var result = new List<SpanRecord>();
        var pending = new Stack<SpanRecord>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var parent = pending.Pop();
            if (!children.TryGetValue((parent.TraceId, parent.SpanId), out var direct))
            {
                continue;
            }

            foreach (var child in direct)
            {
                result.Add(child);
                pending.Push(child);
            }
        }

        return result;
    }

    private static double Sum(
        IReadOnlyList<SpanRecord> spans,
        string name) =>
        spans.Where(span => span.Name == name).Sum(static span => span.DurationMs);

    private static double SumNonNested(
        IReadOnlyList<SpanRecord> spans,
        string name)
    {
        var matchingIds = spans
            .Where(span => span.Name == name)
            .Select(static span => span.SpanId)
            .ToHashSet(StringComparer.Ordinal);
        return spans
            .Where(span =>
                span.Name == name
                && (span.ParentSpanId is null || !matchingIds.Contains(span.ParentSpanId)))
            .Sum(static span => span.DurationMs);
    }

    private static string Tag(
        SpanRecord span,
        string name,
        string fallback) =>
        span.Tags.GetValueOrDefault(name) ?? fallback;

    private static double TagDouble(SpanRecord span, string name) =>
        double.TryParse(
            span.Tags.GetValueOrDefault(name),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    private static int TagInt(SpanRecord span, string name) =>
        int.TryParse(
            span.Tags.GetValueOrDefault(name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    private static string Invariant(FormattableString value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string InvariantFormat(string format, params object[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, format, arguments);

    private sealed class TileJobAnalysis
    {
        public required string Keys { get; init; }
        public required string Priority { get; init; }
        public required string Outcome { get; init; }
        public required double TotalLatencyMs { get; init; }
        public required double QueueWaitMs { get; init; }
        public required double RasterMs { get; init; }
        public required double DiskReadMs { get; init; }
        public required double DiskWriteMs { get; init; }
        public required double PublishMs { get; init; }
        public required double OtherMs { get; init; }
        public required int CandidateOperations { get; init; }
        public required string DominantCause { get; init; }
    }
}
