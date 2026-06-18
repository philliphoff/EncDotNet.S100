using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Wraps <see cref="GetRenderStatsTool"/> as an MCP server tool.</summary>
internal static class GetRenderStatsMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Reports the cost of the viewer's most recently completed on-screen map paint: " +
        "wall-clock frame duration (ms), interval since the previous paint, total style " +
        "draw calls, and a per-style breakdown (calls + duration). Use it to measure " +
        "rendering performance across pan/zoom, palette, or time-step changes. Pair with " +
        "'await_render_idle' so the reported paint reflects a settled view. Describes the " +
        "live map paint, not the offscreen render_to_image clone; returns hasData=false " +
        "when no paint has occurred yet.";

    public static McpServerTool Create(GetRenderStatsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [System.ComponentModel.Description(
                "When true, clears the rolling paint window after reading it, so the next " +
                "call's window covers only paints observed afterwards. Use to bracket a " +
                "measurement phase (reset before a pan/zoom burst, read after).")]
            bool resetWindow = false,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(new GetRenderStatsRequest(resetWindow), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetRenderStatsTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<GetRenderStatsResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ToolErrorPayload.InternalError(ex, JsonOptions); }
    }

    internal static CallToolResult TranslateResult(ToolResult<GetRenderStatsResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var styles = new JsonArray();
            foreach (var s in value!.Styles)
            {
                styles.Add(new JsonObject
                {
                    ["style"] = s.Style,
                    ["calls"] = s.Calls,
                    ["durationMs"] = s.DurationMs,
                });
            }

            var payload = new JsonObject
            {
                ["hasData"] = value.HasData,
                ["frameDurationMs"] = value.FrameDurationMs,
                ["intervalMs"] = value.IntervalMs,
                ["totalDrawCalls"] = value.TotalDrawCalls,
                ["paintSequence"] = value.PaintSequence,
                ["capturedAtUtc"] = value.CapturedAtUtc,
                ["styles"] = styles,
                ["window"] = new JsonObject
                {
                    ["count"] = value.Window.Count,
                    ["firstSequence"] = value.Window.FirstSequence,
                    ["lastSequence"] = value.Window.LastSequence,
                    ["frameMaxMs"] = value.Window.FrameMaxMs,
                    ["frameMeanMs"] = value.Window.FrameMeanMs,
                    ["frameP95Ms"] = value.Window.FrameP95Ms,
                    ["vectorMaxMs"] = value.Window.VectorMaxMs,
                    ["vectorMeanMs"] = value.Window.VectorMeanMs,
                    ["vectorP95Ms"] = value.Window.VectorP95Ms,
                    ["maxTotalDrawCalls"] = value.Window.MaxTotalDrawCalls,
                },
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
