using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Spec;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// MCP wrapper over <see cref="DescribeFeatureService"/> — describes a
/// single feature in a loaded dataset. The reusable query logic lives in
/// <see cref="DescribeFeatureService"/>
/// (<c>EncDotNet.S100.Datasets.Pipelines.Query</c>); this class exists only
/// to surface it as an MCP tool.
/// </summary>
public sealed class DescribeFeatureTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = DescribeFeatureService.Name;

    private readonly DescribeFeatureService _service;

    /// <summary>Creates a new <see cref="DescribeFeatureTool"/> with the default registry.</summary>
    public DescribeFeatureTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _service = new DescribeFeatureService(catalog);
    }

    /// <summary>Creates a new <see cref="DescribeFeatureTool"/> with a custom registry.</summary>
    public DescribeFeatureTool(IDatasetCatalog catalog, FeatureDescriberRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(registry);
        _service = new DescribeFeatureService(catalog, registry);
    }

    /// <summary>Executes the tool by delegating to the shared service.</summary>
    public async Task<ToolResult<DescribeFeatureResult>> InvokeAsync(
        DescribeFeatureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await _service.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        return request.IncludeGeometry || !result.TryGetValue(out var value)
            ? result
            : ToolResult<DescribeFeatureResult>.Ok(value! with { Attributes = WithoutCoordinates(value.Attributes) });
    }

    /// <summary>
    /// The attribute payload with its <c>geometry</c>'s coordinates replaced
    /// by counts (#1628): the primitive and bounding box stay, and
    /// <c>coordinateCount</c>, <c>interiorRingCount</c> and
    /// <c>coordinatesOmitted</c> say what was left out.
    /// </summary>
    /// <param name="attributes">The payload, as <see cref="DescribeFeatureResult.Attributes"/>.</param>
    /// <returns>The trimmed payload; the payload itself when it has no geometry object.</returns>
    public static JsonElement WithoutCoordinates(JsonElement attributes)
    {
        if (attributes.ValueKind != JsonValueKind.Object
            || JsonNode.Parse(attributes.GetRawText()) is not JsonObject root
            || root["geometry"] is not JsonObject geometry)
        {
            return attributes;
        }

        var trimmed = new JsonObject();
        foreach (var (key, node) in geometry)
        {
            switch (key)
            {
                case "coordinates":
                    trimmed["coordinateCount"] = node is JsonArray points ? points.Count : 0;
                    break;
                case "interiorRings":
                    trimmed["interiorRingCount"] = node is JsonArray rings ? rings.Count : 0;
                    break;
                case "depths":
                    break;
                default:
                    trimmed[key] = node?.DeepClone();
                    break;
            }
        }

        trimmed["coordinatesOmitted"] = true;
        root["geometry"] = trimmed;
        using var document = JsonDocument.Parse(root.ToJsonString());
        return document.RootElement.Clone();
    }
}
