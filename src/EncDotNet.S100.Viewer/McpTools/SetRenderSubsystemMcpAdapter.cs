using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Wraps <see cref="SetRenderSubsystemTool"/> as an MCP server tool.</summary>
internal static class SetRenderSubsystemMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Switches the live viewer's base-plane render subsystem between 'Mapsui' (the 'A' arm) and " +
        "'TiledScene' (the experimental tiled/async 'B' arm). The 'A'/'B' shorthand is also accepted " +
        "(case-insensitive). Idempotent — setting the current subsystem is a no-op. Returns the applied " +
        "and previous subsystem. Refused when S100_RENDER_SUBSYSTEM pins the subsystem at startup. " +
        "Intended for scripted A↔B soak runs — see docs/mcp-server.md.";

    public static McpServerTool Create(SetRenderSubsystemTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Render subsystem: 'Mapsui' (A) or 'TiledScene' (B); the 'A'/'B' shorthand is accepted (case-insensitive).")] string subsystem,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(new SetRenderSubsystemRequest(subsystem), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetRenderSubsystemTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(Func<Task<ToolResult<SetRenderSubsystemResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return InternalError(ex); }
    }

    internal static CallToolResult TranslateResult(ToolResult<SetRenderSubsystemResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var payload = new JsonObject
            {
                ["subsystem"] = value!.Subsystem,
                ["previous"] = value.Previous,
            };
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = payload.ToJsonString(JsonOptions) }],
                IsError = false,
            };
        }
        result.TryGetError(out var err);
        return Failure(err!);
    }

    private static CallToolResult Failure(ToolError error) =>
        ToolErrorPayload.AsCallToolResult(error, JsonOptions);

    private static CallToolResult InternalError(Exception ex) =>
        ToolErrorPayload.InternalError(ex, JsonOptions);
}
