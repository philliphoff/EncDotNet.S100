using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Shared <see cref="ToolError"/> → <see cref="CallToolResult"/> translation
/// used by every viewer-side MCP adapter that emits a single text content
/// block on failure. Keeps the failure shape consistent across tools.
/// </summary>
internal static class ToolErrorPayload
{
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
