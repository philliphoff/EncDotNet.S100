using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Describer strategy for S-102 Bathymetric Surface coverages.
/// Resolves a feature by an HDF5-style group path
/// (<c>BathymetryCoverage.01</c>, <c>BathymetryCoverage.1</c>, or bare
/// <c>BathymetryCoverage</c>) and serialises the coverage instance's
/// grid metadata, value-field schema, and depth statistics as JSON.
/// </summary>
/// <remarks>
/// <para>
/// The catalog projects each S-102 dataset to a single
/// <see cref="S102CoverageSource"/> wrapping coverage index 0
/// (<see cref="EncDotNet.S100.Mcp.Tools.Catalog.S102CoverageData"/>),
/// so this describer recognises only one instance id. Attribute paths
/// follow S-102 Edition 2.1.0 §10 — origin/spacing/grid extents under
/// <c>BathymetryCoverage.NN</c>, NoData fill value <c>1_000_000f</c>.
/// </para>
/// </remarks>
internal sealed class S102FeatureDescriber : ISpecFeatureDescriber
{
    private const string FeatureType = "BathymetryCoverage";
    private const float FillValue = S102CoverageSource.FillValue;

    public string SpecName => "S-102";

    public ToolResult<DescribeFeatureResult> Describe(FeatureDescriberContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Dataset.Data is not S102CoverageData s102)
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new SpecNotSupportedForTool(context.Dataset.Spec, DescribeFeatureTool.Name));
        }

        if (!TryParseFeatureId(context.FeatureId, out _))
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new FeatureNotFound(context.Dataset.Id, context.FeatureId));
        }

        var dataset = s102.Source.Dataset;
        var coverage = s102.Source.Coverage;
        var metadata = s102.Source.Metadata;

        var attributes = SerializeAttributes(dataset, coverage, metadata);

        return ToolResult<DescribeFeatureResult>.Ok(new DescribeFeatureResult(
            context.Dataset.Spec,
            FeatureType,
            attributes,
            ImmutableArray<FeatureReference>.Empty));
    }

    /// <summary>
    /// Accepts <c>"BathymetryCoverage"</c>, <c>"BathymetryCoverage.NN"</c>,
    /// or <c>"BathymetryCoverage.N"</c>. Only instance 1 (the single
    /// coverage exposed by the catalog) is recognised.
    /// </summary>
    internal static bool TryParseFeatureId(string id, out int instance)
    {
        instance = 1;
        if (string.IsNullOrEmpty(id)) return false;

        if (string.Equals(id, FeatureType, StringComparison.Ordinal))
        {
            return true;
        }

        if (!id.StartsWith(FeatureType + ".", StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = id[(FeatureType.Length + 1)..];
        if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out instance))
        {
            return false;
        }

        // The catalog only projects coverage index 0 (instance 1).
        return instance == 1;
    }

    private static JsonElement SerializeAttributes(
        S102Dataset dataset,
        BathymetryCoverage coverage,
        EncDotNet.S100.Pipelines.Coverage.CoverageMetadata metadata)
    {
        var (minDepth, maxDepth, noDataCount) = ComputeDepthRange(coverage.Values);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["instanceId"] = $"{FeatureType}.01",
            ["featureType"] = FeatureType,
            ["gridMetadata"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["originLatitude"] = coverage.OriginLatitude,
                ["originLongitude"] = coverage.OriginLongitude,
                ["spacingLatitudinal"] = coverage.SpacingLatitudinal,
                ["spacingLongitudinal"] = coverage.SpacingLongitudinal,
                ["numPointsLatitudinal"] = coverage.NumPointsLatitudinal,
                ["numPointsLongitudinal"] = coverage.NumPointsLongitudinal,
                ["startSequence"] = coverage.StartSequence,
            },
            ["boundingBox"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["south"] = metadata.Extent.SouthLatitude,
                ["west"] = metadata.Extent.WestLongitude,
                ["north"] = metadata.Extent.NorthLatitude,
                ["east"] = metadata.Extent.EastLongitude,
            },
            ["horizontalCRS"] = dataset.HorizontalCRS,
            ["epoch"] = dataset.Epoch,
            ["verticalDatum"] = metadata.VerticalDatum,
            ["noDataValue"] = FillValue,
            ["valueFields"] = metadata.ValueFields.Select(v => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = v.Name,
                ["type"] = v.Type.ToString(),
                ["units"] = v.Units,
                ["fillValue"] = v.FillValue,
            }).ToArray(),
            ["depthRange"] = minDepth is null
                ? null
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["min"] = minDepth,
                    ["max"] = maxDepth,
                    ["nodataCount"] = noDataCount,
                },
            ["geographicIdentifier"] = dataset.GeographicIdentifier,
            ["issueDate"] = dataset.IssueDate,
            ["metadata"] = dataset.Metadata,
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return JsonSerializer.Deserialize<JsonElement>(bytes);
    }

    /// <summary>
    /// Single pass over the depth field, skipping <see cref="FillValue"/>.
    /// Returns <c>(null, null, count)</c> when every cell is NoData.
    /// </summary>
    private static (float? Min, float? Max, int NoDataCount) ComputeDepthRange(BathymetryValue[] values)
    {
        float? min = null;
        float? max = null;
        int nodata = 0;
        for (int i = 0; i < values.Length; i++)
        {
            var d = values[i].Depth;
            if (d == FillValue) { nodata++; continue; }
            if (min is null || d < min) min = d;
            if (max is null || d > max) max = d;
        }
        return (min, max, nodata);
    }
}
