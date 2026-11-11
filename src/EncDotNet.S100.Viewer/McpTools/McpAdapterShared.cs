using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp;
using EncDotNet.S100.Mcp.MutableTools;
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
    /// The serializer options every viewer adapter shares: the shared MCP
    /// options' shape, with source-generated metadata for the viewer's own
    /// results, errors and parameters chained before the shared tools'
    /// (issue #764). Nothing is resolved by reflection.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = JsonTypeInfoResolver.Combine(ViewerMcpJsonContext.Default, McpJson.TypeInfoResolver),
        };
        options.MakeReadOnly();
        return options;
    }

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
        var node = JsonSerializer.SerializeToNode(value, (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T))) ?? new JsonObject();
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = node.ToJsonString(Options) }],
            IsError = false,
        };
    }

    private static CallToolResult Failure(ToolError error) =>
        ToolErrorPayload.AsCallToolResult(error, Options);

    private static CallToolResult InternalError(Exception ex) =>
        ToolErrorPayload.InternalError(ex, Options);
}
