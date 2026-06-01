using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class SetTimeStepToolTests
{
    private sealed class FakeTimeAware : ITimeAwareDataset
    {
        public IReadOnlyList<DateTime> AvailableTimes { get; }
        public DateTime? CurrentTime => null;
        public FakeTimeAware(params DateTime[] samples) => AvailableTimes = samples;
        public DateTime? SnapTo(DateTime t) => null;
    }

    private static (SetTimeStepTool tool, GlobalTimeService time, List<DateTime> renders) Make(params DateTime[] samples)
    {
        var time = new GlobalTimeService();
        var renders = new List<DateTime>();
        time.CurrentTimeChanged += t => renders.Add(t);
        var entry = new DatasetEntry("/tmp/x.h5", "S-111");
        time.Register(entry, new FakeTimeAware(samples));
        var tool = new SetTimeStepTool(time, dispatcher: a => { a(); return Task.CompletedTask; });
        return (tool, time, renders);
    }

    [Fact]
    public async Task Index_form_sets_to_sample()
    {
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var samples = new[] { t0, t0.AddHours(1), t0.AddHours(2), t0.AddHours(3) };
        var (tool, time, renders) = Make(samples);

        var result = await tool.InvokeAsync(new SetTimeStepRequest(Index: 2));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal("index", ok!.Mode);
        Assert.Equal(2, ok.Index);
        Assert.Equal(4, ok.SampleCount);
        Assert.Equal(samples[2], time.CurrentTime);
        Assert.Single(renders);
        Assert.Equal(samples[2], renders[0]);
    }

    [Fact]
    public async Task Timestamp_form_snaps_to_nearest()
    {
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var samples = new[] { t0, t0.AddHours(1), t0.AddHours(2), t0.AddHours(3) };
        var (tool, time, _) = Make(samples);

        // 1:20 is closer to t0+1h than to t0+2h.
        var result = await tool.InvokeAsync(new SetTimeStepRequest(
            Timestamp: t0.AddMinutes(80).ToString("o")));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal("timestamp", ok!.Mode);
        Assert.Equal(1, ok.Index);
        Assert.Equal(samples[1], time.CurrentTime);
    }

    [Fact]
    public async Task Mixing_forms_is_rejected()
    {
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var (tool, _, _) = Make(t0, t0.AddHours(1));
        var result = await tool.InvokeAsync(new SetTimeStepRequest(Index: 0, Timestamp: t0.ToString("o")));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Empty_request_is_rejected()
    {
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var (tool, _, _) = Make(t0);
        var result = await tool.InvokeAsync(new SetTimeStepRequest());
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task Out_of_range_index_is_rejected(int index)
    {
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var (tool, _, _) = Make(t0, t0.AddHours(1));
        var result = await tool.InvokeAsync(new SetTimeStepRequest(Index: index));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Bad_timestamp_is_rejected()
    {
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var (tool, _, _) = Make(t0);
        var result = await tool.InvokeAsync(new SetTimeStepRequest(Timestamp: "not-a-timestamp"));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Map_not_ready_when_no_time_aware_dataset()
    {
        var time = new GlobalTimeService();
        var tool = new SetTimeStepTool(time, dispatcher: a => { a(); return Task.CompletedTask; });
        var result = await tool.InvokeAsync(new SetTimeStepRequest(Index: 0));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<MapNotReady>(err);
    }

    [Fact]
    public void Adapter_translates_success()
    {
        var ok = ToolResult<SetTimeStepResult>.Ok(
            new SetTimeStepResult("index", 3, "2024-01-01T03:00:00.0000000Z", 24, "2024-01-01T00:00:00.0000000Z"));
        var call = SetTimeStepMcpAdapter.TranslateResult(ok);
        Assert.False(call.IsError);
        var single = Assert.Single(call.Content);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(single);
        Assert.Contains("\"mode\":\"index\"", text.Text);
        Assert.Contains("\"index\":3", text.Text);
        Assert.Contains("\"sampleCount\":24", text.Text);
    }
}
