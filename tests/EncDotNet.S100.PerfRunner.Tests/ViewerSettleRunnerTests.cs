using System.Collections.Concurrent;
using System.IO.Pipelines;
using EncDotNet.S100.PerfReport;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.PerfRunner.Tests;

/// <summary>
/// Drives <see cref="ViewerSettleRunner"/> against a fake viewer MCP server
/// over in-memory pipes, so the jumps and pan routes are checked without the
/// real viewer or the Washington data.
/// </summary>
public sealed class ViewerSettleRunnerTests
{
    [Fact]
    public async Task Jumps_Cold_SettlesEachStopAndWritesTotals()
    {
        var viewer = new FakeViewer();
        await using var harness = await Harness.StartAsync(viewer);

        var manifest = await ViewerSettleRunner.RunAsync(harness.Client, Options("jumps", SettleMode.Cold), TestContext.Current.CancellationToken);

        Assert.Equal(SettleReport.Kind, manifest["kind"]!.GetValue<string>());
        Assert.Equal("cold", manifest["mode"]!.GetValue<string>());
        Assert.Equal("moonsharp", manifest["engine"]!.GetValue<string>());
        var steps = manifest["steps"]!.AsArray();
        Assert.Equal(["Seattle", "Tacoma", "Port Townsend"], steps.Select(s => s!["name"]!.GetValue<string>()));
        Assert.All(steps, s => Assert.Equal(1234.5, s!["settleMs"]!.GetValue<double>()));
        Assert.Equal(3 * 1234.5, manifest["totals"]!["settleMs"]!.GetValue<double>(), 6);

        // Cold: the pre-warm is switched off and the Library source put on the map.
        Assert.Contains("portrayal_prewarm:off", viewer.Calls);
        Assert.Contains("set_library_source_options:src-1", viewer.Calls);
        Assert.Equal(3, viewer.Calls.Count(c => c.StartsWith("set_viewport", StringComparison.Ordinal)));
        Assert.Equal(44_300, viewer.Scales.Distinct().Single());
    }

    [Fact]
    public async Task Jumps_Prewarm_WaitsForThePrewarmToComplete()
    {
        var viewer = new FakeViewer { PrewarmPollsUntilComplete = 2 };
        await using var harness = await Harness.StartAsync(viewer);

        var manifest = await ViewerSettleRunner.RunAsync(harness.Client, Options("jumps", SettleMode.Prewarm), TestContext.Current.CancellationToken);

        Assert.Equal("prewarm", manifest["mode"]!.GetValue<string>());
        Assert.Equal("complete", manifest["prewarm"]!["state"]!.GetValue<string>());
        Assert.Contains("portrayal_prewarm:on", viewer.Calls);
    }

    [Fact]
    public async Task Pan_MovesInStepsThenRecordsOneStep()
    {
        var viewer = new FakeViewer();
        await using var harness = await Harness.StartAsync(viewer);

        var manifest = await ViewerSettleRunner.RunAsync(
            harness.Client, Options("pan", SettleMode.Cold) with { PanSteps = 5, PanStepDelayMs = 0 }, TestContext.Current.CancellationToken);

        Assert.Single(manifest["steps"]!.AsArray());
        // One move to the start, then five pan moves.
        Assert.Equal(6, viewer.Calls.Count(c => c.StartsWith("set_viewport", StringComparison.Ordinal)));
        Assert.Equal(5, manifest["pan"]!["steps"]!.GetValue<int>());
    }

    [Fact]
    public async Task WrongEngine_Fails()
    {
        var viewer = new FakeViewer();
        await using var harness = await Harness.StartAsync(viewer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ViewerSettleRunner.RunAsync(
            harness.Client, Options("jumps", SettleMode.Cold) with { ExpectedEngine = "luacsharp" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingLibrarySource_Fails()
    {
        var viewer = new FakeViewer();
        await using var harness = await Harness.StartAsync(viewer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ViewerSettleRunner.RunAsync(
            harness.Client, Options("jumps", SettleMode.Cold) with { LibrarySource = "Oregon" }, TestContext.Current.CancellationToken));
    }

    private static SettleRunOptions Options(string route, SettleMode mode) => new()
    {
        Route = route,
        Mode = mode,
        Jumps = SettleRoute.DefaultJumps,
        Pan = SettleRoute.DefaultPan,
        PrewarmTimeoutMs = 30_000,
    };

    /// <summary>The viewer's harness tools, answering with canned JSON.</summary>
    private sealed class FakeViewer
    {
        private int _prewarmPolls;

        public ConcurrentQueue<string> Calls { get; } = new();

        public ConcurrentQueue<double> Scales { get; } = new();

        public int PrewarmPollsUntilComplete { get; init; }

        public IEnumerable<McpServerTool> Tools()
        {
            yield return McpServerTool.Create(
                (string? window = null) =>
                {
                    Calls.Enqueue("get_process_metrics:" + window);
                    return """
                        {"window":{"wallMs":2000,"cpuMs":6000,"coresBusy":3,"cpuUtilisation":0.1875,"gcPauseMs":200,"gcPauseShare":0.1,
                                   "gen0Collections":5,"gen1Collections":2,"gen2Collections":1,"allocatedBytes":1000000,
                                   "allocationRateBytesPerSecond":500000,"peakWorkingSetBytes":3000000,"peakGcHeapBytes":1000000,
                                   "peakGcCommittedBytes":2000000,"peakTileNativeBytes":500000,"peakSamples":20},
                         "memory":{"gcHeapBytes":1,"gcCommittedBytes":2,"workingSetBytes":3,"privateBytes":4,"tileNativeBytes":5,"tileCount":6},
                         "frames":{"count":10,"p50Ms":2,"p95Ms":4,"maxMs":9},
                         "environment":{"luaEngine":"moonsharp","luaEnginePinned":true,"serverGc":true,"concurrentGc":true,
                                        "gcLatencyMode":"Interactive","processorCount":16,"os":"test","architecture":"Arm64",
                                        "runtime":".NET 10","renderCacheDirectory":"/tmp/cold","optimized":true}}
                        """;
                },
                new McpServerToolCreateOptions { Name = "get_process_metrics" });
            yield return McpServerTool.Create(
                (bool? counts = null) =>
                {
                    Calls.Enqueue("list_library_sources");
                    return """{"collections":[{"id":"col-1","name":"NOAA ENC","sources":[{"id":"src-1","name":"Washington","showOnMap":false}]}]}""";
                },
                new McpServerToolCreateOptions { Name = "list_library_sources" });
            yield return McpServerTool.Create(
                (string id, bool? showOnMap = null) =>
                {
                    Calls.Enqueue("set_library_source_options:" + id);
                    return "{}";
                },
                new McpServerToolCreateOptions { Name = "set_library_source_options" });
            yield return McpServerTool.Create(
                (int? timeoutMs = null) =>
                {
                    Calls.Enqueue("await_library_idle");
                    return """{"idle":true}""";
                },
                new McpServerToolCreateOptions { Name = "await_library_idle" });
            yield return McpServerTool.Create(
                (string? enabled = null) =>
                {
                    Calls.Enqueue("portrayal_prewarm:" + enabled);
                    var state = Interlocked.Increment(ref _prewarmPolls) > PrewarmPollsUntilComplete ? "complete" : "working";
                    return $$"""{"state":"{{state}}","warm":10,"total":10,"enabled":true}""";
                },
                new McpServerToolCreateOptions { Name = "portrayal_prewarm" });
            yield return McpServerTool.Create(
                (int? timeoutMs = null, int? confirmMs = null) =>
                {
                    Calls.Enqueue("await_settled");
                    return """
                        {"settled":true,"timedOut":false,"settleMs":1234.5,"waitedMs":1500,"triggerAgeMs":3,"hadTrigger":true,
                         "loads":{"evaluationPending":false,"queuedVisible":0,"runningVisible":0,"queuedOther":2,"runningOther":0,"loaded":12,"deferred":280},
                         "tiles":{"layersConsidered":30,"layersNotPainted":0,"layersIncomplete":0,"missingTiles":0,"pendingVisibleTiles":0}}
                        """;
                },
                new McpServerToolCreateOptions { Name = "await_settled" });
            yield return McpServerTool.Create(
                (double centerLat, double centerLon, double scaleDenominator) =>
                {
                    Calls.Enqueue("set_viewport");
                    Scales.Enqueue(scaleDenominator);
                    return """{"mode":"center"}""";
                },
                new McpServerToolCreateOptions { Name = "set_viewport" });
            yield return McpServerTool.Create(
                (bool? resetWindow = null) =>
                {
                    Calls.Enqueue("get_render_stats");
                    return """{"hasData":true,"window":{"count":40,"frameP50Ms":3,"frameP95Ms":7,"frameMaxMs":20}}""";
                },
                new McpServerToolCreateOptions { Name = "get_render_stats" });
        }
    }

    /// <summary>A fake viewer MCP server and a client connected to it over pipes.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private Task? _serverTask;

        public McpClient Client { get; private set; } = null!;

        public static async Task<Harness> StartAsync(FakeViewer viewer)
        {
            var toServer = new Pipe();
            var toClient = new Pipe();
            var options = new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "fake-viewer", Version = "1.0" },
                ToolCollection = [.. viewer.Tools()],
            };
            var harness = new Harness();
            var server = McpServer.Create(
                new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()),
                options);
            harness._serverTask = server.RunAsync(harness._stop.Token);
            harness.Client = await McpClient.CreateAsync(
                new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));
            return harness;
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            _stop.Cancel();
            try
            {
                if (_serverTask is not null)
                    await _serverTask;
            }
            catch (OperationCanceledException)
            {
            }

            _stop.Dispose();
        }
    }
}

/// <summary>
/// The jumps route against a real running viewer with the NOAA ENC
/// "Washington" Library source. Run by hand: start the viewer with
/// <c>--mcp --mcp-port-file &lt;file&gt; --cache-dir &lt;empty folder&gt;</c>
/// and set <c>ENC_DOTNET_SETTLE_PORT_FILE</c> to the port file. CI doesn't
/// have the data, so the test is skipped there.
/// </summary>
public sealed class ViewerSettleRunnerLiveTests
{
    private const string PortFileVariable = "ENC_DOTNET_SETTLE_PORT_FILE";

    [Fact]
    public async Task Jumps_AgainstARunningViewer_Settle()
    {
        var portFile = Environment.GetEnvironmentVariable(PortFileVariable);
        Assert.SkipUnless(
            !string.IsNullOrWhiteSpace(portFile) && File.Exists(portFile),
            $"Set {PortFileVariable} to a running viewer's --mcp-port-file to run this test.");

        var endpoint = new Uri(File.ReadAllText(portFile!).Trim());
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
        }), cancellationToken: TestContext.Current.CancellationToken);

        var manifest = await ViewerSettleRunner.RunAsync(
            client,
            new SettleRunOptions
            {
                Route = "jumps",
                Mode = SettleMode.Cold,
                Jumps = SettleRoute.DefaultJumps,
                Pan = SettleRoute.DefaultPan,
            },
            TestContext.Current.CancellationToken);

        Assert.All(manifest["steps"]!.AsArray(), step => Assert.True(step!["settled"]!.GetValue<bool>()));
    }
}
