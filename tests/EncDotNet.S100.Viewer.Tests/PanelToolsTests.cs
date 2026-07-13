using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Fake <see cref="IViewerUiController"/> that records
/// <see cref="SetPanelVisibilityAsync"/> calls and serves a fixed panel
/// snapshot, so the panel MCP tools can be exercised without an Avalonia
/// UI thread.
/// </summary>
internal sealed class FakeViewerUiController : IViewerUiController
{
    private readonly Dictionary<string, ViewerPanelState> _panels;

    public FakeViewerUiController(params ViewerPanelState[] panels)
    {
        _panels = panels.ToDictionary(p => p.Id, System.StringComparer.OrdinalIgnoreCase);
    }

    public int SetCalls { get; private set; }
    public string? LastPanelId { get; private set; }
    public bool? LastVisible { get; private set; }

    public Task<IReadOnlyList<ViewerPanelState>> GetPanelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ViewerPanelState>>(_panels.Values.ToList());

    public Task<PanelMutationOutcome> SetPanelVisibilityAsync(
        string panelId, bool visible, CancellationToken ct = default)
    {
        SetCalls++;
        LastPanelId = panelId;
        LastVisible = visible;

        if (!_panels.TryGetValue(panelId, out var state))
        {
            return Task.FromResult(new PanelMutationOutcome(false, false, null, false));
        }

        var previousShowing = state.Showing;

        if (!state.Available)
        {
            return Task.FromResult(new PanelMutationOutcome(true, false, state, previousShowing));
        }

        // Model the real controller: showing selects + opens (Showing true),
        // hiding closes the dock (Showing false).
        var next = state with { Selected = visible, DockOpen = visible, Showing = visible };
        _panels[state.Id] = next;
        return Task.FromResult(new PanelMutationOutcome(true, true, next, previousShowing));
    }
}

internal sealed class FakeUiControllerAccessor : IViewerUiControllerAccessor
{
    public IViewerUiController? Current { get; set; }
}

public class ListPanelsToolTests
{
    private static ViewerPanelState Panel(
        string id, string dock, bool available, bool selected, bool dockOpen) =>
        new(id, id + " Title", dock, available, selected, dockOpen, available && selected && dockOpen);

    [Fact]
    public async Task Lists_all_panels_with_state()
    {
        var controller = new FakeViewerUiController(
            Panel("Datasets", "Left", available: true, selected: true, dockOpen: true),
            Panel("Helm", "Left", available: false, selected: false, dockOpen: true),
            Panel("PickReport", "Right", available: true, selected: false, dockOpen: false));
        var tool = new ListPanelsTool(new FakeUiControllerAccessor { Current = controller });

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal(3, ok!.Panels.Count);
        var datasets = ok.Panels.Single(p => p.Id == "Datasets");
        Assert.True(datasets.Showing);
        Assert.True(datasets.Available);
        var helm = ok.Panels.Single(p => p.Id == "Helm");
        Assert.False(helm.Available);
        Assert.False(helm.Showing);
        var pick = ok.Panels.Single(p => p.Id == "PickReport");
        Assert.False(pick.Showing);
    }

    [Fact]
    public async Task Ui_not_ready_when_accessor_returns_null()
    {
        var tool = new ListPanelsTool(new FakeUiControllerAccessor { Current = null });
        var result = await tool.InvokeAsync();
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<UiNotReady>(err);
    }

    [Fact]
    public void Adapter_translates_success()
    {
        var ok = ToolResult<ListPanelsResult>.Ok(new ListPanelsResult(new[]
        {
            new ListPanelsPanel("Datasets", "Datasets", "Left", true, true, true, true),
        }));
        var call = ListPanelsMcpAdapter.TranslateResult(ok);
        Assert.False(call.IsError);
        var single = Assert.Single(call.Content);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(single);
        Assert.Contains("\"id\":\"Datasets\"", text.Text);
        Assert.Contains("\"showing\":true", text.Text);
    }
}

public class SetPanelToolTests
{
    private static ViewerPanelState Panel(
        string id, string dock, bool available, bool selected, bool dockOpen) =>
        new(id, id + " Title", dock, available, selected, dockOpen, available && selected && dockOpen);

    [Fact]
    public async Task Shows_hidden_panel_and_reports_change()
    {
        var controller = new FakeViewerUiController(
            Panel("PickReport", "Right", available: true, selected: false, dockOpen: false));
        var tool = new SetPanelTool(new FakeUiControllerAccessor { Current = controller });

        var result = await tool.InvokeAsync(new SetPanelRequest("PickReport", null));

        Assert.True(result.TryGetValue(out var ok));
        Assert.True(ok!.Showing);
        Assert.False(ok.PreviousShowing);
        Assert.True(ok.Changed);
        Assert.Equal(true, controller.LastVisible);
    }

    [Fact]
    public async Task Hides_showing_panel()
    {
        var controller = new FakeViewerUiController(
            Panel("Timeline", "Bottom", available: true, selected: true, dockOpen: true));
        var tool = new SetPanelTool(new FakeUiControllerAccessor { Current = controller });

        var result = await tool.InvokeAsync(new SetPanelRequest("Timeline", false));

        Assert.True(result.TryGetValue(out var ok));
        Assert.False(ok!.Showing);
        Assert.True(ok.PreviousShowing);
        Assert.True(ok.Changed);
    }

    [Fact]
    public async Task Showing_already_shown_panel_is_noop()
    {
        var controller = new FakeViewerUiController(
            Panel("Datasets", "Left", available: true, selected: true, dockOpen: true));
        var tool = new SetPanelTool(new FakeUiControllerAccessor { Current = controller });

        var result = await tool.InvokeAsync(new SetPanelRequest("Datasets", true));

        Assert.True(result.TryGetValue(out var ok));
        Assert.True(ok!.Showing);
        Assert.True(ok.PreviousShowing);
        Assert.False(ok.Changed);
    }

    [Fact]
    public async Task Resolves_panel_id_case_insensitively()
    {
        var controller = new FakeViewerUiController(
            Panel("LayerStack", "Left", available: true, selected: false, dockOpen: false));
        var tool = new SetPanelTool(new FakeUiControllerAccessor { Current = controller });

        var result = await tool.InvokeAsync(new SetPanelRequest("layerstack", true));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal("LayerStack", ok!.Panel);
        Assert.True(ok.Showing);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_blank_panel(string input)
    {
        var controller = new FakeViewerUiController();
        var tool = new SetPanelTool(new FakeUiControllerAccessor { Current = controller });
        var result = await tool.InvokeAsync(new SetPanelRequest(input, true));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
        Assert.Equal(0, controller.SetCalls);
    }

    [Fact]
    public async Task Unknown_panel_returns_panel_not_found()
    {
        var controller = new FakeViewerUiController(
            Panel("Datasets", "Left", available: true, selected: true, dockOpen: true));
        var tool = new SetPanelTool(new FakeUiControllerAccessor { Current = controller });

        var result = await tool.InvokeAsync(new SetPanelRequest("Nope", true));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<PanelNotFound>(err);
    }

    [Fact]
    public async Task Showing_unavailable_panel_returns_panel_unavailable()
    {
        var controller = new FakeViewerUiController(
            Panel("Helm", "Left", available: false, selected: false, dockOpen: false));
        var tool = new SetPanelTool(new FakeUiControllerAccessor { Current = controller });

        var result = await tool.InvokeAsync(new SetPanelRequest("Helm", true));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<PanelUnavailable>(err);
    }

    [Fact]
    public async Task Unavailable_panel_error_echoes_canonical_id_not_caller_casing()
    {
        var controller = new FakeViewerUiController(
            Panel("Helm", "Left", available: false, selected: false, dockOpen: false));
        var tool = new SetPanelTool(new FakeUiControllerAccessor { Current = controller });

        var result = await tool.InvokeAsync(new SetPanelRequest("helm", true));

        Assert.True(result.TryGetError(out var err));
        var unavailable = Assert.IsType<PanelUnavailable>(err);
        Assert.Equal("Helm", unavailable.PanelId);
    }

    [Fact]
    public async Task Hiding_unavailable_panel_is_allowed_noop()
    {
        var controller = new FakeViewerUiController(
            Panel("Helm", "Left", available: false, selected: false, dockOpen: false));
        var tool = new SetPanelTool(new FakeUiControllerAccessor { Current = controller });

        var result = await tool.InvokeAsync(new SetPanelRequest("Helm", false));

        Assert.True(result.TryGetValue(out var ok));
        Assert.False(ok!.Showing);
        Assert.False(ok.Changed);
    }

    [Fact]
    public async Task Ui_not_ready_when_accessor_returns_null()
    {
        var tool = new SetPanelTool(new FakeUiControllerAccessor { Current = null });
        var result = await tool.InvokeAsync(new SetPanelRequest("Datasets", true));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<UiNotReady>(err);
    }

    [Fact]
    public void Adapter_translates_success()
    {
        var ok = ToolResult<SetPanelResult>.Ok(new SetPanelResult(
            "Timeline", "Timeline", "Bottom", true, true, true, true, false, true));
        var call = SetPanelMcpAdapter.TranslateResult(ok);
        Assert.False(call.IsError);
        var single = Assert.Single(call.Content);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(single);
        Assert.Contains("\"panel\":\"Timeline\"", text.Text);
        Assert.Contains("\"changed\":true", text.Text);
    }

    [Fact]
    public void Adapter_translates_error()
    {
        var err = ToolResult<SetPanelResult>.Err(new PanelNotFound("Nope"));
        var call = SetPanelMcpAdapter.TranslateResult(err);
        Assert.True(call.IsError);
        var single = Assert.Single(call.Content);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(single);
        Assert.Contains("panel_not_found", text.Text);
    }
}
