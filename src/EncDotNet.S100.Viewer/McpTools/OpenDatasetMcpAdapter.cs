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

/// <summary>Wraps <see cref="OpenDatasetTool"/> as an MCP server tool.</summary>
internal static class OpenDatasetMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Loads a dataset into the live viewer using its existing open code path, so agents can " +
        "measure the load hot path. 'path' is a local file (S-101 .000, HDF5 .h5, GML, etc.) OR an " +
        "exchange set (a folder containing CATALOG.XML, or a .zip of one); the kind is auto-detected. " +
        "'spec' optionally forces a product-spec hint (e.g. \"S-102\") for single-file loads. Returns the " +
        "resulting catalog id(s), spec, bounding box, and the measured load duration. MUTATING.";

    public static McpServerTool Create(OpenDatasetTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Local filesystem path to a dataset file or an exchange set (folder containing CATALOG.XML, or a .zip of one).")] string path,
            [Description("Optional explicit product-spec hint (e.g. \"S-102\") for single-file loads; ignored for exchange sets.")] string? spec = null,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(new OpenDatasetRequest(path, spec), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = OpenDatasetTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<OpenDatasetResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ToolErrorPayload.InternalError(ex, JsonOptions); }
    }

    internal static CallToolResult TranslateResult(ToolResult<OpenDatasetResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var datasets = new JsonArray();
            foreach (var d in value!.Datasets)
            {
                datasets.Add(new JsonObject
                {
                    ["id"] = d.Id,
                    ["spec"] = d.Spec,
                    ["southLatitude"] = d.SouthLatitude,
                    ["westLongitude"] = d.WestLongitude,
                    ["northLatitude"] = d.NorthLatitude,
                    ["eastLongitude"] = d.EastLongitude,
                });
            }
            var payload = new JsonObject
            {
                ["path"] = value.Path,
                ["kind"] = value.Kind,
                ["count"] = value.Count,
                ["loadDurationMs"] = value.LoadDurationMs,
                ["timedOut"] = value.TimedOut,
                ["datasets"] = datasets,
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
