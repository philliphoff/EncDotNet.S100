using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines.Query;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Shared JSON options and result-translation helpers for viewer-only MCP
/// adapters (routes, Timeline, dataset state, notifications). Centralises the
/// success / failure / internal-error wire shapes so every adapter that uses
/// it emits an identical payload contract.
/// </summary>
internal static class McpAdapterShared
{
    /// <summary>
    /// The serializer options every adapter using this helper shares. A configured
    /// <c>TypeInfoResolver</c> is required so the MCP SDK can call
    /// <see cref="JsonSerializerOptions.MakeReadOnly()"/> in the published
    /// (reflection-disabled) viewer without throwing.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

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
            return InternalError(ex);
        }
    }

    /// <summary>Translates a completed <see cref="ToolResult{T}"/> to a wire result.</summary>
    public static CallToolResult Translate<T>(ToolResult<T> result)
    {
        if (result.TryGetValue(out var value))
            return Success(value);
        result.TryGetError(out var err);
        return Failure(err!);
    }

    private static CallToolResult Success<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, Options) ?? new JsonObject();
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = node.ToJsonString(Options) }],
            IsError = false,
        };
    }

    private static CallToolResult Failure(ToolError error)
    {
        var details = JsonSerializer.SerializeToNode(error, error.GetType(), Options) as JsonObject
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
            Content = [new TextContentBlock { Text = payload.ToJsonString(Options) }],
            IsError = true,
        };
    }

    private static CallToolResult InternalError(Exception ex)
    {
        var payload = new JsonObject
        {
            ["code"] = "internal_error",
            ["message"] = ex.Message,
            ["details"] = new JsonObject { ["exceptionType"] = ex.GetType().FullName },
        };
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = payload.ToJsonString(Options) }],
            IsError = true,
        };
    }
}
