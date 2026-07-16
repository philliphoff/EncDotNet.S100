using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Wraps <see cref="CloseAllDatasetsTool"/> as an MCP server tool.</summary>
internal static class CloseAllDatasetsMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Unloads every currently-loaded dataset from the live viewer using the viewer's existing close " +
        "code path, so agents can run load/render/unload retention loops without restarting the process. " +
        "Returns what was removed. MUTATING.";

    public static McpServerTool Create(CloseAllDatasetsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = CloseAllDatasetsTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<CloseAllDatasetsResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ToolErrorPayload.InternalError(ex, JsonOptions); }
    }

    internal static CallToolResult TranslateResult(ToolResult<CloseAllDatasetsResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var removed = new JsonArray();
            foreach (var d in value!.RemovedDatasets)
            {
                removed.Add(new JsonObject
                {
                    ["id"] = d.Id,
                    ["spec"] = d.Spec,
                });
            }
            var payload = new JsonObject
            {
                ["removed"] = value.Removed,
                ["count"] = value.Count,
                ["removedDatasets"] = removed,
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
