using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Wraps <see cref="GetViewerStateTool"/> as an <see cref="McpServerTool"/>.
/// Read-only counterpart to the mutating set_* tools: lets scripted runs
/// read the live viewport, palette, display category, time clock,
/// dataset count, and own-ship state in one call.
/// </summary>
internal static class GetViewerStateMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Returns a read-only snapshot of the live viewer's render and navigation state: the current " +
        "viewport (WGS-84 bbox, centre, and web-mercator zoom), the active palette and ECDIS display " +
        "category, the global time-clock state (current/min/max instants, current index, sample count), " +
        "the number of loaded datasets, and the simulated own-ship kinematics. The read-side counterpart " +
        "to set_viewport / set_palette / set_display_category / set_time_step / set_own_ship — use it to " +
        "assert preconditions and verify results without a side-effecting write. Each section is null when " +
        "its subsystem is not yet ready (e.g. before the map control finishes initialising). Read-only.";

    public static McpServerTool Create(GetViewerStateTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetViewerStateTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<GetViewerStateResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ToolErrorPayload.InternalError(ex, JsonOptions); }
    }

    internal static CallToolResult TranslateResult(ToolResult<GetViewerStateResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = json }],
                IsError = false,
            };
        }
        result.TryGetError(out var err);
        return ToolErrorPayload.AsCallToolResult(err!, JsonOptions);
    }
}
