using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>MCP <c>get_legend</c> (#1403, legends design note D12): headless, it reads the legend service.</summary>
public sealed class LegendMcpToolTests
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

    private static GetLegendTool Tool(LegendServiceTests.Fixture f, LegendService service) =>
        new(service, f.Position(service), new InlineInvoker());

    private static LegendServiceTests.Fixture TwoProducts()
    {
        var f = new LegendServiceTests.Fixture();
        f.Add("111US00_a.h5", "S-111", 40, -75, 41, -74);
        f.Add("codsus.txt", "CODSUS", 10, 10, 11, 11);
        f.Loader.SetEntries(
            LegendServiceTests.VectorEntry("codsus.txt", "CODSUS", "weather.fronts", "Surface analysis: codsus.txt", S98DisplayPlane.OtherChartOverlays,
            [
                new SymbolLegendEntry("<svg/>", "Pressure centre, hPa"),
                new LineLegendEntry(new LegendLineSample { Color = "#0000FF", Width = 1 }, "Cold front"),
            ]),
            LegendServiceTests.VectorEntry("111US00_a.h5", "S-111", "s111.arrows", "Surface currents: 111US00_a.h5", S98DisplayPlane.DynamicArrows,
            [
                new SwatchLegendEntry("#D0E8FF", "0–1 kn"),
                new SwatchLegendEntry("#D0E8FF", "Eddy") { Outline = new LegendLineSample { Color = "#000000", Width = 1 } },
            ]));
        return f;
    }

    [Fact]
    public async Task It_returns_the_sections_in_view_with_their_rows()
    {
        var f = TwoProducts();
        using var service = f.Create(PaletteType.Dusk);
        var tool = Tool(f, service);

        // get_legend takes the current view, without waiting for pan end.
        f.Viewport.Publish(39, -76, 42, -73);
        var dto = Value(await tool.InvokeAsync(ct: TestContext.Current.CancellationToken));

        Assert.False(dto.TabShown);
        Assert.Equal("Dusk", dto.Palette);
        Assert.Equal(1, dto.NotInViewCount);
        var section = Assert.Single(dto.Sections);
        Assert.Equal("S-111", section.Id);
        Assert.Equal("Surface currents", section.Title);
        Assert.Equal("111US00_a.h5", section.Source);
        Assert.Equal(["111US00_a.h5"], section.Datasets);
        Assert.Equal("none", section.TimeState);
        Assert.True(section.InView);
        Assert.True(section.Expanded);
        var group = Assert.Single(section.Groups);
        Assert.Equal("s111.arrows", group.Key);
        Assert.Equal(["swatch", "area"], group.Rows.Select(r => r.Kind));
        Assert.Equal(["0–1 kn", "Eddy"], group.Rows.Select(r => r.Label));
        Assert.Equal("#D0E8FF", group.Rows[0].Color);
    }

    [Fact]
    public async Task All_lists_every_loaded_product_in_draw_order()
    {
        var f = TwoProducts();
        using var service = f.Create();
        service.IsTabShown = true;
        var tool = Tool(f, service);
        f.Viewport.Publish(39, -76, 42, -73);

        var dto = Value(await tool.InvokeAsync(all: true, ct: TestContext.Current.CancellationToken));

        Assert.True(dto.TabShown);
        // The stack is bottom first: S-111 on Dynamic arrows draws over the fronts.
        Assert.Equal(["S-111", "CODSUS"], dto.Sections.Select(s => s.Id));
        Assert.Equal([true, false], dto.Sections.Select(s => s.InView));
        var fronts = dto.Sections[1];
        Assert.Equal("Surface analysis", fronts.Title);
        Assert.Equal(["symbol", "line"], fronts.Groups.Single().Rows.Select(r => r.Kind));
        Assert.All(fronts.Groups.Single().Rows, r => Assert.Null(r.Color));
    }

    [Fact]
    public async Task No_data_at_the_view_time_is_reported()
    {
        var f = TwoProducts();
        f.Datasets.Entries.Single(e => e.Id.Value == "111US00_a.h5")
            .SetLayerTime(new LayerTime("no data · last 18:00Z, 6 h earlier", LayerTimeState.NoData, null), null);
        using var service = f.Create();
        var tool = Tool(f, service);

        var dto = Value(await tool.InvokeAsync(all: true, ct: TestContext.Current.CancellationToken));

        var section = dto.Sections.Single(s => s.Id == "S-111");
        Assert.Equal("no_data", section.TimeState);
        Assert.Equal("no data · last 18:00Z, 6 h earlier", section.Time);
    }

    [Fact]
    public async Task The_result_serializes_with_camel_case_names()
    {
        var f = TwoProducts();
        using var service = f.Create();
        var dto = Value(await Tool(f, service).InvokeAsync(all: true, ct: TestContext.Current.CancellationToken));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, McpAdapterShared.Options));

        var root = json.RootElement;
        Assert.False(root.GetProperty("tabShown").GetBoolean());
        Assert.Equal(2, root.GetProperty("sections").GetArrayLength());
        var row = root.GetProperty("sections")[0].GetProperty("groups")[0].GetProperty("rows")[0];
        Assert.Equal("swatch", row.GetProperty("kind").GetString());
        Assert.Equal("0–1 kn", row.GetProperty("label").GetString());
    }

    [Fact]
    public void The_adapter_is_named_get_legend()
    {
        var f = new LegendServiceTests.Fixture();
        using var service = f.Create();

        var tool = LegendMcpAdapter.Create(Tool(f, service));

        Assert.Equal("get_legend", tool.ProtocolTool.Name);
    }
}
