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

/// <summary>Wraps <see cref="SetPaletteTool"/> as an MCP server tool.</summary>
internal static class SetPaletteMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const string Description =
        "Mutates the live viewer's active map palette. Allowed values: 'Day', 'Dusk', 'Night' " +
        "(case-insensitive). Idempotent — setting the current palette is a no-op. Returns the " +
        "applied and previous palette so callers can detect no-op invocations. Distinguished " +
        "from read-only tools (e.g. render_to_image) — see docs/mcp-server.md.";

    public static McpServerTool Create(SetPaletteTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Palette name: 'Day', 'Dusk', or 'Night' (case-insensitive).")] string palette,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(new SetPaletteRequest(palette), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetPaletteTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(Func<Task<ToolResult<SetPaletteResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return InternalError(ex); }
    }

    internal static CallToolResult TranslateResult(ToolResult<SetPaletteResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var payload = new JsonObject
            {
                ["palette"] = value!.Palette,
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
