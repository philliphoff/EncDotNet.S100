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

/// <summary>Wraps <see cref="SetDisplayCategoryTool"/> as an MCP server tool.</summary>
internal static class SetDisplayCategoryMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const string Description =
        "Mutates the live viewer's ECDIS display category. Allowed values: 'DisplayBase', " +
        "'Standard', 'OtherInformation', 'All' (case-insensitive). Idempotent — setting the " +
        "current category is a no-op. Returns the applied and previous category. " +
        "See docs/mcp-server.md for the read-only / mutating split.";

    public static McpServerTool Create(SetDisplayCategoryTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Category: 'DisplayBase', 'Standard', 'OtherInformation', or 'All' (case-insensitive).")] string displayCategory,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(new SetDisplayCategoryRequest(displayCategory), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetDisplayCategoryTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(Func<Task<ToolResult<SetDisplayCategoryResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return InternalError(ex); }
    }

    internal static CallToolResult TranslateResult(ToolResult<SetDisplayCategoryResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var payload = new JsonObject
            {
                ["displayCategory"] = value!.DisplayCategory,
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
