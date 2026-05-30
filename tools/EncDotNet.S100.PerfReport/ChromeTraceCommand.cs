using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EncDotNet.S100.PerfReport;

/// <summary>
/// The <c>chrome-trace</c> command: converts a <c>.jsonl</c> telemetry
/// file produced by the PerfRunner into the Chrome Trace JSON Object
/// Array format.
///
/// <para>
/// The output can be loaded directly in <c>chrome://tracing</c>,
/// <a href="https://ui.perfetto.dev">Perfetto UI</a>, or
/// <a href="https://www.speedscope.app">Speedscope</a>. The format is
/// documented at
/// <a href="https://docs.google.com/document/d/1CvAClvFfyA5R-PhYUmn5OOQtYMH4h6I0nSsKchNAySU/preview">
/// Trace Event Format
/// </a>.
/// </para>
///
/// <para>
/// <strong>This is a span timeline, not a CPU flamegraph.</strong>
/// It visualises the existing
/// <see cref="System.Diagnostics.ActivitySource"/> spans recorded by the
/// product code (pipeline stages, Lua execution, HDF5 reads, renderer
/// frames, etc.). It does <em>not</em> sample the CPU and so cannot
/// show JIT/runtime/library frames between span boundaries. To get a
/// real CPU flamegraph, run the PerfRunner with
/// <c>--profile cpu</c> and convert the resulting <c>.nettrace</c> file
/// using <c>dotnet-trace convert &lt;file&gt;.nettrace --format speedscope</c>.
/// </para>
/// </summary>
public sealed class ChromeTraceCommand : Command<ChromeTraceCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("Path to the .jsonl telemetry file.")]
        public string File { get; set; } = "";

        [CommandOption("--out <PATH>")]
        [Description("Path to write the Chrome Trace JSON file. Defaults to <FILE>.chrome.json next to the input.")]
        public string? OutputPath { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (!System.IO.File.Exists(settings.File))
        {
            AnsiConsole.MarkupLine($"[red]File not found:[/] {settings.File}");
            return 1;
        }

        var reader = TelemetryFileReader.Read(settings.File);
        if (reader.Spans.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No spans found in the telemetry file.[/]");
            return 1;
        }

        // Spans missing start/end timestamps cannot be placed on the
        // timeline. This happens with older telemetry files that pre-date
        // the addition of startUnixNs/endUnixNs to the FileTelemetryExporter
        // schema. Warn rather than fail so partial conversions still work.
        int missingTimestamps = 0;
        foreach (var span in reader.Spans)
        {
            if (span.StartUnixNs is null || span.EndUnixNs is null) missingTimestamps++;
        }
        if (missingTimestamps == reader.Spans.Count)
        {
            AnsiConsole.MarkupLine(
                "[red]All spans are missing startUnixNs/endUnixNs timestamps.[/] " +
                "Re-run the PerfRunner with a current build to produce convertible telemetry.");
            return 1;
        }
        if (missingTimestamps > 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Skipping {missingTimestamps} span(s) without timestamps.[/]");
        }

        var outputPath = settings.OutputPath ?? settings.File + ".chrome.json";

        // Use the earliest span start as the timeline origin so the
        // rendered timeline starts at t=0us. Chrome Trace ts values are
        // in microseconds; we convert from nanoseconds with rounding.
        long? originNs = null;
        foreach (var span in reader.Spans)
        {
            if (span.StartUnixNs is { } s && (originNs is null || s < originNs)) originNs = s;
        }
        long origin = originNs ?? 0;

        // Each distinct traceId becomes a virtual "thread" in the output
        // so that concurrent activity from different scenarios/iterations
        // is rendered as parallel swimlanes rather than overlapping rows.
        var tidByTrace = new Dictionary<string, int>(StringComparer.Ordinal);

        using var stream = System.IO.File.Create(outputPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        writer.WriteStartArray();

        foreach (var span in reader.Spans)
        {
            if (span.StartUnixNs is not { } startNs) continue;
            if (span.EndUnixNs is not { } endNs) continue;

            if (!tidByTrace.TryGetValue(span.TraceId, out var tid))
            {
                tid = tidByTrace.Count + 1;
                tidByTrace[span.TraceId] = tid;
            }

            // Chrome Trace stores ts (start) and dur (duration) in
            // microseconds. Use a "complete" event (ph: "X") rather
            // than separate B/E events — it halves the output size and
            // is what Speedscope/Perfetto prefer.
            double tsUs = (startNs - origin) / 1000.0;
            double durUs = (endNs - startNs) / 1000.0;

            writer.WriteStartObject();
            writer.WriteString("name", span.Name);
            writer.WriteString("cat", "span");
            writer.WriteString("ph", "X");
            writer.WriteNumber("ts", tsUs);
            writer.WriteNumber("dur", durUs);
            writer.WriteNumber("pid", 1);
            writer.WriteNumber("tid", tid);

            writer.WriteStartObject("args");
            writer.WriteString("traceId", span.TraceId);
            writer.WriteString("spanId", span.SpanId);
            if (span.ParentSpanId is not null) writer.WriteString("parentSpanId", span.ParentSpanId);
            foreach (var (k, v) in span.Tags)
            {
                writer.WriteString(k, v);
            }
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        // Emit thread-name metadata events so Perfetto/Speedscope label
        // each swimlane with its traceId rather than "tid 1, tid 2, …".
        foreach (var (traceId, tid) in tidByTrace)
        {
            writer.WriteStartObject();
            writer.WriteString("name", "thread_name");
            writer.WriteString("ph", "M");
            writer.WriteNumber("pid", 1);
            writer.WriteNumber("tid", tid);
            writer.WriteStartObject("args");
            writer.WriteString("name", traceId);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.Flush();

        AnsiConsole.MarkupLine(
            $"[green]Wrote[/] {reader.Spans.Count - missingTimestamps} span(s) " +
            $"across {tidByTrace.Count} trace(s) to [cyan]{outputPath}[/]");
        AnsiConsole.MarkupLine(
            "[grey]Open in chrome://tracing, https://ui.perfetto.dev, or https://www.speedscope.app[/]");
        AnsiConsole.MarkupLine(
            "[grey]This is a span timeline. For a CPU flamegraph, run perfrunner with --profile cpu.[/]");

        return 0;
    }
}
