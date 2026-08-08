using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines.Query;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Mcp.MutableTools;

/// <summary>
/// Shared <see cref="ToolError"/> → <see cref="CallToolResult"/> translation
/// used by every mutating-tool adapter that emits a single text content block on
/// failure. Keeps the failure shape (<c>{ code, message, details }</c>)
/// consistent across tools. Promoted from the desktop viewer so the CLI host and
/// the viewer share one implementation.
/// </summary>
internal static class ToolErrorPayload
{
    /// <summary>Serialises a typed <see cref="ToolError"/> as an error result.</summary>
    public static CallToolResult AsCallToolResult(ToolError error, JsonSerializerOptions json)
    {
        var details = JsonSerializer.SerializeToNode(error, error.GetType(), json) as JsonObject
            ?? new JsonObject();
        details.Remove("code");
        details.Remove("message");
        details.Remove("Code");
        details.Remove("Message");

        var payload = new JsonObject
        {
            ["code"] = error.Code,
            ["message"] = error.Message,
            ["details"] = details,
        };
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = payload.ToJsonString(json) }],
            IsError = true,
        };
    }

    /// <summary>Serialises an unexpected exception as an <c>internal_error</c> result.</summary>
    public static CallToolResult InternalError(Exception ex, JsonSerializerOptions json)
    {
        var payload = new JsonObject
        {
            ["code"] = "internal_error",
            ["message"] = ex.Message,
            ["details"] = new JsonObject
            {
                ["exceptionType"] = ex.GetType().FullName,
            },
        };
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = payload.ToJsonString(json) }],
            IsError = true,
        };
    }
}
