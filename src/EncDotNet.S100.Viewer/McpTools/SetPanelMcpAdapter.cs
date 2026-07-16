using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Wraps <see cref="SetPanelTool"/> as an MCP server tool.</summary>
internal static class SetPanelMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Shows or hides one of the live viewer's activity panels (a tab in the left / right / bottom dock). "
        + "'panel' is a panel id from list_panels (case-insensitive), e.g. 'Datasets', 'LayerStack', "
        + "'PickReport', 'Timeline'. 'visible' defaults to true: showing selects the panel's tab and opens "
        + "its dock; hiding (false) closes the panel's dock when that panel is the one currently shown there. "
        + "Idempotent — a panel already in the requested state is left untouched. Returns the resulting "
        + "'showing' state, the 'previousShowing' state, and whether it 'changed'. Rejects an unknown id "
        + "(panel_not_found) and an attempt to show a panel that is not currently available (panel_unavailable, "
        + "e.g. Vessels while the AIS overlay is disabled). Lets scripted runs drive non-render UX from outside "
        + "the GUI. See docs/mcp-server.md for the read-only / mutating split.";

    public static McpServerTool Create(SetPanelTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Panel id from list_panels (case-insensitive), e.g. 'Datasets', 'LayerStack', 'PickReport', 'Timeline'.")] string panel,
            [Description("True (default) shows the panel (selects it and opens its dock); false hides it (closes its dock when it is the panel currently shown).")] bool? visible = null,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(new SetPanelRequest(panel, visible), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetPanelTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(Func<Task<ToolResult<SetPanelResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return InternalError(ex); }
    }

    internal static CallToolResult TranslateResult(ToolResult<SetPanelResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var payload = new JsonObject
            {
                ["panel"] = value!.Panel,
                ["title"] = value.Title,
                ["dock"] = value.Dock,
                ["available"] = value.Available,
                ["selected"] = value.Selected,
                ["dockOpen"] = value.DockOpen,
                ["showing"] = value.Showing,
                ["previousShowing"] = value.PreviousShowing,
                ["changed"] = value.Changed,
            };
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = payload.ToJsonString(JsonOptions) }],
                IsError = false,
            };
        }
        result.TryGetError(out var err);
        return ToolErrorPayload.AsCallToolResult(err!, JsonOptions);
    }

    private static CallToolResult InternalError(Exception ex) =>
        ToolErrorPayload.InternalError(ex, JsonOptions);
}
