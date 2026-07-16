using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Wraps <see cref="AwaitRenderIdleTool"/> as an MCP server tool.</summary>
internal static class AwaitRenderIdleMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Blocks until the viewer's live map settles — no completed paint, graphics-refresh " +
        "request, or busy layer for a continuous quiet period — or until a timeout elapses. " +
        "Use it between 'set_viewport' and 'render_to_image' so the screenshot reflects a " +
        "settled view instead of racing the render pass. Measures the on-screen map paint " +
        "loop (not the offscreen render_to_image clone) and always waits at least the quiet " +
        "period. Returns whether the map went idle, how long it waited, and how many paints " +
        "completed while waiting.";

    public static McpServerTool Create(AwaitRenderIdleTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Continuous inactivity (ms) that qualifies as idle; also the minimum time the call waits. Null defaults to 250. Clamped to [0, 10000].")] int? quietPeriodMs = null,
            [Description("Maximum total time to wait (ms). Null defaults to 5000. Clamped to [50, 120000].")] int? timeoutMs = null,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(new AwaitRenderIdleRequest(quietPeriodMs, timeoutMs), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = AwaitRenderIdleTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<AwaitRenderIdleResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ToolErrorPayload.InternalError(ex, JsonOptions); }
    }

    internal static CallToolResult TranslateResult(ToolResult<AwaitRenderIdleResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var payload = new JsonObject
            {
                ["wentIdle"] = value!.WentIdle,
                ["timedOut"] = value.TimedOut,
                ["waitedMs"] = value.WaitedMs,
                ["paintsObserved"] = value.PaintsObserved,
                ["quietForMs"] = value.QuietForMs,
                ["quietPeriodMs"] = value.QuietPeriodMs,
                ["timeoutMs"] = value.TimeoutMs,
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
