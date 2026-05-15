using System.Collections.Immutable;
using System.Text.Json;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Describer strategy for S-124 Navigational Warnings. Looks up the
/// feature by ID, serialises its attributes and complex attributes as
/// JSON, and projects each <see cref="GmlReference"/> against the
/// catalog snapshot to flag whether the target was loaded.
/// </summary>
internal sealed class S124FeatureDescriber : ISpecFeatureDescriber
{
    public string SpecName => "S-124";

    public ToolResult<DescribeFeatureResult> Describe(FeatureDescriberContext context)
    {
        if (context.Dataset.Data is not S124DatasetData s124)
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new SpecNotSupportedForTool(context.Dataset.Spec, DescribeFeatureTool.Name));
        }

        var model = s124.Model;

        S124Feature? feature = null;
        foreach (var f in model.Features)
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

        var attributesJson = SerializeAttributes(feature);
        var references = ProjectReferences(feature.References, context);

        return ToolResult<DescribeFeatureResult>.Ok(
            new DescribeFeatureResult(
                context.Dataset.Spec,
                feature.FeatureType,
                attributesJson,
                references));
    }

    private static JsonElement SerializeAttributes(S124Feature feature)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = feature.Id,
            ["featureType"] = feature.FeatureType,
            ["geometryType"] = feature.GeometryType.ToString(),
            ["attributes"] = feature.Attributes,
            ["complexAttributes"] = feature.ComplexAttributes.Select(c => new
            {
                code = c.Code,
                subAttributes = c.SubAttributes,
            }).ToArray(),
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return JsonSerializer.Deserialize<JsonElement>(bytes);
    }

    private static ImmutableArray<FeatureReference> ProjectReferences(
        ImmutableArray<GmlReference> references,
        FeatureDescriberContext context)
    {
        if (references.IsDefaultOrEmpty)
        {
            return ImmutableArray<FeatureReference>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<FeatureReference>(references.Length);
        foreach (var reference in references)
        {
            var targetId = ExtractFragment(reference.Href);
            var (datasetId, resolved) = ResolveTarget(targetId, context);
            builder.Add(new FeatureReference(
                reference.Role,
                reference.Href,
                datasetId,
                targetId,
                resolved));
        }
        return builder.MoveToImmutable();
    }

    private static string? ExtractFragment(string href)
    {
        if (string.IsNullOrEmpty(href)) return null;
        var hash = href.IndexOf('#');
        if (hash < 0)
        {
            // Some encoders omit the leading '#'; treat the whole href as the ID.
            return href;
        }
        var fragment = href[(hash + 1)..];
        return fragment.Length == 0 ? null : fragment;
    }

    private static (DatasetId? DatasetId, bool Resolved) ResolveTarget(
        string? targetId,
        FeatureDescriberContext context)
    {
        if (targetId is null)
        {
            return (null, false);
        }

        // Look in the originating dataset first.
        if (TargetExistsInDataset(context.Dataset, targetId))
        {
            return (context.Dataset.Id, true);
        }

        // Fall back to other loaded datasets in the snapshot.
        foreach (var dataset in context.Snapshot)
        {
            if (dataset.Id == context.Dataset.Id) continue;
            if (TargetExistsInDataset(dataset, targetId))
            {
                return (dataset.Id, true);
            }
        }

        return (null, false);
    }

    private static bool TargetExistsInDataset(LoadedDataset dataset, string targetId)
    {
        if (dataset.Data is not S124DatasetData s124) return false;

        foreach (var f in s124.Model.Features)
        {
            if (string.Equals(f.Id, targetId, StringComparison.Ordinal)) return true;
        }
        foreach (var info in s124.Model.InformationTypes)
        {
            if (string.Equals(info.Id, targetId, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
