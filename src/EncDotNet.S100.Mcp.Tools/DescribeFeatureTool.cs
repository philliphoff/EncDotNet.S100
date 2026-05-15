using System.Collections.Immutable;
using System.Text.Json;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Spec;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>Request payload for <see cref="DescribeFeatureTool"/>.</summary>
public sealed record DescribeFeatureRequest(DatasetId DatasetId, string FeatureId);

/// <summary>Result of <see cref="DescribeFeatureTool"/>.</summary>
/// <param name="Spec">The spec the feature was parsed from.</param>
/// <param name="FeatureTypeName">The spec-defined feature type code (e.g. <c>"NavwarnPart"</c>).</param>
/// <param name="Attributes">Serialised attribute payload as JSON.</param>
/// <param name="References">xlink-style cross references resolved against the catalog snapshot.</param>
public sealed record DescribeFeatureResult(
    Core.SpecRef Spec,
    string FeatureTypeName,
    JsonElement Attributes,
    ImmutableArray<FeatureReference> References);

/// <summary>
/// A cross-reference from one feature to another, projected from the
/// underlying <c>xlink:href</c> attribute.
/// </summary>
/// <param name="Role">The association role name (the local element name carrying <c>xlink:href</c>).</param>
/// <param name="Href">The raw <c>xlink:href</c> value.</param>
/// <param name="TargetDatasetId">The dataset containing the target, if it could be resolved within the catalog snapshot.</param>
/// <param name="TargetFeatureId">The target feature ID, if extractable from the href.</param>
/// <param name="Resolved"><c>true</c> when both target dataset and feature ID were located in the snapshot.</param>
public sealed record FeatureReference(
    string Role,
    string Href,
    DatasetId? TargetDatasetId,
    string? TargetFeatureId,
    bool Resolved);

/// <summary>
/// Describes a single feature in a loaded dataset. Dispatches to a
/// per-spec strategy (<see cref="ISpecFeatureDescriber"/>); specs without
/// an end-to-end implementation in this PR return
/// <see cref="SpecNotSupportedForTool"/>.
/// </summary>
public sealed class DescribeFeatureTool
{
    /// <summary>Tool name used in <see cref="SpecNotSupportedForTool"/> errors.</summary>
    public const string Name = "describe_feature";

    private readonly IDatasetCatalog _catalog;
    private readonly FeatureDescriberRegistry _registry;

    /// <summary>Creates a new <see cref="DescribeFeatureTool"/> with the default registry.</summary>
    public DescribeFeatureTool(IDatasetCatalog catalog)
        : this(catalog, FeatureDescriberRegistry.Default)
    {
    }

    /// <summary>Creates a new <see cref="DescribeFeatureTool"/> with a custom registry.</summary>
    public DescribeFeatureTool(IDatasetCatalog catalog, FeatureDescriberRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(registry);
        _catalog = catalog;
        _registry = registry;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<DescribeFeatureResult>> InvokeAsync(
        DescribeFeatureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = _catalog.Datasets;
        LoadedDataset? target = null;
        foreach (var dataset in snapshot)
        {
            if (dataset.Id == request.DatasetId)
            {
                target = dataset;
                break;
            }
        }

        if (target is null)
        {
            return Task.FromResult(
                ToolResult<DescribeFeatureResult>.Err(
                    new DatasetNotFound(request.DatasetId)));
        }

        var describer = _registry.Get(target.Spec.Name);
        if (describer is null)
        {
            return Task.FromResult(
                ToolResult<DescribeFeatureResult>.Err(
                    new SpecNotSupportedForTool(target.Spec, Name)));
        }

        var context = new FeatureDescriberContext(target, request.FeatureId, snapshot);
        return Task.FromResult(describer.Describe(context));
    }
}
