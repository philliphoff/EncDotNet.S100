using System.Diagnostics;
using System.Text.Json;
using EncDotNet.S100.Diagnostics.Export;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace EncDotNet.S100.Diagnostics.Export.Tests;

public class FileTelemetryExporterTests : IDisposable
{
    private readonly string _tempDir;

    public FileTelemetryExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "encdotnet-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ExporterWritesHeaderLineOnStart()
    {
        var path = Path.Combine(_tempDir, "header.jsonl");

        using (var exporter = new FileTelemetryExporter(path))
        {
            // Give the writer thread time to flush.
            Thread.Sleep(200);
        }

        var lines = File.ReadAllLines(path);
        Assert.NotEmpty(lines);

        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("header", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("startedAtUtc", out _));
    }

    [Fact]
    public void ExporterWritesSpanWithParentChild()
    {
        var path = Path.Combine(_tempDir, "spans.jsonl");
        var source = new ActivitySource("test.exporter.spans");

        using (var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("test.exporter.spans")
            .AddFileExporter(path)
            .Build())
        {
            using (var parent = source.StartActivity("parent-op"))
            {
                parent?.SetTag("s100.product", "S-101");
                using (var child = source.StartActivity("child-op"))
                {
                    child?.SetTag("s100.pipeline.stage", "lua");
                }
            }
        }

        var lines = File.ReadAllLines(path);
        // header + 2 spans (child exported first on close, then parent)
        Assert.True(lines.Length >= 3, $"Expected ≥3 lines, got {lines.Length}");

        var spanLines = lines.Skip(1).Select(l => JsonDocument.Parse(l)).ToList();
        try
        {
            var spans = spanLines
                .Where(d => d.RootElement.GetProperty("kind").GetString() == "span")
                .ToList();

            Assert.True(spans.Count >= 2, $"Expected ≥2 span lines, got {spans.Count}");

            // Verify each span has required fields.
            foreach (var span in spans)
            {
                var root = span.RootElement;
                Assert.True(root.TryGetProperty("name", out _));
                Assert.True(root.TryGetProperty("traceId", out _));
                Assert.True(root.TryGetProperty("spanId", out _));
                Assert.True(root.TryGetProperty("startUnixNs", out _));
                Assert.True(root.TryGetProperty("endUnixNs", out _));
                Assert.True(root.TryGetProperty("durationMs", out _));
            }

            // Find the child span and verify its parentSpanId is set.
            var childSpan = spans.FirstOrDefault(
                d => d.RootElement.GetProperty("name").GetString() == "child-op");
            Assert.NotNull(childSpan);
            Assert.True(
                childSpan.RootElement.TryGetProperty("parentSpanId", out var parentId) &&
                parentId.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(parentId.GetString()));
        }
        finally
        {
            foreach (var doc in spanLines) doc.Dispose();
        }
    }

    [Fact]
    public void GracefulDisposeDoesNotTruncateTrailingLine()
    {
        var path = Path.Combine(_tempDir, "graceful.jsonl");
        var source = new ActivitySource("test.exporter.graceful");

        using (var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("test.exporter.graceful")
            .AddFileExporter(path)
            .Build())
        {
            for (int i = 0; i < 10; i++)
            {
                using var activity = source.StartActivity($"op-{i}");
                activity?.SetTag("index", i);
            }
        }

        var text = File.ReadAllText(path);
        // Verify no truncated line at end: last char should be newline,
        // and every non-empty line should parse as valid JSON.
        Assert.EndsWith("\n", text);

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("kind", out _));
        }
    }
}
