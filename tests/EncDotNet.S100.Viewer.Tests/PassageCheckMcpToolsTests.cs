using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Check passage MCP tools (#901): <c>set_check_mode</c>,
/// <c>get_check_state</c> and <c>set_passage_cursor</c>.
/// </summary>
public sealed class PassageCheckMcpToolsTests
{
    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static T Value<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    [Fact]
    public async Task Set_check_mode_turns_check_on_waits_and_reports_the_verdict()
    {
        var f = new PassageCheckFixture();
        var tool = new SetCheckModeTool(f.Routes, new InlineInvoker(), f.Check);

        var state = Value(await tool.InvokeAsync(new SetCheckModeRequest(true), TestContext.Current.CancellationToken));

        Assert.True(f.Routes.IsCheckModeActive);
        Assert.True(state.On);
        Assert.False(state.Computing);
        Assert.Equal(f.Route.Id, state.RouteId);
        Assert.Equal("below_setting", state.Verdict);
        Assert.Equal("Below your UKC setting in 1 place", state.VerdictTitle);
        Assert.Equal(-1.5, state.MinUkcMeters);
        Assert.Equal(5, state.MinUkcLeg);

        var off = Value(await tool.InvokeAsync(new SetCheckModeRequest(false), TestContext.Current.CancellationToken));
        Assert.False(off.On);
        Assert.Null(off.Verdict);
        Assert.False(f.Routes.IsCheckModeActive);
    }

    [Fact]
    public async Task Set_check_mode_needs_a_draft()
    {
        var f = new PassageCheckFixture();
        f.Route.SetPassageValue(RoutePassageField.Draft, null);
        var tool = new SetCheckModeTool(f.Routes, new InlineInvoker(), f.Check);

        var result = await tool.InvokeAsync(new SetCheckModeRequest(true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
        Assert.False(f.Routes.IsCheckModeActive);
    }

    [Fact]
    public async Task Set_check_mode_on_an_unknown_route_is_route_not_found()
    {
        var f = new PassageCheckFixture();
        var tool = new SetCheckModeTool(f.Routes, new InlineInvoker(), f.Check);

        var result = await tool.InvokeAsync(new SetCheckModeRequest(true, "missing"), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<RouteNotFound>(error);
    }

    [Fact]
    public async Task The_cursor_is_settable_and_read_back()
    {
        var f = new PassageCheckFixture();
        await new SetCheckModeTool(f.Routes, new InlineInvoker(), f.Check)
            .InvokeAsync(new SetCheckModeRequest(true), TestContext.Current.CancellationToken);
        var cursor = new SetPassageCursorTool(f.Routes, new InlineInvoker(), f.Check);

        var state = Value(await cursor.InvokeAsync(new SetPassageCursorRequest(3.0), TestContext.Current.CancellationToken));

        Assert.Equal(3.0, state.CursorNm!.Value, 3);
        Assert.Equal(2, state.CursorLeg);
        Assert.NotNull(state.CursorEta);
        Assert.NotNull(f.Check.Cursor);

        var read = Value(await new GetCheckStateTool(f.Routes, new InlineInvoker(), f.Check).InvokeAsync(wait: true, TestContext.Current.CancellationToken));
        Assert.Equal(state.CursorLongitude, read.CursorLongitude);

        var hidden = Value(await cursor.InvokeAsync(new SetPassageCursorRequest(null), TestContext.Current.CancellationToken));
        Assert.Null(hidden.CursorNm);
    }

    [Fact]
    public async Task The_cursor_needs_a_result()
    {
        var f = new PassageCheckFixture();
        var cursor = new SetPassageCursorTool(f.Routes, new InlineInvoker(), f.Check);

        var result = await cursor.InvokeAsync(new SetPassageCursorRequest(1), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }
}
