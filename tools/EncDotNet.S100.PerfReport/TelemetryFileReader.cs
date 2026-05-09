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
    /// Reads all lines from the specified <c>.jsonl</c> file.
    /// </summary>
    public static TelemetryFileReader Read(string path)
    {
        var reader = new TelemetryFileReader();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
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
