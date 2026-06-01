using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class SetPaletteToolTests
{
    private sealed class FakeController : IRenderStateController
    {
        public PaletteType CurrentPalette { get; set; } = PaletteType.Day;
        public EcdisDisplayCategory CurrentDisplayCategory { get; set; } = EcdisDisplayCategory.Standard;
        public int PaletteCalls { get; private set; }
        public int CategoryCalls { get; private set; }

        public Task SetPaletteAsync(PaletteType palette, CancellationToken ct = default)
        {
            PaletteCalls++;
            CurrentPalette = palette;
            return Task.CompletedTask;
        }

        public Task SetDisplayCategoryAsync(EcdisDisplayCategory category, CancellationToken ct = default)
        {
            CategoryCalls++;
            CurrentDisplayCategory = category;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccessor : IRenderStateControllerAccessor
    {
        public IRenderStateController? Current { get; set; }
    }

    [Theory]
    [InlineData("Day", PaletteType.Day)]
    [InlineData("dusk", PaletteType.Dusk)]
    [InlineData("NIGHT", PaletteType.Night)]
    public async Task Sets_palette_case_insensitively(string input, PaletteType expected)
    {
        var ctrl = new FakeController { CurrentPalette = PaletteType.Day };
        var tool = new SetPaletteTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetPaletteRequest(input));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal(expected.ToString(), ok!.Palette);
        Assert.Equal("Day", ok.Previous);
        Assert.Equal(expected, ctrl.CurrentPalette);
        Assert.Equal(1, ctrl.PaletteCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sepia")]
    [InlineData("0")]
    public async Task Rejects_invalid_palette(string input)
    {
        var ctrl = new FakeController();
        var tool = new SetPaletteTool(new FakeAccessor { Current = ctrl });
        var result = await tool.InvokeAsync(new SetPaletteRequest(input));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
        Assert.Equal(0, ctrl.PaletteCalls);
    }

    [Fact]
    public async Task Map_not_ready_when_accessor_returns_null()
    {
        var tool = new SetPaletteTool(new FakeAccessor { Current = null });
        var result = await tool.InvokeAsync(new SetPaletteRequest("Day"));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<MapNotReady>(err);
    }

    [Fact]
    public void Adapter_translates_success()
    {
        var ok = ToolResult<SetPaletteResult>.Ok(new SetPaletteResult("Night", "Day"));
        var call = SetPaletteMcpAdapter.TranslateResult(ok);
        Assert.False(call.IsError);
        var single = Assert.Single(call.Content);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(single);
        Assert.Contains("\"palette\":\"Night\"", text.Text);
        Assert.Contains("\"previous\":\"Day\"", text.Text);
    }

    [Fact]
    public void Adapter_translates_error()
    {
        var err = ToolResult<SetPaletteResult>.Err(new InvalidArgument("palette", "bad"));
        var call = SetPaletteMcpAdapter.TranslateResult(err);
        Assert.True(call.IsError);
    }
}

public class SetDisplayCategoryToolTests
{
    private sealed class FakeController : IRenderStateController
    {
        public PaletteType CurrentPalette { get; set; } = PaletteType.Day;
        public EcdisDisplayCategory CurrentDisplayCategory { get; set; } = EcdisDisplayCategory.Standard;
        public int Calls { get; private set; }

        public Task SetPaletteAsync(PaletteType palette, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetDisplayCategoryAsync(EcdisDisplayCategory category, CancellationToken ct = default)
        {
            Calls++;
            CurrentDisplayCategory = category;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccessor : IRenderStateControllerAccessor
    {
        public IRenderStateController? Current { get; set; }
    }

    [Theory]
    [InlineData("DisplayBase", EcdisDisplayCategory.DisplayBase)]
    [InlineData("standard", EcdisDisplayCategory.Standard)]
    [InlineData("OTHERINFORMATION", EcdisDisplayCategory.OtherInformation)]
    [InlineData("All", EcdisDisplayCategory.All)]
    public async Task Sets_category_case_insensitively(string input, EcdisDisplayCategory expected)
    {
        var ctrl = new FakeController { CurrentDisplayCategory = EcdisDisplayCategory.Standard };
        var tool = new SetDisplayCategoryTool(new FakeAccessor { Current = ctrl });
        var result = await tool.InvokeAsync(new SetDisplayCategoryRequest(input));
        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal(expected.ToString(), ok!.DisplayCategory);
        Assert.Equal("Standard", ok.Previous);
        Assert.Equal(expected, ctrl.CurrentDisplayCategory);
        Assert.Equal(1, ctrl.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BadValue")]
    [InlineData("  ")]
    public async Task Rejects_invalid_category(string input)
    {
        var ctrl = new FakeController();
        var tool = new SetDisplayCategoryTool(new FakeAccessor { Current = ctrl });
        var result = await tool.InvokeAsync(new SetDisplayCategoryRequest(input));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
        Assert.Equal(0, ctrl.Calls);
    }

    [Fact]
    public async Task Map_not_ready_when_accessor_returns_null()
    {
        var tool = new SetDisplayCategoryTool(new FakeAccessor { Current = null });
        var result = await tool.InvokeAsync(new SetDisplayCategoryRequest("Standard"));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<MapNotReady>(err);
    }

    [Fact]
    public void Adapter_translates_success()
    {
        var ok = ToolResult<SetDisplayCategoryResult>.Ok(
            new SetDisplayCategoryResult("All", "Standard"));
        var call = SetDisplayCategoryMcpAdapter.TranslateResult(ok);
        Assert.False(call.IsError);
        var single = Assert.Single(call.Content);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(single);
        Assert.Contains("\"displayCategory\":\"All\"", text.Text);
    }
}
