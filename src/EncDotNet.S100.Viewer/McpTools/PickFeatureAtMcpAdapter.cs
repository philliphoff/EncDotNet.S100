using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Wraps <see cref="PickFeatureAtTool"/> as an <see cref="McpServerTool"/>.
/// Read-only: resolves a pixel of a render_to_image-style capture to a
/// world point and the features/datasets under it.
/// </summary>
internal static class PickFeatureAtMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Resolves the world point under a pixel of a render_to_image-style capture and returns the " +
        "GML features and datasets whose bounds contain it. Ask \"what is at pixel (x, y) of the view " +
        "I rendered?\" to turn a screenshot coordinate into concrete feature IDs — precise visual " +
        "assertions without pixel-diff baselines. Width/height default to render_to_image's 1024x768 so " +
        "coordinates line up between the two tools. Matching is at bounding-box precision (a feature is " +
        "returned when its bbox contains the point, not when a rendered symbol pixel is hit). Coverage " +
        "products and S-101 contribute datasets but not features. Viewer-injected and read-only.";

    public static McpServerTool Create(PickFeatureAtTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        var del = (
            [Description("Horizontal pixel offset from the image's left edge. Must be within [0, width].")] double x,
            [Description("Vertical pixel offset from the image's top edge. Must be within [0, height].")] double y,
            [Description("Reference image width in pixels; null defaults to 1024. Clamped to [64, 4096].")] int? width = null,
            [Description("Reference image height in pixels; null defaults to 768. Clamped to [64, 4096].")] int? height = null,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(
                new PickFeatureAtRequest(x, y, width, height), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = PickFeatureAtTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<PickFeatureAtResult>>> factory)
    {
        try
        {
            var result = await factory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ToolErrorPayload.InternalError(ex, JsonOptions); }
    }

    internal static CallToolResult TranslateResult(ToolResult<PickFeatureAtResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = json }],
                IsError = false,
            };
        }
        result.TryGetError(out var err);
        return ToolErrorPayload.AsCallToolResult(err!, JsonOptions);
    }
}
