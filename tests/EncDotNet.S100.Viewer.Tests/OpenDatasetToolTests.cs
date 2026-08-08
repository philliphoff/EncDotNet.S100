using System.Diagnostics;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;

// The shared Query namespace now also defines a DatasetLoadFailed error (added
// for the mutable MCP tool set, #560). This test exercises the Viewer's own
// OpenDatasetTool, which still raises the Viewer-internal one, so pin the name
// to that until issue #569 re-points the Viewer at the shared tool set and
// removes the Viewer-internal error.
using DatasetLoadFailed = EncDotNet.S100.Viewer.McpTools.DatasetLoadFailed;

namespace EncDotNet.S100.Viewer.Tests;

public class OpenDatasetToolTests
{
    private static string NewTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"encdotnet-open-{Guid.NewGuid():N}.000");
        File.WriteAllText(path, "x");
        return path;
    }

    private static string NewTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"encdotnet-open-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task File_load_happy_path_returns_id_spec_and_bounds()
    {
        var path = NewTempFile();
        try
        {
            var catalog = new FakeDatasetCatalog();
            var gateway = new FakeDatasetLoadGateway
            {
                Kind = DatasetPathKind.File,
                OnLoadFile = (p, _) => { catalog.Add(Path.GetFileName(p), "S-102"); return Task.FromResult(true); },
            };
            var tool = new OpenDatasetTool(catalog, gateway, quietMs: 50, maxWaitMs: 1000);

            var result = await tool.InvokeAsync(new OpenDatasetRequest(path));

            Assert.True(result.TryGetValue(out var ok));
            Assert.Equal("file", ok!.Kind);
            Assert.Equal(1, ok.Count);
            Assert.False(ok.TimedOut);
            Assert.True(ok.LoadDurationMs >= 0);
            var ds = Assert.Single(ok.Datasets);
            Assert.Equal(Path.GetFileName(path), ds.Id);
            Assert.Equal("S-102", ds.Spec);
            Assert.Equal(50.0, ds.SouthLatitude);
            Assert.Equal(-1.0, ds.EastLongitude);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Unrecognised_file_type_returns_invalid_argument()
    {
        var path = NewTempFile();
        try
        {
            var catalog = new FakeDatasetCatalog();
            var gateway = new FakeDatasetLoadGateway
            {
                Kind = DatasetPathKind.File,
                OnLoadFile = (_, _) => Task.FromResult(false),
            };
            var tool = new OpenDatasetTool(catalog, gateway, quietMs: 50, maxWaitMs: 1000);

            var result = await tool.InvokeAsync(new OpenDatasetRequest(path));

            Assert.True(result.TryGetError(out var err));
            Assert.IsType<InvalidArgument>(err);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Missing_path_returns_invalid_argument()
    {
        var tool = new OpenDatasetTool(new FakeDatasetCatalog(), new FakeDatasetLoadGateway());
        var result = await tool.InvokeAsync(new OpenDatasetRequest("/no/such/path-xyz.000"));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Empty_path_returns_invalid_argument()
    {
        var tool = new OpenDatasetTool(new FakeDatasetCatalog(), new FakeDatasetLoadGateway());
        var result = await tool.InvokeAsync(new OpenDatasetRequest("   "));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Not_ready_returns_map_not_ready()
    {
        var path = NewTempFile();
        try
        {
            var gateway = new FakeDatasetLoadGateway { IsReady = false };
            var tool = new OpenDatasetTool(new FakeDatasetCatalog(), gateway);

            var result = await tool.InvokeAsync(new OpenDatasetRequest(path));

            Assert.True(result.TryGetError(out var err));
            Assert.IsType<MapNotReady>(err);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Load_succeeds_but_adds_nothing_returns_dataset_load_failed()
    {
        var path = NewTempFile();
        try
        {
            var catalog = new FakeDatasetCatalog();
            var gateway = new FakeDatasetLoadGateway
            {
                Kind = DatasetPathKind.File,
                OnLoadFile = (_, _) => Task.FromResult(true), // reports success but adds nothing
            };
            var tool = new OpenDatasetTool(catalog, gateway, quietMs: 50, maxWaitMs: 1000);

            var result = await tool.InvokeAsync(new OpenDatasetRequest(path));

            Assert.True(result.TryGetError(out var err));
            Assert.IsType<DatasetLoadFailed>(err);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExchangeSet_collects_delayed_adds()
    {
        var dir = NewTempDir();
        try
        {
            var catalog = new FakeDatasetCatalog();
            var gateway = new FakeDatasetLoadGateway
            {
                Kind = DatasetPathKind.ExchangeSet,
                OnTriggerExchangeSet = path =>
                {
                    // Fire-and-forget adds after the trigger returns, like
                    // the real exchange-set service; 2 datasets dispatched.
                    // The two adds are issued as a single synchronous burst
                    // (no await between them) so the tool's quiescence quiet
                    // window can never open *between* them: the fast path
                    // (added >= dispatched) resolves deterministically. If the
                    // whole continuation is starved past the quiet window, the
                    // tool simply keeps waiting up to maxWaitMs and still sees
                    // both adds together. This removes the timing race where a
                    // delayed second add could be mistaken for a failed load
                    // (issue #215).
                    //
                    // NOTE: collapsing the adds means this test now exercises
                    // the fast path, not the quiet-window settle path. That
                    // path is still covered deterministically by
                    // ExchangeSet_partial_failure_settles_via_quiet_window
                    // below (1 add dispatched as 2 -> settles via quiet window).
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(20);
                        catalog.Add("a.000", "S-101");
                        catalog.Add("b.000", "S-102");
                    });
                    return Task.FromResult(2);
                },
            };
            var tool = new OpenDatasetTool(catalog, gateway, quietMs: 150, maxWaitMs: 5000);

            var result = await tool.InvokeAsync(new OpenDatasetRequest(dir));

            Assert.True(result.TryGetValue(out var ok));
            Assert.Equal("exchangeSet", ok!.Kind);
            Assert.Equal(2, ok.Count);
            Assert.False(ok.TimedOut);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ExchangeSet_partial_failure_settles_via_quiet_window()
    {
        var dir = NewTempDir();
        try
        {
            var catalog = new FakeDatasetCatalog();
            var gateway = new FakeDatasetLoadGateway
            {
                Kind = DatasetPathKind.ExchangeSet,
                OnTriggerExchangeSet = path =>
                {
                    // 2 datasets dispatched, but only 1 ever loads (the other
                    // failed). Must settle via the quiet window after the add.
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(20);
                        catalog.Add("only.000", "S-101");
                    });
                    return Task.FromResult(2);
                },
            };
            var tool = new OpenDatasetTool(catalog, gateway, quietMs: 120, maxWaitMs: 5000);

            var sw = Stopwatch.StartNew();
            var result = await tool.InvokeAsync(new OpenDatasetRequest(dir));
            sw.Stop();

            // Resolves shortly after the single add + quiet window, well below
            // the 5s ceiling — not a timeout.
            Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds}ms");
            Assert.True(result.TryGetValue(out var ok));
            Assert.Equal(1, ok!.Count);
            Assert.False(ok.TimedOut);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ExchangeSet_with_no_supported_datasets_fails_fast()
    {
        var dir = NewTempDir();
        try
        {
            var catalog = new FakeDatasetCatalog();
            var gateway = new FakeDatasetLoadGateway
            {
                Kind = DatasetPathKind.ExchangeSet,
                OnTriggerExchangeSet = _ => Task.FromResult(0), // nothing dispatched
            };
            var tool = new OpenDatasetTool(catalog, gateway, quietMs: 100, maxWaitMs: 5000);

            var sw = Stopwatch.StartNew();
            var result = await tool.InvokeAsync(new OpenDatasetRequest(dir));
            sw.Stop();

            // Zero dispatched datasets must fail fast, not wait out any window.
            Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds}ms");
            Assert.True(result.TryGetError(out var err));
            Assert.IsType<DatasetLoadFailed>(err);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ExchangeSet_event_before_trigger_returns_is_counted()
    {
        var dir = NewTempDir();
        try
        {
            var catalog = new FakeDatasetCatalog();
            var gateway = new FakeDatasetLoadGateway
            {
                Kind = DatasetPathKind.ExchangeSet,
                OnTriggerExchangeSet = _ =>
                {
                    // Synchronous add before the trigger returns.
                    catalog.Add("sync.000", "S-104");
                    return Task.FromResult(1);
                },
            };
            var tool = new OpenDatasetTool(catalog, gateway, quietMs: 100, maxWaitMs: 5000);

            var result = await tool.InvokeAsync(new OpenDatasetRequest(dir));

            Assert.True(result.TryGetValue(out var ok));
            Assert.Equal(1, ok!.Count);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Adapter_translates_success_payload()
    {
        var ok = ToolResult<OpenDatasetResult>.Ok(new OpenDatasetResult(
            "/tmp/x.000", "file", 1, 12.5, false,
            new[] { new OpenedDataset("x.000", "S-102", 1, 2, 3, 4) }));

        var call = OpenDatasetMcpAdapter.TranslateResult(ok);

        Assert.False(call.IsError);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(call.Content[0]).Text;
        Assert.Contains("\"kind\":\"file\"", text);
        Assert.Contains("\"southLatitude\":1", text);
    }

    [Fact]
    public void Adapter_translates_error_payload()
    {
        var err = ToolResult<OpenDatasetResult>.Err(new DatasetLoadFailed("nothing"));

        var call = OpenDatasetMcpAdapter.TranslateResult(err);

        Assert.True(call.IsError);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(call.Content[0]).Text;
        Assert.Contains("dataset_load_failed", text);
    }
}
