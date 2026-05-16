namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Placeholder describer for S-101 Electronic Navigational Charts.
/// </summary>
/// <remarks>
/// S-101 datasets are surfaced by <c>list_datasets</c> via
/// <see cref="EncDotNet.S100.Mcp.Tools.Catalog.S101DatasetData"/>, but a
/// full ISO 8211 feature-record describe is its own work item. Until
/// that lands this describer returns
/// <see cref="SpecNotSupportedForTool"/> so callers receive the stable
/// "spec_not_supported_for_tool" error rather than a confusing
/// "dataset spec not registered" indirection. S-101 feature describe
/// support is tracked as a follow-up to PR MCP-3.
/// </remarks>
internal sealed class S101FeatureDescriber : ISpecFeatureDescriber
{
    public string SpecName => "S-101";

    public ToolResult<DescribeFeatureResult> Describe(FeatureDescriberContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ToolResult<DescribeFeatureResult>.Err(
            new SpecNotSupportedForTool(context.Dataset.Spec, DescribeFeatureTool.Name));
    }
}
