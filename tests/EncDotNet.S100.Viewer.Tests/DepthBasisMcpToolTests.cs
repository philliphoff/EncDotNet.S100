using System.Text.Json;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <c>get_depth_basis</c> and the <c>depthBasis</c> field of
/// <c>get_check_state</c> (#1191, <c>docs/design/depth-basis-indicator.md</c> §2.6).
/// </summary>
public sealed class DepthBasisMcpToolTests
{
    private static readonly DateTimeOffset ViewTime = new(2026, 10, 31, 14, 20, 0, TimeSpan.Zero);
    private static readonly GeoBounds InView = new(47.55, -122.45, 47.65, -122.35);

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class FixedViewTime : IViewTime
    {
        public DateTimeOffset Current => ViewTime;

        public event EventHandler? Changed { add { } remove { } }
    }

    private static T Value<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    private static DepthBasis NotApplied() => DepthBasisEvaluator.Evaluate(new DepthBasisInputs(
        [
            new DepthBasisDataset("US5WA22M", "S-101", true, InView, 12),
            new DepthBasisDataset("104US00_CBOFS_20261031T12Z", "S-104", true, InView, 12,
                new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc),
                WaterLevelSourceKind.Grid),
        ],
        InView,
        ViewTime,
        NoWaterLevelAdjustment.Instance));

    [Fact]
    public void The_dto_reports_the_evaluator_state_in_snake_case_codes()
    {
        var dto = GetDepthBasisTool.ToDto(NotApplied());

        Assert.Equal("charted", dto.Kind);
        Assert.Equal("not_applied", dto.Reason);
        Assert.Null(dto.WaterLevelStatus);
        Assert.Equal(ViewTime, dto.ViewTime);
        Assert.True(dto.CoversWholeView);
        Assert.Equal("same", dto.Datum);
        var depth = Assert.Single(dto.DepthSources);
        Assert.Equal(("US5WA22M", "S-101", 12, "Mean Lower Low Water"), (depth.Id, depth.Spec, depth.VerticalDatum, depth.VerticalDatumLabel));
        var water = Assert.Single(dto.WaterLevelSources);
        Assert.Equal(("104US00_CBOFS_20261031T12Z", "grid"), (water.Id, water.Kind));
    }

    [Fact]
    public void Display_matches_the_pill_text()
    {
        var basis = NotApplied();
        var pill = new DepthBasisIndicatorViewModel();
        pill.Update(basis, waterLevelLoaded: true, TimeFormat.Utc);

        Assert.Equal(pill.DisplayedText, GetDepthBasisTool.ToDto(basis).Display);
        Assert.Equal("Charted · water level not applied", GetDepthBasisTool.ToDto(basis).Display);
    }

    [Theory]
    [InlineData(WaterLevelStatus.Applied, "applied")]
    [InlineData(WaterLevelStatus.NoCoverage, "no_coverage")]
    [InlineData(WaterLevelStatus.BeforeStart, "before_start")]
    [InlineData(WaterLevelStatus.AfterEnd, "after_end")]
    [InlineData(WaterLevelStatus.NoData, "no_data")]
    [InlineData(WaterLevelStatus.DatumMismatch, "datum_mismatch")]
    public void Water_level_statuses_use_check_route_codes(WaterLevelStatus status, string code) =>
        Assert.Equal(code, GetDepthBasisTool.Status(status));

    [Fact]
    public async Task The_tool_recomputes_and_returns_the_current_basis()
    {
        var loader = new FakeDatasetLoaderService();
        using var service = new DepthBasisService(loader, null, new FixedViewTime(), NoWaterLevelAdjustment.Instance, post: _ => { });
        var tool = new GetDepthBasisTool(service, new InlineInvoker());

        var dto = Value(await tool.InvokeAsync(TestContext.Current.CancellationToken));

        Assert.Equal("none", dto.Kind);
        Assert.Equal(string.Empty, dto.Display);
        Assert.Empty(dto.DepthSources);
    }

    [Fact]
    public void The_wire_shape_uses_the_documented_names()
    {
        var json = JsonSerializer.Serialize(GetDepthBasisTool.ToDto(NotApplied()), McpAdapterShared.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("charted", root.GetProperty("kind").GetString());
        Assert.Equal("not_applied", root.GetProperty("reason").GetString());
        Assert.Equal("Charted · water level not applied", root.GetProperty("display").GetString());
        Assert.Equal(12, root.GetProperty("depthSources")[0].GetProperty("verticalDatum").GetInt32());
        Assert.Equal("grid", root.GetProperty("waterLevelSources")[0].GetProperty("kind").GetString());
        Assert.True(root.GetProperty("coversWholeView").GetBoolean());
    }

    [Fact]
    public async Task Get_check_state_reports_the_depth_basis()
    {
        var f = new PassageCheckFixture();
        var loader = new FakeDatasetLoaderService();
        using var service = new DepthBasisService(loader, null, new FixedViewTime(), NoWaterLevelAdjustment.Instance, post: _ => { });

        var state = Value(await new GetCheckStateTool(f.Routes, new InlineInvoker(), f.Check) { DepthBasis = service }
            .InvokeAsync(false, TestContext.Current.CancellationToken));
        var without = Value(await new GetCheckStateTool(f.Routes, new InlineInvoker(), f.Check)
            .InvokeAsync(false, TestContext.Current.CancellationToken));

        Assert.NotNull(state.DepthBasis);
        Assert.Equal("none", state.DepthBasis!.Kind);
        Assert.Null(without.DepthBasis);
    }
}
