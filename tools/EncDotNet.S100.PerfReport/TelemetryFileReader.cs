using System.Text.Json;

namespace EncDotNet.S100.PerfReport;

/// <summary>
/// Reads a <c>.jsonl</c> telemetry file produced by the PerfRunner
/// and builds an in-memory model of spans and metrics.
/// </summary>
public sealed class TelemetryFileReader
{
    /// <summary>Parsed span records.</summary>
    public List<SpanRecord> Spans { get; } = [];

    /// <summary>Parsed metric records.</summary>
    public List<MetricRecord> Metrics { get; } = [];

    /// <summary>Header metadata (version, start time, etc.).</summary>
    public JsonElement? Header { get; private set; }

    /// <summary>
    /// Iteration samples extracted from <c>perf.iteration</c> spans. One
    /// entry per measured iteration emitted by the perf runner. When the
    /// list is non-empty, downstream gating prefers per-iteration
    /// statistics (median + MAD) over span-sum totals.
    /// </summary>
    public IReadOnlyList<IterationSample> Iterations
    {
        get
        {
            // The activity name and tag key constants must stay in sync
            // with EncDotNet.S100.PerfRunner.PerfActivitySource. They are
            // duplicated here as string literals because PerfReport must
            // not take a project reference on PerfRunner (one is a
            // standalone .NET tool, the other an executable).
            const string IterationActivityName = "perf.iteration";
            const string ScenarioTag = "perf.scenario";
            const string RoundTag = "perf.round";
            const string IterTag = "perf.iter";
            const string SideTag = "perf.side";

            var samples = new List<IterationSample>(Spans.Count);
            foreach (var span in Spans)
            {
                if (span.Name != IterationActivityName) continue;
                var scenario = span.Tags.GetValueOrDefault(ScenarioTag) ?? "";
                int round = ParseInt(span.Tags.GetValueOrDefault(RoundTag), 1);
                int index = ParseInt(span.Tags.GetValueOrDefault(IterTag), 0);
                var side = span.Tags.GetValueOrDefault(SideTag);
                samples.Add(new IterationSample(scenario, round, index, side, span.DurationMs));
            }
            return samples;
        }
    }

    private static int ParseInt(string? s, int fallback) =>
        int.TryParse(s, out var v) ? v : fallback;

    /// <summary>
    /// Reads all lines from the specified <c>.jsonl</c> file.
    /// </summary>
    public static TelemetryFileReader Read(string path)
    {
        var reader = new TelemetryFileReader();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                // Skip corrupt lines caused by concurrent file writes.
                continue;
            }

            using var _ = doc;
            var root = doc.RootElement;

            if (!root.TryGetProperty("kind", out var kindProp)) continue;
            var kind = kindProp.GetString();

            switch (kind)
            {
                case "header":
                    reader.Header = root.Clone();
                    break;

                case "span":
                    reader.Spans.Add(new SpanRecord
                    {
                        Name = root.GetProperty("name").GetString() ?? "",
                        TraceId = root.GetProperty("traceId").GetString() ?? "",
                        SpanId = root.GetProperty("spanId").GetString() ?? "",
                        ParentSpanId = root.TryGetProperty("parentSpanId", out var p) ? p.GetString() : null,
                        DurationMs = root.TryGetProperty("durationMs", out var d) ? d.GetDouble() : 0,
                        Tags = ReadTags(root),
                    });
                    break;

                case "metric":
                    var mr = new MetricRecord
                    {
                        Name = root.GetProperty("name").GetString() ?? "",
                        Instrument = root.TryGetProperty("instrument", out var inst) ? inst.GetString() ?? "" : "",
                        Unit = root.TryGetProperty("unit", out var u) ? u.GetString() : null,
                        Tags = ReadTags(root),
                    };

                    if (root.TryGetProperty("value", out var val))
                    {
                        mr.Value = val.ValueKind == JsonValueKind.Number ? val.GetDouble() : null;
                    }

                    if (root.TryGetProperty("buckets", out var buckets) && buckets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var bucket in buckets.EnumerateArray())
                        {
                            mr.BucketSum = bucket.TryGetProperty("sum", out var s) ? s.GetDouble() : null;
                            mr.BucketCount = bucket.TryGetProperty("count", out var c) ? c.GetInt64() : null;
                            mr.BucketMin = bucket.TryGetProperty("min", out var mn) ? mn.GetDouble() : null;
                            mr.BucketMax = bucket.TryGetProperty("max", out var mx) ? mx.GetDouble() : null;
                        }
                    }

                    reader.Metrics.Add(mr);
                    break;
            }
        }

        return reader;
    }

    private static Dictionary<string, string> ReadTags(JsonElement root)
    {
        var tags = new Dictionary<string, string>();
        if (root.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in tagsEl.EnumerateObject())
            {
                tags[prop.Name] = prop.Value.GetString() ?? prop.Value.ToString();
            }
        }
        return tags;
    }
}

/// <summary>A single span record from the telemetry file.</summary>
public sealed class SpanRecord
{
    public string Name { get; init; } = "";
    public string TraceId { get; init; } = "";
    public string SpanId { get; init; } = "";
    public string? ParentSpanId { get; init; }
    public double DurationMs { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
}

/// <summary>
/// One measured-iteration sample, derived from a <c>perf.iteration</c>
/// span emitted by <c>EncDotNet.S100.PerfRunner</c>. Used by the
/// performance gate to compute median + MAD per scenario.
/// </summary>
public sealed record IterationSample(string Scenario, int Round, int Index, string? Side, double DurationMs);

/// <summary>A single metric record from the telemetry file.</summary>
public sealed class MetricRecord
{
    public string Name { get; init; } = "";
    public string Instrument { get; init; } = "";
    public string? Unit { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
    public double? Value { get; set; }
    public double? BucketSum { get; set; }
    public long? BucketCount { get; set; }
    public double? BucketMin { get; set; }
    public double? BucketMax { get; set; }
}
