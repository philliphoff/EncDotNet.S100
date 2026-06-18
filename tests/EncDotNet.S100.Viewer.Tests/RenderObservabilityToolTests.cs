using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Protocol;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests the two render-observability MCP tools and their adapters end
/// to end against a fake <see cref="IRenderActivityMonitor"/>.
/// </summary>
public class RenderObservabilityToolTests
{
    private sealed class FakeMonitor : IRenderActivityMonitor
    {
        public long PaintCount { get; set; }
        public RenderStatsSnapshot? LatestStats { get; set; }
        public Func<bool>? BusyProbe { get; set; }
        public RenderIdleResult NextResult { get; set; }
        public TimeSpan SeenQuiet { get; private set; }
        public TimeSpan SeenTimeout { get; private set; }

        public Task<RenderIdleResult> WaitForIdleAsync(
            TimeSpan quietPeriod, TimeSpan timeout, System.Threading.CancellationToken ct = default)
        {
            SeenQuiet = quietPeriod;
            SeenTimeout = timeout;
            return Task.FromResult(NextResult);
        }
    }

    private static JsonObject Parse(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        return (JsonObject)JsonNode.Parse(text)!;
    }

    // ---- await_render_idle ---------------------------------------------

    [Fact]
    public async Task AwaitRenderIdle_applies_defaults_and_echoes_outcome()
    {
        var monitor = new FakeMonitor
        {
            NextResult = new RenderIdleResult(true, false, 42.0, 3, 250.0),
        };
        var tool = new AwaitRenderIdleTool(monitor);

        var result = await tool.InvokeAsync(new AwaitRenderIdleRequest());

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(AwaitRenderIdleTool.DefaultQuietPeriodMs, value!.QuietPeriodMs);
        Assert.Equal(AwaitRenderIdleTool.DefaultTimeoutMs, value.TimeoutMs);
        Assert.True(value.WentIdle);
        Assert.Equal(3, value.PaintsObserved);
        Assert.Equal(TimeSpan.FromMilliseconds(AwaitRenderIdleTool.DefaultQuietPeriodMs), monitor.SeenQuiet);
    }

    [Fact]
    public async Task AwaitRenderIdle_clamps_out_of_range_arguments()
    {
        var monitor = new FakeMonitor { NextResult = new RenderIdleResult(false, true, 1, 0, 1) };
        var tool = new AwaitRenderIdleTool(monitor);

        var result = await tool.InvokeAsync(new AwaitRenderIdleRequest(QuietPeriodMs: 999_999, TimeoutMs: 1));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(AwaitRenderIdleTool.MaxQuietPeriodMs, value!.QuietPeriodMs);
        Assert.Equal(AwaitRenderIdleTool.MinTimeoutMs, value.TimeoutMs);
    }

    [Fact]
    public void AwaitRenderIdle_adapter_serialises_success()
    {
        var ok = ToolResult<AwaitRenderIdleResult>.Ok(
            new AwaitRenderIdleResult(true, false, 12.0, 2, 250.0, 250, 5000));
        var call = AwaitRenderIdleMcpAdapter.TranslateResult(ok);

        Assert.False(call.IsError);
        var json = Parse(call);
        Assert.True((bool)json["wentIdle"]!);
        Assert.Equal(2, (long)json["paintsObserved"]!);
        Assert.Equal(250, (int)json["quietPeriodMs"]!);
    }

    // ---- get_render_stats ----------------------------------------------

    [Fact]
    public async Task GetRenderStats_reports_no_data_when_no_paint()
    {
        var tool = new GetRenderStatsTool(new FakeMonitor { LatestStats = null });
        var result = await tool.InvokeAsync(new GetRenderStatsRequest());

        Assert.True(result.TryGetValue(out var value));
        Assert.False(value!.HasData);
        Assert.Null(value.FrameDurationMs);
        Assert.Empty(value.Styles);
    }

    [Fact]
    public async Task GetRenderStats_projects_latest_snapshot()
    {
        var snapshot = new RenderStatsSnapshot(
            FrameDurationMs: 14.0,
            IntervalMs: 33.0,
            TotalDrawCalls: 7,
            Styles: new List<RenderStyleStat>
            {
                new("VectorStyle", 5, 9.0),
                new("LabelStyle", 2, 3.0),
            },
            PaintSequence: 42,
            CapturedAtUtc: DateTimeOffset.UnixEpoch);
        var tool = new GetRenderStatsTool(new FakeMonitor { LatestStats = snapshot });

        var result = await tool.InvokeAsync(new GetRenderStatsRequest());

        Assert.True(result.TryGetValue(out var value));
        Assert.True(value!.HasData);
        Assert.Equal(14.0, value.FrameDurationMs);
        Assert.Equal(7, value.TotalDrawCalls);
        Assert.Equal(2, value.Styles.Count);
        Assert.Equal("VectorStyle", value.Styles[0].Style);
    }

    [Fact]
    public void GetRenderStats_adapter_serialises_styles_array()
    {
        var ok = ToolResult<GetRenderStatsResult>.Ok(new GetRenderStatsResult(
            HasData: true,
            FrameDurationMs: 14.0,
            IntervalMs: 33.0,
            TotalDrawCalls: 7,
            PaintSequence: 42,
            CapturedAtUtc: DateTimeOffset.UnixEpoch.ToString("O"),
            Styles: new[] { new RenderStyleStatDto("VectorStyle", 5, 9.0) },
            Window: new RenderWindowStatsDto(10, 33, 42, 51.0, 20.0, 48.0, 49.0, 18.0, 47.0, 7)));
        var call = GetRenderStatsMcpAdapter.TranslateResult(ok);

        Assert.False(call.IsError);
        var json = Parse(call);
        Assert.True((bool)json["hasData"]!);
        var styles = (JsonArray)json["styles"]!;
        Assert.Single(styles);
        Assert.Equal("VectorStyle", (string)styles[0]!["style"]!);
        var window = (JsonObject)json["window"]!;
        Assert.Equal(10, (long)window["count"]!);
        Assert.Equal(51.0, (double)window["frameMaxMs"]!);
        Assert.Equal(49.0, (double)window["vectorMaxMs"]!);
    }

    [Fact]
    public void GetRenderStats_adapter_serialises_no_data()
    {
        var ok = ToolResult<GetRenderStatsResult>.Ok(new GetRenderStatsResult(
            HasData: false, null, null, null, null, null, Array.Empty<RenderStyleStatDto>(),
            Window: new RenderWindowStatsDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0)));
        var call = GetRenderStatsMcpAdapter.TranslateResult(ok);

        Assert.False(call.IsError);
        var json = Parse(call);
        Assert.False((bool)json["hasData"]!);
        Assert.Empty((JsonArray)json["styles"]!);
    }
}
