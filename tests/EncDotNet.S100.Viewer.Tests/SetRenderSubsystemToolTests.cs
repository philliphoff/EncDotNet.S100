using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class SetRenderSubsystemToolTests
{
    private sealed class FakeController : IRenderStateController
    {
        public PaletteType CurrentPalette { get; set; } = PaletteType.Day;
        public EcdisDisplayCategory CurrentDisplayCategory { get; set; } = EcdisDisplayCategory.Standard;
        public RenderSubsystemKind CurrentRenderSubsystem { get; set; } = RenderSubsystemKind.Mapsui;
        public bool RenderSubsystemPinned { get; set; }
        public int Calls { get; private set; }

        public Task SetPaletteAsync(PaletteType palette, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetDisplayCategoryAsync(EcdisDisplayCategory category, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetRenderSubsystemAsync(RenderSubsystemKind subsystem, CancellationToken ct = default)
        {
            Calls++;
            CurrentRenderSubsystem = subsystem;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccessor : IRenderStateControllerAccessor
    {
        public IRenderStateController? Current { get; set; }
    }

    [Theory]
    [InlineData("TiledScene", RenderSubsystemKind.TiledScene)]
    [InlineData("tiledscene", RenderSubsystemKind.TiledScene)]
    [InlineData("B", RenderSubsystemKind.TiledScene)]
    [InlineData("tiled", RenderSubsystemKind.TiledScene)]
    [InlineData("Mapsui", RenderSubsystemKind.Mapsui)]
    [InlineData("a", RenderSubsystemKind.Mapsui)]
    public async Task Sets_subsystem_case_insensitively_with_shorthand(string input, RenderSubsystemKind expected)
    {
        // Start from the opposite arm so the call is never a no-op.
        var start = expected == RenderSubsystemKind.Mapsui
            ? RenderSubsystemKind.TiledScene
            : RenderSubsystemKind.Mapsui;
        var ctrl = new FakeController { CurrentRenderSubsystem = start };
        var tool = new SetRenderSubsystemTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetRenderSubsystemRequest(input));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal(expected.ToString(), ok!.Subsystem);
        Assert.Equal(start.ToString(), ok.Previous);
        Assert.Equal(expected, ctrl.CurrentRenderSubsystem);
        Assert.Equal(1, ctrl.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Skia")]
    [InlineData("0")]
    [InlineData("1")]
    public async Task Rejects_invalid_subsystem(string input)
    {
        var ctrl = new FakeController();
        var tool = new SetRenderSubsystemTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetRenderSubsystemRequest(input));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
        Assert.Equal(0, ctrl.Calls);
    }

    [Fact]
    public async Task Map_not_ready_when_accessor_returns_null()
    {
        var tool = new SetRenderSubsystemTool(new FakeAccessor { Current = null });

        var result = await tool.InvokeAsync(new SetRenderSubsystemRequest("TiledScene"));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<MapNotReady>(err);
    }

    [Fact]
    public async Task Refuses_when_pinned_by_env()
    {
        var ctrl = new FakeController { RenderSubsystemPinned = true };
        var tool = new SetRenderSubsystemTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetRenderSubsystemRequest("TiledScene"));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<RenderSubsystemPinned>(err);
        Assert.Equal(0, ctrl.Calls);
    }

    [Fact]
    public async Task No_op_when_already_active()
    {
        var ctrl = new FakeController { CurrentRenderSubsystem = RenderSubsystemKind.TiledScene };
        var tool = new SetRenderSubsystemTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetRenderSubsystemRequest("TiledScene"));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal("TiledScene", ok!.Subsystem);
        Assert.Equal("TiledScene", ok.Previous);
    }

    [Fact]
    public void Adapter_translates_success()
    {
        var ok = ToolResult<SetRenderSubsystemResult>.Ok(
            new SetRenderSubsystemResult("TiledScene", "Mapsui"));

        var call = SetRenderSubsystemMcpAdapter.TranslateResult(ok);

        Assert.False(call.IsError);
        var single = Assert.Single(call.Content);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(single);
        Assert.Contains("\"subsystem\":\"TiledScene\"", text.Text);
        Assert.Contains("\"previous\":\"Mapsui\"", text.Text);
    }

    [Fact]
    public void Adapter_translates_error()
    {
        var err = ToolResult<SetRenderSubsystemResult>.Err(
            new RenderSubsystemPinned("pinned"));

        var call = SetRenderSubsystemMcpAdapter.TranslateResult(err);

        Assert.True(call.IsError);
    }
}
