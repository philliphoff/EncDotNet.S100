using EncDotNet.S100.PerfReport;

namespace EncDotNet.S100.PerfReport.Tests;

public class GateCommandTests : IDisposable
{
    private readonly string _tempDir;

    public GateCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "gatecmd-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void EvaluateScenario_HandlesDuplicateMetricRecordsFromMultipleExportCycles()
    {
        // The OTel periodic exporter emits one record per cycle (cumulative
        // temporality). For long-running scenarios this produces multiple
        // JSONL lines for the same (name, tags) tuple. The gate command must
        // not crash with "An item with the same key has already been added"
        // — it should keep the last (most recent / highest cumulative) value.
        var basePath = Path.Combine(_tempDir, "scenario.jsonl");
        var candPath = Path.Combine(_tempDir, "candidate.jsonl");

        // Two exports of the same histogram with the same tags; second is
        // the cumulative total.
        File.WriteAllLines(basePath,
        [
            """{"kind":"metric","name":"s100.pipeline.duration","instrument":"histogram","tags":{"s100.pipeline.stage":"vector"},"buckets":[{"sum":100.0,"count":1}]}""",
            """{"kind":"metric","name":"s100.pipeline.duration","instrument":"histogram","tags":{"s100.pipeline.stage":"vector"},"buckets":[{"sum":300.0,"count":3}]}""",
        ]);
        File.WriteAllLines(candPath,
        [
            """{"kind":"metric","name":"s100.pipeline.duration","instrument":"histogram","tags":{"s100.pipeline.stage":"vector"},"buckets":[{"sum":120.0,"count":1}]}""",
            """{"kind":"metric","name":"s100.pipeline.duration","instrument":"histogram","tags":{"s100.pipeline.stage":"vector"},"buckets":[{"sum":330.0,"count":3}]}""",
        ]);

        var baseline = TelemetryFileReader.Read(basePath);
        var candidate = TelemetryFileReader.Read(candPath);

        var result = GateCommand.EvaluateScenario("scenario", baseline, candidate, threshold: 20.0);

        var detail = Assert.Single(result.Details, d => d.Kind == "metric");
        Assert.Equal(300.0, detail.Baseline);
        Assert.Equal(330.0, detail.Candidate);
        Assert.False(detail.Regressed); // 10% < 20% threshold
    }
}
