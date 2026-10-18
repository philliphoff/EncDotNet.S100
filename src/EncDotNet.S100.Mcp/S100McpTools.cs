using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// Builds the read-only S-100 MCP tool set from a dataset catalog,
/// independent of transport.
/// </summary>
/// <remarks>
/// The tools returned here are transport-agnostic <see cref="McpServerTool"/>
/// instances. They are shared by <see cref="S100McpServer"/> (Streamable
/// HTTP, hosted by the viewer or a service) and <see cref="S100McpStdioHost"/>
/// (stdio, hosted by the <c>s100</c> CLI when an agent spawns it directly).
/// Every tool is read-only — it answers questions about the supplied catalog
/// and never mutates host state.
/// </remarks>
public static class S100McpTools
{
    /// <summary>
    /// Creates the built-in read-only tool set bound to <paramref name="catalog"/>.
    /// </summary>
    /// <param name="catalog">The dataset catalog the tools read from.</param>
    /// <param name="passageRoutes">
    /// The host's routes for <c>get_passage_result</c> and <c>check_route</c>,
    /// or <c>null</c> when the host has none (the tools then take a polyline only).
    /// </param>
    /// <returns>
    /// The tools in a stable order. Tool names are the <c>Name</c> constants on
    /// the corresponding <c>EncDotNet.S100.Mcp.Tools</c> types (e.g.
    /// <c>list_datasets</c>, <c>describe_feature</c>, <c>sample_coverage</c>);
    /// host-supplied tools must not collide with them.
    /// </returns>
    public static IReadOnlyList<McpServerTool> Create(IDatasetCatalog catalog, IPassageRouteProvider? passageRoutes = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return S100McpServerToolFactory.CreateTools(
            new ListDatasetsTool(catalog),
            new DescribeFeatureTool(catalog),
            new DescribeFeatureTypeTool(),
            new SampleCoverageTool(catalog),
            new FindAtTool(catalog),
            new IdentifyFeaturesTool(catalog),
            new NearestFeaturesTool(catalog),
            new QueryFeaturesTool(catalog),
            new CountFeaturesTool(catalog),
            new SearchFeaturesTool(catalog),
            new SampleCoverageAlongTool(catalog),
            new ListSpecsTool(catalog),
            new ListTimeStepsTool(catalog),
            new GetPassageResultTool(catalog, passageRoutes),
            new CheckRouteTool(catalog, passageRoutes))
            .ToList();
    }
}
