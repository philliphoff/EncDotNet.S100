using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.MutableTools;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// Translates a tool's <see cref="ToolResult{T}"/> into an MCP result for the
/// shared tools any host registers (the Library tools, #792): success as the
/// value's JSON, failure as <see cref="ToolErrorPayload"/>'s
/// <c>{ code, message, details }</c>, and an unexpected exception as
/// <c>internal_error</c>. Serialization uses <see cref="McpJson.Options"/>.
/// </summary>
internal static class McpToolDispatch
{
    /// <summary>Runs <paramref name="resultFactory"/> and translates its outcome.</summary>
    public static async Task<CallToolResult> DispatchAsync<T>(Func<Task<ToolResult<T>>> resultFactory)
    {
        try
        {
            var result = await resultFactory().ConfigureAwait(false);
            return Translate(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolErrorPayload.InternalError(ex, McpJson.Options);
        }
    }

    /// <summary>Translates a completed <see cref="ToolResult{T}"/>.</summary>
    public static CallToolResult Translate<T>(ToolResult<T> result)
    {
        if (!result.TryGetValue(out var value))
        {
            result.TryGetError(out var error);
            return ToolErrorPayload.AsCallToolResult(error!, McpJson.Options);
        }

        var node = JsonSerializer.SerializeToNode(value, McpJson.Options.GetTypeInfo(typeof(T))) ?? new JsonObject();
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = node.ToJsonString(McpJson.Options) }],
            IsError = false,
        };
    }
}
