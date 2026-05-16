using System.Collections.Immutable;
using System.Text.Json;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Generic describer for GML-encoded specs that expose their features
/// via <see cref="IGmlFeature"/> through <see cref="GmlFeatureAccessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// One instance is registered per spec name (S-122, S-125, S-127,
/// S-128, S-129, S-131, S-201, S-411, S-421). The describer serialises
/// the feature's identity, type, geometry kind, simple attributes, and
/// complex attributes — the minimum set of information an agent needs
/// after locating features with <see cref="QueryFeaturesTool"/>.
/// </para>
/// <para>
/// References (xlink:href cross-feature bindings) are intentionally
/// returned as <see cref="ImmutableArray{T}.Empty"/> here because each
/// spec models references with its own type
/// (<c>S125InformationReference</c>, <c>S131Reference</c>,
/// <c>S201FeatureReference</c>, …) and unifying them requires a
/// follow-up slice. S-124 — which carries <c>GmlReference</c>-shaped
/// references with full cross-dataset resolution — keeps its
/// purpose-built <see cref="S124FeatureDescriber"/>.
/// </para>
/// </remarks>
internal sealed class GmlFeatureDescriber : ISpecFeatureDescriber
{
    public string SpecName { get; }

    public GmlFeatureDescriber(string specName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specName);
        SpecName = specName;
    }

    public ToolResult<DescribeFeatureResult> Describe(FeatureDescriberContext context)
    {
        var features = GmlFeatureAccessor.GetFeatures(context.Dataset);
        if (features is null)
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new SpecNotSupportedForTool(context.Dataset.Spec, DescribeFeatureTool.Name));
        }

        IGmlFeature? feature = null;
        foreach (var f in features)
        {
            if (string.Equals(f.Id, context.FeatureId, StringComparison.Ordinal))
            {
                feature = f;
                break;
            }
        }

        if (feature is null)
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new FeatureNotFound(context.Dataset.Id, context.FeatureId));
        }

        var attributesJson = SerializeFeature(feature);
        return ToolResult<DescribeFeatureResult>.Ok(
            new DescribeFeatureResult(
                context.Dataset.Spec,
                feature.FeatureType,
                attributesJson,
                ImmutableArray<FeatureReference>.Empty));
    }

    private static JsonElement SerializeFeature(IGmlFeature feature)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = feature.Id,
            ["featureType"] = feature.FeatureType,
            ["geometryType"] = feature.GeometryType.ToString(),
            ["attributes"] = feature.Attributes,
            ["complexAttributes"] = feature.GmlComplexAttributes
                .Select(c => new
                {
                    code = c.Code,
                    subAttributes = c.SubAttributes,
                })
                .ToArray(),
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return JsonSerializer.Deserialize<JsonElement>(bytes);
    }
}
