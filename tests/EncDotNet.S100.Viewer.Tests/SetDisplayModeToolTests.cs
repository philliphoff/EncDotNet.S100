using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class SetDisplayModeToolTests
{
    private sealed class FakeController : IRenderStateController
    {
        private readonly Dictionary<string, string?> _modes = new();

        public PaletteType CurrentPalette { get; set; } = PaletteType.Day;
        public EcdisDisplayCategory CurrentDisplayCategory { get; set; } = EcdisDisplayCategory.Standard;
        public RenderSubsystemKind CurrentRenderSubsystem { get; set; } = RenderSubsystemKind.Mapsui;
        public bool RenderSubsystemPinned { get; set; }
        public int SetCalls { get; private set; }

        public Task SetPaletteAsync(PaletteType palette, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetDisplayCategoryAsync(EcdisDisplayCategory category, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRenderSubsystemAsync(RenderSubsystemKind subsystem, CancellationToken ct = default) => Task.CompletedTask;

        public string? GetDisplayMode(string spec) => _modes.GetValueOrDefault(spec);

        public Task SetDisplayModeAsync(string spec, string? modeId, CancellationToken ct = default)
        {
            SetCalls++;
            _modes[spec] = modeId;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccessor : IRenderStateControllerAccessor
    {
        public IRenderStateController? Current { get; set; }
    }

    [Theory]
    [InlineData("ice-concentration", S411DisplayModes.ConcentrationModeId)]
    [InlineData("sod", S411DisplayModes.StageOfDevelopmentModeId)]
    [InlineData("ICE-NAVIGATIONAL", S411DisplayModes.NavigationalModeId)]
    [InlineData(S411DisplayModes.StageOfDevelopmentModeId, S411DisplayModes.StageOfDevelopmentModeId)]
    [InlineData("icescientificicesoddisplaymode", S411DisplayModes.StageOfDevelopmentModeId)]
    public async Task Sets_mode_from_token_or_raw_id(string input, string expected)
    {
        var ctrl = new FakeController();
        var tool = new SetDisplayModeTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetDisplayModeRequest(input, null));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal("S-411", ok!.Spec);
        Assert.Equal(expected, ok.Mode);
        Assert.Null(ok.Previous);
        Assert.Equal(expected, ctrl.GetDisplayMode("S-411"));
        Assert.Equal(1, ctrl.SetCalls);
    }

    [Fact]
    public async Task Reports_provisional_for_navigational()
    {
        var ctrl = new FakeController();
        var tool = new SetDisplayModeTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetDisplayModeRequest("ice-navigational", null));

        Assert.True(result.TryGetValue(out var ok));
        Assert.True(ok!.Provisional);
    }

    [Fact]
    public async Task Not_provisional_for_concentration()
    {
        var ctrl = new FakeController();
        var tool = new SetDisplayModeTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetDisplayModeRequest("ice-concentration", null));

        Assert.True(result.TryGetValue(out var ok));
        Assert.False(ok!.Provisional);
    }

    [Fact]
    public async Task Returns_previous_mode()
    {
        var ctrl = new FakeController();
        var tool = new SetDisplayModeTool(new FakeAccessor { Current = ctrl });

        await tool.InvokeAsync(new SetDisplayModeRequest("ice-concentration", null));
        var result = await tool.InvokeAsync(new SetDisplayModeRequest("ice-sod", null));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal(S411DisplayModes.ConcentrationModeId, ok!.Previous);
        Assert.Equal(S411DisplayModes.StageOfDevelopmentModeId, ok.Mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("polaris")]
    public async Task Rejects_invalid_mode(string input)
    {
        var ctrl = new FakeController();
        var tool = new SetDisplayModeTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetDisplayModeRequest(input, null));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
        Assert.Equal(0, ctrl.SetCalls);
    }

    [Fact]
    public async Task Invalid_mode_message_lists_all_accepted_inputs()
    {
        var ctrl = new FakeController();
        var tool = new SetDisplayModeTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetDisplayModeRequest("polaris", null));

        Assert.True(result.TryGetError(out var err));
        var invalid = Assert.IsType<InvalidArgument>(err);
        // The message must advertise the bare aliases and the raw spec-native
        // mode ids, both of which are actually accepted by the tool.
        Assert.Contains("concentration", invalid.Reason);
        Assert.Contains("sod", invalid.Reason);
        Assert.Contains("navigational", invalid.Reason);
        Assert.Contains(S411DisplayModes.ConcentrationModeId, invalid.Reason);
        Assert.Contains(S411DisplayModes.StageOfDevelopmentModeId, invalid.Reason);
        Assert.Contains(S411DisplayModes.NavigationalModeId, invalid.Reason);
    }

    [Fact]
    public async Task Honours_explicit_spec()
    {
        var ctrl = new FakeController();
        var tool = new SetDisplayModeTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetDisplayModeRequest("ice-sod", "S-411"));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal("S-411", ok!.Spec);
        Assert.Equal(S411DisplayModes.StageOfDevelopmentModeId, ctrl.GetDisplayMode("S-411"));
    }

    [Fact]
    public async Task Errors_when_controller_not_ready()
    {
        var tool = new SetDisplayModeTool(new FakeAccessor { Current = null });

        var result = await tool.InvokeAsync(new SetDisplayModeRequest("ice-sod", null));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<MapNotReady>(err);
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-102")]
    public async Task Rejects_spec_that_declares_no_modes(string spec)
    {
        var ctrl = new FakeController();
        var tool = new SetDisplayModeTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetDisplayModeRequest("ice-sod", spec));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
        Assert.Equal(0, ctrl.SetCalls);
    }

    [Fact]
    public async Task Accepts_hyphenless_s411_spec()
    {
        var ctrl = new FakeController();
        var tool = new SetDisplayModeTool(new FakeAccessor { Current = ctrl });

        var result = await tool.InvokeAsync(new SetDisplayModeRequest("ice-sod", "S411"));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal("S-411", ok!.Spec);
        Assert.Equal(S411DisplayModes.StageOfDevelopmentModeId, ok!.Mode);
        Assert.Equal(S411DisplayModes.StageOfDevelopmentModeId, ctrl.GetDisplayMode("S-411"));
        Assert.Null(ctrl.GetDisplayMode("S411"));
    }
}
