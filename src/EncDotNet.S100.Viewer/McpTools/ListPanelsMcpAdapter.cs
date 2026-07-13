using System;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Wraps <see cref="ListPanelsTool"/> as an MCP server tool.</summary>
internal static class ListPanelsMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Lists the live viewer's activity panels (the tabs in the left / right / bottom docks) and their "
        + "current visibility. Read-only — snapshots the activity bar without changing it. Each panel reports "
        + "'id', 'title', 'dock' (Left|Right|Bottom), 'available' (registered in the activity bar right now; "
        + "some panels such as Vessels and Helm are conditionally available), 'selected' (active tab in its "
        + "dock), 'dockOpen' (its dock is expanded), and 'showing' (actually visible = available && selected "
        + "&& dockOpen). Call this to discover valid panel ids for set_panel and to verify a show / hide took "
        + "effect. See docs/mcp-server.md for the read-only / mutating split.";

    public static McpServerTool Create(ListPanelsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) => DispatchAsync(() => inner.InvokeAsync(ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = ListPanelsTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(Func<Task<ToolResult<ListPanelsResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return InternalError(ex); }
    }

    internal static CallToolResult TranslateResult(ToolResult<ListPanelsResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var panels = new JsonArray();
            foreach (var p in value!.Panels)
            {
                panels.Add(new JsonObject
                {
                    ["id"] = p.Id,
                    ["title"] = p.Title,
                    ["dock"] = p.Dock,
                    ["available"] = p.Available,
                    ["selected"] = p.Selected,
                    ["dockOpen"] = p.DockOpen,
                    ["showing"] = p.Showing,
                });
            }
            var payload = new JsonObject { ["panels"] = panels };
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
