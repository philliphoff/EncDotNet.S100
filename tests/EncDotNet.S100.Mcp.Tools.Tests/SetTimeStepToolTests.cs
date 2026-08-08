using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Mutable;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Verifies <see cref="SetTimeStepTool"/> resolves an index or a snapped
/// timestamp against the shared <see cref="ITimeController"/> (#560).
/// </summary>
public class SetTimeStepToolTests
{
    private static readonly DateTime[] Steps =
    {
        new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        new(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc),
        new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task SetByIndex_AppliesThatStep()
    {
        var host = new FakeTime(Steps);
        var tool = new SetTimeStepTool(Accessor(host));

        var value = AssertOk(await tool.InvokeAsync(new SetTimeStepRequest(Index: 2)));

        Assert.Equal("index", value.Mode);
        Assert.Equal(2, value.Index);
        Assert.Equal(3, value.SampleCount);
        Assert.Null(value.Previous);
        Assert.Equal(Steps[2], host.Current);
        Assert.Equal(Steps[2].ToString("o", CultureInfo.InvariantCulture), value.Timestamp);
    }

    [Fact]
    public async Task SetByTimestamp_SnapsToNearestStepAndReportsPrevious()
    {
        var host = new FakeTime(Steps);
        await new SetTimeStepTool(Accessor(host)).InvokeAsync(new SetTimeStepRequest(Index: 0));

        // 08:00 is closer to the 06:00 step than to 12:00.
        var value = AssertOk(await new SetTimeStepTool(Accessor(host))
            .InvokeAsync(new SetTimeStepRequest(Timestamp: "2026-08-01T08:00:00Z")));

        Assert.Equal("timestamp", value.Mode);
        Assert.Equal(1, value.Index);
        Assert.Equal(Steps[1], host.Current);
        Assert.Equal(Steps[0].ToString("o", CultureInfo.InvariantCulture), value.Previous);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task SetByIndex_OutOfRange_IsInvalidArgument(int index)
    {
        var host = new FakeTime(Steps);

        var error = Assert.IsType<InvalidArgument>(
            AssertErr(await new SetTimeStepTool(Accessor(host))
                .InvokeAsync(new SetTimeStepRequest(Index: index))));
        Assert.Equal("index", error.Parameter);
    }

    [Fact]
    public async Task NeitherIndexNorTimestamp_IsInvalidArgument()
    {
        var host = new FakeTime(Steps);

        Assert.IsType<InvalidArgument>(
            AssertErr(await new SetTimeStepTool(Accessor(host))
                .InvokeAsync(new SetTimeStepRequest())));
    }

    [Fact]
    public async Task BothIndexAndTimestamp_IsInvalidArgument()
    {
        var host = new FakeTime(Steps);

        Assert.IsType<InvalidArgument>(
            AssertErr(await new SetTimeStepTool(Accessor(host))
                .InvokeAsync(new SetTimeStepRequest(Index: 0, Timestamp: "2026-08-01T00:00:00Z"))));
    }

    [Fact]
    public async Task UnparseableTimestamp_IsInvalidArgument()
    {
        var host = new FakeTime(Steps);

        var error = Assert.IsType<InvalidArgument>(
            AssertErr(await new SetTimeStepTool(Accessor(host))
                .InvokeAsync(new SetTimeStepRequest(Timestamp: "not-a-date"))));
        Assert.Equal("timestamp", error.Parameter);
    }

    [Fact]
    public async Task NoTimeAwareDataset_IsHostNotReady()
    {
        var host = new FakeTime(Array.Empty<DateTime>());

        Assert.IsType<HostNotReady>(
            AssertErr(await new SetTimeStepTool(Accessor(host))
                .InvokeAsync(new SetTimeStepRequest(Index: 0))));
    }

    [Fact]
    public async Task ControllerUnattached_IsHostNotReady()
    {
        var tool = new SetTimeStepTool(new NullCapabilityAccessor<ITimeController>());

        Assert.IsType<HostNotReady>(
            AssertErr(await tool.InvokeAsync(new SetTimeStepRequest(Index: 0))));
    }

    private static ICapabilityAccessor<ITimeController> Accessor(ITimeController c)
        => new StaticCapabilityAccessor<ITimeController>(c);

    private static TValue AssertOk<TValue>(ToolResult<TValue> result)
    {
        Assert.True(result.TryGetValue(out var value), "expected a success result");
        return value!;
    }

    private static ToolError AssertErr<TValue>(ToolResult<TValue> result)
    {
        Assert.True(result.TryGetError(out var error), "expected an error result");
        return error!;
    }

    private sealed class NullCapabilityAccessor<T> : ICapabilityAccessor<T> where T : class
    {
        public T? Current => null;
    }

    private sealed class FakeTime(IReadOnlyList<DateTime> steps) : ITimeController
    {
        public DateTime? Current { get; private set; }

        public IReadOnlyList<DateTime> AvailableSteps { get; } = steps;

        public Task SetTimeAsync(DateTime time, CancellationToken cancellationToken = default)
        {
            Current = time;
            return Task.CompletedTask;
        }
    }
}
