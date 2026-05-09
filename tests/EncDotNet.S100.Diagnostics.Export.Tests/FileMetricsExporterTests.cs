using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using EncDotNet.S100.Diagnostics.Export;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace EncDotNet.S100.Diagnostics.Export.Tests;

public class FileMetricsExporterTests : IDisposable
{
    private readonly string _tempDir;

    public FileMetricsExporterTests()
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
    public void ExporterWritesHistogramSamples()
    {
        var path = Path.Combine(_tempDir, "metrics.jsonl");
        var meter = new Meter("test.metrics.histogram", "1.0.0");

        using (var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter("test.metrics.histogram")
            .AddReader(new PeriodicExportingMetricReader(
                new FileMetricsExporter(path),
                exportIntervalMilliseconds: 100))
            .Build())
        {
            var histogram = meter.CreateHistogram<double>("test.duration", "ms");
            histogram.Record(13.4, new KeyValuePair<string, object?>("product", "S-101"));
            histogram.Record(25.1, new KeyValuePair<string, object?>("product", "S-101"));

            // Allow time for at least one export cycle.
            Thread.Sleep(500);
        }

        var lines = File.ReadAllLines(path);
        Assert.NotEmpty(lines);

        var metricLine = lines
            .Select(l => JsonDocument.Parse(l))
            .FirstOrDefault(d =>
                d.RootElement.TryGetProperty("kind", out var k) &&
                k.GetString() == "metric");

        Assert.NotNull(metricLine);

        var root = metricLine.RootElement;
        Assert.Equal("test.duration", root.GetProperty("name").GetString());
        Assert.Equal("histogram", root.GetProperty("instrument").GetString());
        Assert.Equal("ms", root.GetProperty("unit").GetString());

        metricLine.Dispose();
    }

    [Fact]
    public void ExporterWritesCounterSamples()
    {
        var path = Path.Combine(_tempDir, "counters.jsonl");
        var meter = new Meter("test.metrics.counter", "1.0.0");

        using (var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter("test.metrics.counter")
            .AddReader(new PeriodicExportingMetricReader(
                new FileMetricsExporter(path),
                exportIntervalMilliseconds: 100))
            .Build())
        {
            var counter = meter.CreateCounter<long>("test.cache.hit.count", "{hits}");
            counter.Add(5, new KeyValuePair<string, object?>("cache", "symbol"));
            counter.Add(3, new KeyValuePair<string, object?>("cache", "symbol"));

            Thread.Sleep(500);
        }

        var lines = File.ReadAllLines(path);
        Assert.NotEmpty(lines);

        var metricLine = lines
            .Select(l => JsonDocument.Parse(l))
            .FirstOrDefault(d =>
                d.RootElement.TryGetProperty("kind", out var k) &&
                k.GetString() == "metric" &&
                d.RootElement.GetProperty("name").GetString() == "test.cache.hit.count");

        Assert.NotNull(metricLine);

        var root = metricLine.RootElement;
        Assert.Equal("counter", root.GetProperty("instrument").GetString());
        Assert.True(root.GetProperty("value").GetInt64() >= 8);

        metricLine.Dispose();
    }
}
