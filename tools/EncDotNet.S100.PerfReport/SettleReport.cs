using System.Globalization;
using System.Text.Json.Nodes;

namespace EncDotNet.S100.PerfReport;

/// <summary>
/// The settle-run manifest that PerfRunner's <c>viewer-stress --scenario
/// jumps|pan</c> writes (map rendering slice 1), and its Markdown summary and
/// diff. This file is compiled into both PerfRunner (which writes manifests)
/// and PerfReport (which reads them), so the two can't drift.
/// </summary>
/// <remarks>
/// A manifest is one JSON object: <c>kind</c> = <c>viewer-settle</c>,
/// <c>schemaVersion</c>, <c>route</c>, <c>mode</c>, <c>engine</c>, the
/// <c>machine</c> and <c>viewer</c> environment, <c>steps</c> (one flat
/// object per jump, or one for a pan, built by <see cref="Flatten"/>) and
/// <c>totals</c> (<see cref="Totals"/>). Each step keeps the raw MCP replies
/// under <c>raw</c>.
/// </remarks>
internal static class SettleReport
{
    /// <summary>The manifest's <c>kind</c>.</summary>
    public const string Kind = "viewer-settle";

    /// <summary>The manifest schema version.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The metrics a step carries, in report order: field, heading, unit kind, and whether lower is better.</summary>
    internal static readonly (string Field, string Heading, string Unit, bool LowerIsBetter)[] StepMetrics =
    [
        ("settleMs", "Settle", "ms", true),
        ("cpuMs", "CPU", "ms", true),
        ("coresBusy", "Cores busy", "x", false),
        ("schedulerCoresBusy", "Scheduler cores busy", "x", false),
        ("visibleWaitP95Ms", "Visible job wait p95", "ms", true),
        ("jobRunP95Ms", "Job run p95", "ms", true),
        ("gcPauseShare", "GC pause share", "%", true),
        ("gcPauseMs", "GC pause", "ms", true),
        ("gen2Collections", "Gen 2", "n", true),
        ("allocatedBytes", "Allocated", "bytes", true),
        ("peakWorkingSetBytes", "Peak working set", "bytes", true),
        ("peakGcHeapBytes", "Peak GC heap", "bytes", true),
        ("peakGcCommittedBytes", "Peak GC committed", "bytes", true),
        ("peakTileNativeBytes", "Peak tile native", "bytes", true),
        ("frameP50Ms", "Frame p50", "ms", true),
        ("frameP95Ms", "Frame p95", "ms", true),
        ("frameMaxMs", "Frame max", "ms", true),
    ];

    /// <summary>Whether <paramref name="node"/> is a settle manifest.</summary>
    public static bool IsSettleManifest(JsonNode? node) =>
        node is JsonObject o && o["kind"]?.GetValue<string>() == Kind;

    /// <summary>Whether the file at <paramref name="path"/> is a settle manifest.</summary>
    public static bool IsSettleManifestFile(string path)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            return IsSettleManifest(JsonNode.Parse(File.ReadAllText(path)));
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds one step from the viewer's MCP replies: <c>await_settled</c>,
    /// <c>get_process_metrics</c> (its window covers the step) and
    /// <c>get_render_stats</c> (its window covers the step's paints).
    /// </summary>
    public static JsonObject Flatten(
        string name,
        double latitude,
        double longitude,
        JsonNode? settle,
        JsonNode? metrics,
        JsonNode? renderStats)
    {
        var window = metrics?["window"];
        var memory = metrics?["memory"];
        var frames = renderStats?["window"];
        var scheduler = metrics?["scheduler"];
        return new JsonObject
        {
            ["name"] = name,
            ["latitude"] = latitude,
            ["longitude"] = longitude,
            ["settled"] = Bool(settle, "settled"),
            ["timedOut"] = Bool(settle, "timedOut"),
            ["settleMs"] = Number(settle, "settleMs"),
            ["waitedMs"] = Number(settle, "waitedMs"),
            ["wallMs"] = Number(window, "wallMs"),
            ["cpuMs"] = Number(window, "cpuMs"),
            ["coresBusy"] = Number(window, "coresBusy"),
            ["cpuUtilisation"] = Number(window, "cpuUtilisation"),
            ["gcPauseMs"] = Number(window, "gcPauseMs"),
            ["gcPauseShare"] = Number(window, "gcPauseShare"),
            ["gen0Collections"] = Number(window, "gen0Collections"),
            ["gen1Collections"] = Number(window, "gen1Collections"),
            ["gen2Collections"] = Number(window, "gen2Collections"),
            ["allocatedBytes"] = Number(window, "allocatedBytes"),
            ["peakWorkingSetBytes"] = Number(window, "peakWorkingSetBytes"),
            ["peakGcHeapBytes"] = Number(window, "peakGcHeapBytes"),
            ["peakGcCommittedBytes"] = Number(window, "peakGcCommittedBytes"),
            ["peakTileNativeBytes"] = Number(window, "peakTileNativeBytes"),
            ["workingSetBytes"] = Number(memory, "workingSetBytes"),
            ["gcHeapBytes"] = Number(memory, "gcHeapBytes"),
            ["gcCommittedBytes"] = Number(memory, "gcCommittedBytes"),
            ["tileNativeBytes"] = Number(memory, "tileNativeBytes"),
            ["schedulerCoresBusy"] = Number(scheduler, "coresBusy"),
            ["visibleWaitP95Ms"] = Number(scheduler?["visibleWait"], "p95Ms"),
            ["visibleWaitMaxMs"] = Number(scheduler?["visibleWait"], "maxMs"),
            ["visibleJobs"] = Number(scheduler?["visibleWait"], "count"),
            ["jobRunP95Ms"] = Number(scheduler?["run"], "p95Ms"),
            ["frameCount"] = Number(frames, "count"),
            ["frameP50Ms"] = Number(frames, "frameP50Ms"),
            ["frameP95Ms"] = Number(frames, "frameP95Ms"),
            ["frameMaxMs"] = Number(frames, "frameMaxMs"),
            ["loadedCells"] = Number(settle?["loads"], "loaded"),
            ["raw"] = new JsonObject
            {
                ["settle"] = settle?.DeepClone(),
                ["metrics"] = metrics?.DeepClone(),
                ["renderStats"] = renderStats?.DeepClone(),
            },
        };
    }

    /// <summary>
    /// Totals over the steps: summed settle, CPU, wall, GC pause and
    /// allocation; cores busy and pause share over the summed wall time; the
    /// largest settle, peaks, frame times and scheduler job-wait p95 (the
    /// largest step p95, not a pooled one). <c>settleMs</c> is null when any
    /// step timed out.
    /// </summary>
    public static JsonObject Totals(JsonArray steps)
    {
        double settle = 0, maxSettle = 0, cpu = 0, wall = 0, pause = 0, allocated = 0, gen2 = 0;
        double peakWs = 0, peakHeap = 0, peakCommitted = 0, peakTiles = 0, frameP95 = 0, frameMax = 0;
        double schedulerBusy = 0, visibleWaitP95 = 0, visibleWaitMax = 0, jobRunP95 = 0;
        var timedOut = 0;
        foreach (var step in steps)
        {
            if (Number(step, "settleMs") is { } s)
            {
                settle += s;
                maxSettle = Math.Max(maxSettle, s);
            }
            else
            {
                timedOut++;
            }

            cpu += Number(step, "cpuMs") ?? 0;
            wall += Number(step, "wallMs") ?? 0;
            pause += Number(step, "gcPauseMs") ?? 0;
            allocated += Number(step, "allocatedBytes") ?? 0;
            gen2 += Number(step, "gen2Collections") ?? 0;
            peakWs = Math.Max(peakWs, Number(step, "peakWorkingSetBytes") ?? 0);
            peakHeap = Math.Max(peakHeap, Number(step, "peakGcHeapBytes") ?? 0);
            peakCommitted = Math.Max(peakCommitted, Number(step, "peakGcCommittedBytes") ?? 0);
            peakTiles = Math.Max(peakTiles, Number(step, "peakTileNativeBytes") ?? 0);
            frameP95 = Math.Max(frameP95, Number(step, "frameP95Ms") ?? 0);
            frameMax = Math.Max(frameMax, Number(step, "frameMaxMs") ?? 0);
            schedulerBusy += (Number(step, "schedulerCoresBusy") ?? 0) * (Number(step, "wallMs") ?? 0);
            visibleWaitP95 = Math.Max(visibleWaitP95, Number(step, "visibleWaitP95Ms") ?? 0);
            visibleWaitMax = Math.Max(visibleWaitMax, Number(step, "visibleWaitMaxMs") ?? 0);
            jobRunP95 = Math.Max(jobRunP95, Number(step, "jobRunP95Ms") ?? 0);
        }

        return new JsonObject
        {
            ["steps"] = steps.Count,
            ["timedOut"] = timedOut,
            ["settleMs"] = timedOut == 0 ? settle : null,
            ["maxSettleMs"] = maxSettle,
            ["cpuMs"] = cpu,
            ["wallMs"] = wall,
            ["coresBusy"] = wall > 0 ? cpu / wall : 0,
            ["gcPauseMs"] = pause,
            ["gcPauseShare"] = wall > 0 ? pause / wall : 0,
            ["gen2Collections"] = gen2,
            ["allocatedBytes"] = allocated,
            ["peakWorkingSetBytes"] = peakWs,
            ["peakGcHeapBytes"] = peakHeap,
            ["peakGcCommittedBytes"] = peakCommitted,
            ["peakTileNativeBytes"] = peakTiles,
            ["frameP95Ms"] = frameP95,
            ["frameMaxMs"] = frameMax,
            ["schedulerCoresBusy"] = wall > 0 ? schedulerBusy / wall : 0,
            ["visibleWaitP95Ms"] = visibleWaitP95,
            ["visibleWaitMaxMs"] = visibleWaitMax,
            ["jobRunP95Ms"] = jobRunP95,
        };
    }

    /// <summary>A short label for a manifest: route, mode and engine.</summary>
    public static string Label(JsonNode manifest) =>
        $"{Text(manifest, "route")} · {Text(manifest, "mode")} · {Text(manifest, "engine")}";

    /// <summary>Writes the Markdown summary of one manifest.</summary>
    public static void WriteSummary(TextWriter writer, JsonNode manifest, string sourcePath)
    {
        writer.WriteLine($"# Settle run: {Label(manifest)}");
        writer.WriteLine();
        writer.WriteLine($"- **Source:** `{Path.GetFileName(sourcePath)}`");
        writer.WriteLine($"- **Started:** {Text(manifest, "startedAtUtc")}");
        if (Number(manifest, "scaleDenominator") is { } scale)
            writer.WriteLine($"- **Scale:** 1:{FormatNumber(scale, "F0")}");
        var viewer = manifest["viewer"];
        if (viewer is not null)
        {
            writer.WriteLine(
                $"- **Viewer:** Lua {Text(viewer, "luaEngine")}, {(Bool(viewer, "serverGc") == true ? "server" : "workstation")} GC, "
                + $"{Text(viewer, "processorCount")} cores, {Text(viewer, "os")} {Text(viewer, "architecture")}, "
                + $"{Text(viewer, "runtime")}{(Bool(viewer, "optimized") == false ? ", **Debug build**" : string.Empty)}"
                + $"{(Bool(viewer, "appNapDisabled") == false && Text(viewer, "os").Contains("Darwin", StringComparison.OrdinalIgnoreCase) ? ", **App Nap on**" : string.Empty)}");
        }

        if (manifest["prewarm"] is { } prewarm)
        {
            writer.WriteLine(
                $"- **Pre-warm:** {Text(prewarm, "state")}, {Text(prewarm, "warm")} of {Text(prewarm, "total")} cells, "
                + $"waited {FormatMs(Number(prewarm, "waitedMs"))}");
        }

        writer.WriteLine();
        writer.WriteLine("| Step | Settled | Settle | CPU | Cores busy | GC pause | Allocated | Peak WS | Peak GC committed | Peak tile native | Frame p50/p95/max |");
        writer.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var step in Steps(manifest))
        {
            writer.WriteLine(
                $"| {Text(step, "name")} | {(Bool(step, "settled") == true ? "yes" : "**timed out**")} "
                + $"| {FormatMs(Number(step, "settleMs"))} | {FormatMs(Number(step, "cpuMs"))} "
                + $"| {FormatNumber(Number(step, "coresBusy"), "F1")} "
                + $"| {FormatPercent(Number(step, "gcPauseShare"))} "
                + $"| {FormatBytes(Number(step, "allocatedBytes"))} | {FormatBytes(Number(step, "peakWorkingSetBytes"))} "
                + $"| {FormatBytes(Number(step, "peakGcCommittedBytes"))} | {FormatBytes(Number(step, "peakTileNativeBytes"))} "
                + $"| {FormatNumber(Number(step, "frameP50Ms"), "F1")} / {FormatNumber(Number(step, "frameP95Ms"), "F1")} / {FormatNumber(Number(step, "frameMaxMs"), "F1")} ms |");
        }

        if (manifest["totals"] is { } totals)
        {
            writer.WriteLine(
                $"| **Total** | {Text(totals, "timedOut")} timed out | {FormatMs(Number(totals, "settleMs"))} "
                + $"| {FormatMs(Number(totals, "cpuMs"))} | {FormatNumber(Number(totals, "coresBusy"), "F1")} "
                + $"| {FormatPercent(Number(totals, "gcPauseShare"))} | {FormatBytes(Number(totals, "allocatedBytes"))} "
                + $"| {FormatBytes(Number(totals, "peakWorkingSetBytes"))} | {FormatBytes(Number(totals, "peakGcCommittedBytes"))} "
                + $"| {FormatBytes(Number(totals, "peakTileNativeBytes"))} | max {FormatNumber(Number(totals, "frameMaxMs"), "F1")} ms |");
        }

        writer.WriteLine();
        writer.WriteLine("Settle is the time from `set_viewport` to the frame that completed the view. CPU, cores busy, GC pause and allocation cover each step's window; peaks are sampled every 100 ms.");
        writer.WriteLine();
        writer.WriteLine("---");
        writer.WriteLine("*Generated by EncDotNet.S100.PerfReport*");
        writer.Flush();
    }

    /// <summary>
    /// Writes a Markdown comparison of two manifests, step by step and in
    /// total, for example the same route on MoonSharp and Lua-CSharp.
    /// </summary>
    public static void WriteDiff(TextWriter writer, JsonNode baseline, JsonNode candidate, string baselinePath, string candidatePath)
    {
        writer.WriteLine("# Settle run comparison");
        writer.WriteLine();
        writer.WriteLine($"- **Baseline:** `{Path.GetFileName(baselinePath)}` ({Label(baseline)})");
        writer.WriteLine($"- **Candidate:** `{Path.GetFileName(candidatePath)}` ({Label(candidate)})");
        writer.WriteLine();

        var baseSteps = Steps(baseline).ToList();
        var candSteps = Steps(candidate).ToList();
        var names = baseSteps.Select(s => Text(s, "name"))
            .Concat(candSteps.Select(s => Text(s, "name")))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var name in names)
        {
            var b = baseSteps.FirstOrDefault(s => Text(s, "name") == name);
            var c = candSteps.FirstOrDefault(s => Text(s, "name") == name);
            writer.WriteLine($"## {name}");
            writer.WriteLine();
            WriteMetricTable(writer, b, c);
        }

        writer.WriteLine("## Total");
        writer.WriteLine();
        WriteMetricTable(writer, baseline["totals"], candidate["totals"]);
        writer.WriteLine("Status: ✅ better by 10% or more, ❌ worse by 5% or more, ▫️ otherwise. Cores busy is better when higher.");
        writer.WriteLine();
        writer.WriteLine("---");
        writer.WriteLine("*Generated by EncDotNet.S100.PerfReport*");
        writer.Flush();
    }

    private static void WriteMetricTable(TextWriter writer, JsonNode? baseline, JsonNode? candidate)
    {
        writer.WriteLine("| Metric | Baseline | Candidate | Delta | Status |");
        writer.WriteLine("|---|---|---|---|---|");
        foreach (var (field, heading, unit, lowerIsBetter) in StepMetrics)
        {
            var b = Number(baseline, field);
            var c = Number(candidate, field);
            if (b is null && c is null)
                continue;
            var (delta, status) = Compare(b, c, lowerIsBetter);
            writer.WriteLine($"| {heading} | {Format(b, unit)} | {Format(c, unit)} | {delta} | {status} |");
        }

        writer.WriteLine();
    }

    /// <summary>The relative change from <paramref name="baseline"/> to <paramref name="candidate"/> and its status.</summary>
    internal static (string Delta, string Status) Compare(double? baseline, double? candidate, bool lowerIsBetter)
    {
        if (baseline is not { } b || candidate is not { } c || b == 0)
            return ("N/A", "▫️");
        var pct = (c - b) / b * 100;
        var delta = pct.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "%";
        var worse = lowerIsBetter ? pct : -pct;
        if (worse >= 5)
            return (delta, "❌");
        if (worse <= -10)
            return (delta, "✅");
        return (delta, "▫️");
    }

    private static IEnumerable<JsonNode> Steps(JsonNode manifest) =>
        manifest["steps"] is JsonArray steps ? steps.Where(s => s is not null)!.Cast<JsonNode>() : [];

    internal static double? Number(JsonNode? node, string field)
    {
        if (node?[field] is not JsonValue value)
            return null;
        if (value.TryGetValue<double>(out var d))
            return d;
        if (value.TryGetValue<long>(out var l))
            return l;
        if (value.TryGetValue<int>(out var i))
            return i;
        return null;
    }

    private static bool? Bool(JsonNode? node, string field) =>
        node?[field] is JsonValue value && value.TryGetValue<bool>(out var b) ? b : null;

    private static string Text(JsonNode? node, string field) =>
        node?[field] switch
        {
            null => "?",
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            var other => other.ToJsonString(),
        };

    private static string Format(double? value, string unit) => unit switch
    {
        "ms" => FormatMs(value),
        "bytes" => FormatBytes(value),
        "%" => FormatPercent(value),
        "x" => FormatNumber(value, "F1"),
        _ => FormatNumber(value, "F0"),
    };

    internal static string FormatMs(double? ms) =>
        ms is not { } v ? "—"
        : v >= 10_000 ? (v / 1000).ToString("F1", CultureInfo.InvariantCulture) + " s"
        : v.ToString("F0", CultureInfo.InvariantCulture) + " ms";

    internal static string FormatBytes(double? bytes) =>
        bytes is not { } v ? "—"
        : v >= 1024d * 1024 * 1024 ? (v / (1024d * 1024 * 1024)).ToString("F2", CultureInfo.InvariantCulture) + " GB"
        : (v / (1024d * 1024)).ToString("F0", CultureInfo.InvariantCulture) + " MB";

    private static string FormatPercent(double? share) =>
        share is not { } v ? "—" : (v * 100).ToString("F1", CultureInfo.InvariantCulture) + "%";

    private static string FormatNumber(double? value, string format) =>
        value is not { } v ? "—" : v.ToString(format, CultureInfo.InvariantCulture);
}
