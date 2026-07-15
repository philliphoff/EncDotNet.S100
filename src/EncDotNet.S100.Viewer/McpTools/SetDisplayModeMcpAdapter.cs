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

/// <summary>Wraps <see cref="SetDisplayModeTool"/> as an MCP server tool.</summary>
internal static class SetDisplayModeMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Mutates the live viewer's explicit per-spec display mode (S-100 Part 9 §11.7). "
        + "Today only S-411 sea ice declares more than one mode: 'ice-concentration' (default), "
        + "'ice-sod' (stage of development), or 'ice-navigational' (a PROVISIONAL concentration-derived "
        + "preview, not a POLARIS/RIO product). Accepts the same friendly tokens as the CLI "
        + "'render --display-mode' flag. Idempotent — setting the current mode is a no-op. Returns the "
        + "applied and previous mode ids and whether the applied mode is provisional. "
        + "See docs/mcp-server.md for the read-only / mutating split.";

    public static McpServerTool Create(SetDisplayModeTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Mode token: 'ice-concentration', 'ice-sod', or 'ice-navigational' (case-insensitive).")] string mode,
            [Description("Product-spec code the mode applies to. Defaults to 'S-411'.")] string? spec = null,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(new SetDisplayModeRequest(mode, spec), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetDisplayModeTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(Func<Task<ToolResult<SetDisplayModeResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return InternalError(ex); }
    }

    internal static CallToolResult TranslateResult(ToolResult<SetDisplayModeResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var payload = new JsonObject
            {
                ["spec"] = value!.Spec,
                ["mode"] = value.Mode,
                ["previous"] = value.Previous,
                ["provisional"] = value.Provisional,
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
