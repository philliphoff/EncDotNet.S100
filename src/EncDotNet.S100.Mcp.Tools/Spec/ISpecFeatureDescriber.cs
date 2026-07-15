using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Context handed to an <see cref="ISpecFeatureDescriber"/>.
/// </summary>
/// <param name="Dataset">The dataset being inspected.</param>
/// <param name="FeatureId">The feature ID requested by the caller.</param>
/// <param name="Snapshot">
/// The catalog snapshot captured at tool entry; describers use it to
/// resolve <c>xlink:href</c> references against currently-loaded
/// datasets.
/// </param>
/// <param name="Units">
/// Resolver for attribute units of measure, used to annotate numeric
/// attribute values (e.g. S-101 depths) with their Feature Catalogue
/// unit. May be <see langword="null"/> when units are not required.
/// </param>
internal sealed record FeatureDescriberContext(
    LoadedDataset Dataset,
    string FeatureId,
    IReadOnlyList<LoadedDataset> Snapshot,
    AttributeUnitResolver? Units = null);

/// <summary>Per-spec describer strategy.</summary>
internal interface ISpecFeatureDescriber
{
    /// <summary>Spec name (canonical "S-NNN") this describer handles.</summary>
    string SpecName { get; }

    /// <summary>Builds a <see cref="DescribeFeatureResult"/> or returns a typed error.</summary>
    ToolResult<DescribeFeatureResult> Describe(FeatureDescriberContext context);
}
