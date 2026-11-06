using System.Text.Json.Nodes;

namespace EncDotNet.S100.PerfReport.Tests;

/// <summary>The settle-run manifest: flattening, totals, summary and diff (map rendering slice 1).</summary>
public sealed class SettleReportTests
{
    [Fact]
    public void Flatten_ReadsTheViewersReplies()
    {
        var step = SettleReport.Flatten("Seattle", 47.6, -122.35, Settle(1500), Metrics(cpuMs: 8000, wallMs: 2000, allocated: 3_000_000_000), Frames());

        Assert.Equal("Seattle", step["name"]!.GetValue<string>());
        Assert.True(step["settled"]!.GetValue<bool>());
        Assert.Equal(1500, SettleReport.Number(step, "settleMs"));
        Assert.Equal(4, SettleReport.Number(step, "coresBusy"));
        Assert.Equal(3_000_000_000, SettleReport.Number(step, "allocatedBytes"));
        Assert.Equal(7, SettleReport.Number(step, "frameP95Ms"));
        Assert.Equal(12, SettleReport.Number(step, "loadedCells"));
        Assert.NotNull(step["raw"]!["settle"]);
    }

    [Fact]
    public void Totals_SumAndPeak_AndNullSettleWhenAStepTimedOut()
    {
        var steps = new JsonArray
        {
            SettleReport.Flatten("a", 0, 0, Settle(1000), Metrics(cpuMs: 3000, wallMs: 1000, allocated: 10, peakWs: 5), Frames()),
            SettleReport.Flatten("b", 0, 0, Settle(2000), Metrics(cpuMs: 1000, wallMs: 3000, allocated: 20, peakWs: 9), Frames()),
        };

        var totals = SettleReport.Totals(steps);
        Assert.Equal(3000, SettleReport.Number(totals, "settleMs"));
        Assert.Equal(2000, SettleReport.Number(totals, "maxSettleMs"));
        Assert.Equal(1, SettleReport.Number(totals, "coresBusy"));
        Assert.Equal(30, SettleReport.Number(totals, "allocatedBytes"));
        Assert.Equal(9, SettleReport.Number(totals, "peakWorkingSetBytes"));

        steps.Add(SettleReport.Flatten("c", 0, 0, Settle(null), Metrics(1, 1, 1), Frames()));
        var withTimeout = SettleReport.Totals(steps);
        Assert.Null(SettleReport.Number(withTimeout, "settleMs"));
        Assert.Equal(1, SettleReport.Number(withTimeout, "timedOut"));
    }

    [Fact]
    public void Flatten_AndTotals_ReadTheSchedulerWindow()
    {
        var steps = new JsonArray
        {
            SettleReport.Flatten("a", 0, 0, Settle(1000), WithScheduler(Metrics(3000, 1000, 1), coresBusy: 2, visibleP95: 40, runP95: 30), Frames()),
            SettleReport.Flatten("b", 0, 0, Settle(1000), WithScheduler(Metrics(3000, 3000, 1), coresBusy: 4, visibleP95: 90, runP95: 20), Frames()),
        };

        Assert.Equal(40, SettleReport.Number(steps[0], "visibleWaitP95Ms"));
        Assert.Equal(5, SettleReport.Number(steps[0], "visibleJobs"));
        var totals = SettleReport.Totals(steps);
        Assert.Equal(3.5, SettleReport.Number(totals, "schedulerCoresBusy"));
        Assert.Equal(90, SettleReport.Number(totals, "visibleWaitP95Ms"));
        Assert.Equal(30, SettleReport.Number(totals, "jobRunP95Ms"));

        // A viewer without the scheduler window reports nothing, not zero.
        var old = SettleReport.Flatten("c", 0, 0, Settle(1000), Metrics(1, 1, 1), Frames());
        Assert.Null(SettleReport.Number(old, "visibleWaitP95Ms"));
    }

    private static JsonNode WithScheduler(JsonNode metrics, double coresBusy, double visibleP95, double runP95)
    {
        metrics["scheduler"] = new JsonObject
        {
            ["coresBusy"] = coresBusy,
            ["visibleWait"] = new JsonObject { ["count"] = 5, ["p95Ms"] = visibleP95, ["maxMs"] = visibleP95 * 2 },
            ["run"] = new JsonObject { ["count"] = 9, ["p95Ms"] = runP95 },
        };
        return metrics;
    }

    [Fact]
    public void WriteSummary_ListsEachStepAndTheTotal()
    {
        var manifest = Manifest("moonsharp", 1500);
        using var writer = new StringWriter();

        SettleReport.WriteSummary(writer, manifest, "/runs/a.json");

        var text = writer.ToString();
        Assert.Contains("# Settle run: jumps · cold · moonsharp", text);
        Assert.Contains("| Seattle | yes | 1500 ms |", text);
        Assert.Contains("| **Total** |", text);
        Assert.Contains("Lua moonsharp, server GC, 16 cores", text);
    }

    [Fact]
    public void WriteDiff_ComparesStepsAndTotals()
    {
        using var writer = new StringWriter();

        SettleReport.WriteDiff(writer, Manifest("moonsharp", 2000), Manifest("luacsharp", 1000), "/runs/m.json", "/runs/l.json");

        var text = writer.ToString();
        Assert.Contains("jumps · cold · moonsharp", text);
        Assert.Contains("jumps · cold · luacsharp", text);
        Assert.Contains("## Seattle", text);
        Assert.Contains("| Settle | 2000 ms | 1000 ms | -50.0% | ✅ |", text);
        Assert.Contains("## Total", text);
    }

    [Theory]
    [InlineData(100, 120, true, "❌")]
    [InlineData(100, 80, true, "✅")]
    [InlineData(100, 102, true, "▫️")]
    [InlineData(4, 8, false, "✅")]
    [InlineData(4, 3, false, "❌")]
    public void Compare_RespectsDirection(double baseline, double candidate, bool lowerIsBetter, string status) =>
        Assert.Equal(status, SettleReport.Compare(baseline, candidate, lowerIsBetter).Status);

    [Fact]
    public void IsSettleManifestFile_RecognisesOnlyManifests()
    {
        var dir = Directory.CreateTempSubdirectory("settle-report-");
        try
        {
            var manifest = Path.Combine(dir.FullName, "run.json");
            File.WriteAllText(manifest, Manifest("luacsharp", 10).ToJsonString());
            var other = Path.Combine(dir.FullName, "other.json");
            File.WriteAllText(other, """{"schemaVersion":1}""");
            var jsonl = Path.Combine(dir.FullName, "run.jsonl");
            File.WriteAllText(jsonl, """{"kind":"header"}""");

            Assert.True(SettleReport.IsSettleManifestFile(manifest));
            Assert.False(SettleReport.IsSettleManifestFile(other));
            Assert.False(SettleReport.IsSettleManifestFile(jsonl));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Commands_SummariseAndDiffManifests()
    {
        var dir = Directory.CreateTempSubdirectory("settle-cmd-");
        try
        {
            var a = Path.Combine(dir.FullName, "a.json");
            var b = Path.Combine(dir.FullName, "b.json");
            File.WriteAllText(a, Manifest("moonsharp", 2000).ToJsonString());
            File.WriteAllText(b, Manifest("luacsharp", 1000).ToJsonString());
            var summary = Path.Combine(dir.FullName, "a.md");
            var diff = Path.Combine(dir.FullName, "diff.md");

            Assert.Equal(0, new SummariseCommand().Execute(null!, new SummariseCommand.Settings { File = a, OutputPath = summary }, CancellationToken.None));
            Assert.Equal(0, new DiffCommand().Execute(null!, new DiffCommand.Settings { Baseline = a, Candidate = b, OutputPath = diff }, CancellationToken.None));

            Assert.Contains("Settle run", File.ReadAllText(summary));
            Assert.Contains("Settle run comparison", File.ReadAllText(diff));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static JsonObject Manifest(string engine, double settleMs)
    {
        var steps = new JsonArray
        {
            SettleReport.Flatten("Seattle", 47.6, -122.35, Settle(settleMs), Metrics(4000, 2000, 1_000_000), Frames()),
            SettleReport.Flatten("Tacoma", 47.27, -122.43, Settle(settleMs), Metrics(4000, 2000, 1_000_000), Frames()),
        };
        return new JsonObject
        {
            ["kind"] = SettleReport.Kind,
            ["schemaVersion"] = SettleReport.SchemaVersion,
            ["route"] = "jumps",
            ["mode"] = "cold",
            ["engine"] = engine,
            ["startedAtUtc"] = "2026-11-05T00:00:00Z",
            ["scaleDenominator"] = 44300,
            ["viewer"] = new JsonObject
            {
                ["luaEngine"] = engine,
                ["serverGc"] = true,
                ["processorCount"] = 16,
                ["os"] = "macOS",
                ["architecture"] = "Arm64",
                ["runtime"] = ".NET 10",
                ["optimized"] = true,
            },
            ["steps"] = steps,
            ["totals"] = SettleReport.Totals(steps),
        };
    }

    private static JsonNode Settle(double? settleMs) => new JsonObject
    {
        ["settled"] = settleMs is not null,
        ["timedOut"] = settleMs is null,
        ["settleMs"] = settleMs,
        ["waitedMs"] = (settleMs ?? 0) + 250,
        ["loads"] = new JsonObject { ["loaded"] = 12 },
    };

    private static JsonNode Metrics(double cpuMs, double wallMs, long allocated, long peakWs = 1) => new JsonObject
    {
        ["window"] = new JsonObject
        {
            ["wallMs"] = wallMs,
            ["cpuMs"] = cpuMs,
            ["coresBusy"] = cpuMs / wallMs,
            ["gcPauseMs"] = 10,
            ["gcPauseShare"] = 10 / wallMs,
            ["gen2Collections"] = 1,
            ["allocatedBytes"] = allocated,
            ["peakWorkingSetBytes"] = peakWs,
            ["peakGcCommittedBytes"] = 2,
            ["peakTileNativeBytes"] = 3,
        },
        ["memory"] = new JsonObject { ["workingSetBytes"] = 1 },
    };

    private static JsonNode Frames() => new JsonObject
    {
        ["window"] = new JsonObject { ["count"] = 20, ["frameP50Ms"] = 3, ["frameP95Ms"] = 7, ["frameMaxMs"] = 12 },
    };
}
