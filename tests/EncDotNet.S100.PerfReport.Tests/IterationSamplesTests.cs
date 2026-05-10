using EncDotNet.S100.PerfReport;

namespace EncDotNet.S100.PerfReport.Tests;

public class IterationSamplesTests : IDisposable
{
    private readonly string _tempDir;

    public IterationSamplesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "iter-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Iterations_ParsesPerfIterationSpans()
    {
        // Mix of perf.iteration spans (the iteration markers) and other
        // spans that should be ignored by the iteration accessor.
        var path = Path.Combine(_tempDir, "scenario.jsonl");
        File.WriteAllLines(path,
        [
            """{"kind":"header","version":1}""",
            """{"kind":"span","name":"perf.iteration","traceId":"a","spanId":"1","durationMs":105.5,"tags":{"perf.scenario":"s101","perf.round":"1","perf.iter":"0","perf.side":"baseline"}}""",
            """{"kind":"span","name":"perf.iteration","traceId":"b","spanId":"2","durationMs":110.2,"tags":{"perf.scenario":"s101","perf.round":"1","perf.iter":"1","perf.side":"baseline"}}""",
            """{"kind":"span","name":"perf.iteration","traceId":"c","spanId":"3","durationMs":104.8,"tags":{"perf.scenario":"s101","perf.round":"2","perf.iter":"0","perf.side":"baseline"}}""",
            """{"kind":"span","name":"s100.pipeline.vector","traceId":"a","spanId":"99","parentSpanId":"1","durationMs":100.0,"tags":{}}""",
        ]);

        var reader = TelemetryFileReader.Read(path);
        var iters = reader.Iterations;

        Assert.Equal(3, iters.Count);
        Assert.All(iters, i => Assert.Equal("s101", i.Scenario));
        Assert.All(iters, i => Assert.Equal("baseline", i.Side));
        Assert.Equal(new[] { 1, 1, 2 }, iters.Select(i => i.Round));
        Assert.Equal(new[] { 0, 1, 0 }, iters.Select(i => i.Index));
        Assert.Equal(new[] { 105.5, 110.2, 104.8 }, iters.Select(i => i.DurationMs));
    }

    [Fact]
    public void Iterations_EmptyWhenNoPerfIterationSpans()
    {
        var path = Path.Combine(_tempDir, "scenario.jsonl");
        File.WriteAllLines(path,
        [
            """{"kind":"header","version":1}""",
            """{"kind":"span","name":"s100.pipeline.vector","traceId":"a","spanId":"1","durationMs":100.0,"tags":{}}""",
        ]);

        var reader = TelemetryFileReader.Read(path);
        Assert.Empty(reader.Iterations);
    }

    [Fact]
    public void EvaluateScenario_UsesMedianMad_WhenIterationsPresent()
    {
        // Baseline: tight cluster around 100ms (median 100, MAD 1).
        // Candidate: noisy with one outlier at 500ms but the rest around 102.
        // With span-sum logic this would look like a huge regression
        // (sum 506 vs 510). With median-MAD logic the candidate median
        // is 102, baseline median 100, delta=2%, well below threshold.
        var basePath = Path.Combine(_tempDir, "baseline.jsonl");
        var candPath = Path.Combine(_tempDir, "candidate.jsonl");

        File.WriteAllLines(basePath, MakeIterationLines("scenario", "baseline",
            new[] { 99.0, 100, 100, 101, 100 }));
        File.WriteAllLines(candPath, MakeIterationLines("scenario", "candidate",
            new[] { 101.0, 102, 102, 103, 500 }));

        var baseline = TelemetryFileReader.Read(basePath);
        var candidate = TelemetryFileReader.Read(candPath);

        var result = GateCommand.EvaluateScenario(
            "scenario", baseline, candidate,
            threshold: 10.0, minAbsolute: 50.0,
            madK: 3.0, retryZoneMult: 2.0, minSamples: 5);

        Assert.NotNull(result.IterationStats);
        Assert.Equal(GateCommand.ScenarioStatus.Clean, result.Status);
        Assert.Equal(100, result.IterationStats!.BaselineMedian, precision: 1);
        Assert.Equal(102, result.IterationStats.CandidateMedian, precision: 1);
    }

    [Fact]
    public void EvaluateScenario_FallsBackToSpanSum_WhenIterationsAbsent()
    {
        // No perf.iteration spans → existing span-sum behaviour applies.
        // Baseline span totals 100ms, candidate totals 130ms = +30%.
        // With threshold 10% and minAbs 50ms this should regress.
        var basePath = Path.Combine(_tempDir, "baseline.jsonl");
        var candPath = Path.Combine(_tempDir, "candidate.jsonl");

        File.WriteAllLines(basePath,
        [
            """{"kind":"header","version":1}""",
            """{"kind":"span","name":"work","traceId":"a","spanId":"1","durationMs":100.0,"tags":{}}""",
        ]);
        File.WriteAllLines(candPath,
        [
            """{"kind":"header","version":1}""",
            """{"kind":"span","name":"work","traceId":"b","spanId":"2","durationMs":130.0,"tags":{}}""",
        ]);

        var baseline = TelemetryFileReader.Read(basePath);
        var candidate = TelemetryFileReader.Read(candPath);

        var result = GateCommand.EvaluateScenario(
            "scenario", baseline, candidate,
            threshold: 10.0, minAbsolute: 50.0,
            madK: 3.0, retryZoneMult: 2.0, minSamples: 5);

        Assert.Null(result.IterationStats);
        Assert.Equal(GateCommand.ScenarioStatus.Regressed, result.Status);
    }

    private static IEnumerable<string> MakeIterationLines(string scenario, string side, double[] durations)
    {
        yield return """{"kind":"header","version":1}""";
        for (int i = 0; i < durations.Length; i++)
        {
            yield return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{{\"kind\":\"span\",\"name\":\"perf.iteration\",\"traceId\":\"t{0}\",\"spanId\":\"s{0}\",\"durationMs\":{1},\"tags\":{{\"perf.scenario\":\"{2}\",\"perf.round\":\"1\",\"perf.iter\":\"{0}\",\"perf.side\":\"{3}\"}}}}",
                i, durations[i], scenario, side);
        }
    }
}
