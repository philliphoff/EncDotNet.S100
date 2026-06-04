using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Wraps <see cref="CloseDatasetTool"/> as an MCP server tool.</summary>
internal static class CloseDatasetMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const string Description =
        "Unloads a currently-loaded dataset from the live viewer by its catalog id (as returned by " +
        "list_datasets or open_dataset), using the viewer's existing close code path so agents can " +
        "measure the unload hot path. An unknown or already-removed id resolves gracefully as a " +
        "non-error result with removed:false. Returns what was removed. MUTATING.";

    public static McpServerTool Create(CloseDatasetTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Catalog id of the dataset to unload (from list_datasets or open_dataset).")] string id,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(new CloseDatasetRequest(id), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = CloseDatasetTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<CloseDatasetResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ToolErrorPayload.InternalError(ex, JsonOptions); }
    }

    internal static CallToolResult TranslateResult(ToolResult<CloseDatasetResult> result)
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
                ["id"] = value.Id,
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
