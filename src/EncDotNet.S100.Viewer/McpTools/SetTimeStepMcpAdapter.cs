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

/// <summary>Wraps <see cref="SetTimeStepTool"/> as an MCP server tool.</summary>
internal static class SetTimeStepMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const string Description =
        "Mutates the live viewer's global time clock to a specific sample. Supply EITHER " +
        "'index' (a 0-based integer into list_time_steps) OR 'timestamp' (ISO-8601, snapped " +
        "to the nearest sample). Mixing the two is rejected. Returns the resolved index and " +
        "snapped timestamp so callers can stitch repeated runs without recomputing the " +
        "timeline. Counterpart to the --time-step CLI flag, but applicable mid-session.";

    public static McpServerTool Create(SetTimeStepTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("0-based index into the aggregated time samples (see list_time_steps). Mutually exclusive with timestamp.")] int? index = null,
            [Description("ISO-8601 timestamp; snapped to the nearest available sample. Mutually exclusive with index.")] string? timestamp = null,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(new SetTimeStepRequest(index, timestamp), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetTimeStepTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<SetTimeStepResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ToolErrorPayload.InternalError(ex, JsonOptions); }
    }

    internal static CallToolResult TranslateResult(ToolResult<SetTimeStepResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var payload = new JsonObject
            {
                ["mode"] = value!.Mode,
                ["index"] = value.Index,
                ["timestamp"] = value.Timestamp,
                ["sampleCount"] = value.SampleCount,
                ["previous"] = value.Previous,
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
}
