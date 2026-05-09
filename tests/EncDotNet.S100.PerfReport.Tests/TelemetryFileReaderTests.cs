using System.Text.Json;
using EncDotNet.S100.PerfReport;

namespace EncDotNet.S100.PerfReport.Tests;

public class TelemetryFileReaderTests : IDisposable
{
    private readonly string _tempDir;

    public TelemetryFileReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "perfreport-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ReaderParsesHeaderSpansAndMetrics()
    {
        var path = Path.Combine(_tempDir, "test.jsonl");
        File.WriteAllLines(path,
        [
            """{"kind":"header","version":1,"startedAtUtc":"2026-05-09T00:00:00Z"}""",
            """{"kind":"span","name":"s100.pipeline.vector.process","traceId":"aaa","spanId":"bbb","durationMs":42.5,"tags":{"s100.product":"S-124"}}""",
            """{"kind":"span","name":"s100.pipeline.vector.stage.xslt","traceId":"aaa","spanId":"ccc","parentSpanId":"bbb","durationMs":12.1,"tags":{}}""",
            """{"kind":"metric","name":"s100.pipeline.duration","instrument":"histogram","unit":"ms","tags":{"s100.product":"S-124"},"buckets":[{"sum":42.5,"count":1,"min":42.5,"max":42.5}]}""",
            """{"kind":"metric","name":"s100.xslt.cache.miss.count","instrument":"counter","unit":"{misses}","tags":{},"value":3}""",
        ]);

        var data = TelemetryFileReader.Read(path);

        Assert.NotNull(data.Header);
        Assert.Equal(2, data.Spans.Count);
        Assert.Equal(2, data.Metrics.Count);

        // Verify span parent-child.
        var child = data.Spans.First(s => s.Name == "s100.pipeline.vector.stage.xslt");
        Assert.Equal("bbb", child.ParentSpanId);
        Assert.Equal(12.1, child.DurationMs);

        // Verify histogram.
        var hist = data.Metrics.First(m => m.Instrument == "histogram");
        Assert.Equal("s100.pipeline.duration", hist.Name);
        Assert.Equal(42.5, hist.BucketSum);
        Assert.Equal(1, hist.BucketCount);

        // Verify counter.
        var counter = data.Metrics.First(m => m.Instrument == "counter");
        Assert.Equal(3, counter.Value);
    }

    [Fact]
    public void SummariseProducesMarkdown()
    {
        var path = Path.Combine(_tempDir, "summary-input.jsonl");
        File.WriteAllLines(path,
        [
            """{"kind":"header","version":1,"startedAtUtc":"2026-05-09T00:00:00Z"}""",
            """{"kind":"span","name":"test.op","traceId":"aaa","spanId":"bbb","durationMs":10.0,"tags":{}}""",
        ]);

        var data = TelemetryFileReader.Read(path);
        var outputPath = Path.Combine(_tempDir, "summary.md");

        using (var writer = new StreamWriter(outputPath))
        {
            SummariseCommand.WriteSummary(writer, data, path);
        }

        var content = File.ReadAllText(outputPath);
        Assert.Contains("# Telemetry Summary", content);
        Assert.Contains("test.op", content);
    }

    [Fact]
    public void DiffProducesMarkdown()
    {
        var basePath = Path.Combine(_tempDir, "baseline.jsonl");
        var candPath = Path.Combine(_tempDir, "candidate.jsonl");

        File.WriteAllLines(basePath,
        [
            """{"kind":"header","version":1}""",
            """{"kind":"span","name":"test.op","traceId":"a","spanId":"b","durationMs":100.0,"tags":{}}""",
        ]);
        File.WriteAllLines(candPath,
        [
            """{"kind":"header","version":1}""",
            """{"kind":"span","name":"test.op","traceId":"c","spanId":"d","durationMs":110.0,"tags":{}}""",
        ]);

        var baseline = TelemetryFileReader.Read(basePath);
        var candidate = TelemetryFileReader.Read(candPath);
        var outputPath = Path.Combine(_tempDir, "diff.md");

        using (var writer = new StreamWriter(outputPath))
        {
            DiffCommand.WriteDiff(writer, baseline, candidate, basePath, candPath);
        }

        var content = File.ReadAllText(outputPath);
        Assert.Contains("# Performance Diff", content);
        Assert.Contains("test.op", content);
        Assert.Contains("❌", content); // 10% regression should be flagged
    }

    [Fact]
    public void FormatDeltaCategories()
    {
        // Stable (< 5%)
        var (_, status1) = DiffCommand.FormatDelta(100, 103);
        Assert.Equal("▫️", status1);

        // Regression (≥ 5%)
        var (_, status2) = DiffCommand.FormatDelta(100, 106);
        Assert.Equal("❌", status2);

        // Win (≤ -10%)
        var (_, status3) = DiffCommand.FormatDelta(100, 89);
        Assert.Equal("✅", status3);
    }
}
