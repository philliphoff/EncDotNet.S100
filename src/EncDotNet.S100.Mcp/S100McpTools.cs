using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
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
    /// Where <c>get_navigability_grid</c> writes its grid files: a folder in
    /// the system's temporary directory.
    /// </summary>
    public static string NavigabilityGridDirectory { get; } = Path.Combine(Path.GetTempPath(), "s100-mcp", "navigability-grids");

    /// <summary>The resource URI template <c>get_navigability_grid</c>'s files are served at (#1615).</summary>
    public const string NavigabilityGridUriTemplate = "s100://grids/{id}";

    /// <summary>The MIME type of a navigability grid file.</summary>
    public const string NavigabilityGridMimeType = "application/octet-stream";

    /// <summary>
    /// The grid files <c>get_navigability_grid</c> writes, in
    /// <see cref="NavigabilityGridDirectory"/>: served as
    /// <c>s100://grids/{id}</c> resources, and kept for
    /// <see cref="ResultFileStore.DefaultMaxAge"/>, up to the
    /// <see cref="ResultFileStore.DefaultMaxFiles"/> newest.
    /// </summary>
    public static ResultFileStore NavigabilityGrids { get; } =
        new(NavigabilityGridDirectory, "navigability", NavigabilityGridUriTemplate.Replace("{id}", string.Empty, StringComparison.Ordinal));

    /// <summary>
    /// Creates the built-in read-only tool set bound to <paramref name="catalog"/>.
    /// </summary>
    /// <param name="catalog">The dataset catalog the tools read from.</param>
    /// <param name="passageRoutes">
    /// The host's routes for <c>check_route</c>, or <c>null</c> when the host
    /// has none (the tool then takes a polyline only).
    /// </param>
    /// <param name="routeCheckSources">
    /// The route-check sources shared with the host's own checks, or
    /// <c>null</c> for the tool's own cache over ProjNet. Their
    /// <see cref="RouteCheckSourceCache.TidePredictions"/> also fill S-104 gaps
    /// in <c>sample</c>.
    /// </param>
    /// <param name="mapDrawState">
    /// What the host's map draws, so <c>identify_features</c> with no time
    /// matches it, or <c>null</c> when the host has no map.
    /// </param>
    /// <returns>
    /// The tools in a stable order. Tool names are the <c>Name</c> constants on
    /// the corresponding <c>EncDotNet.S100.Mcp.Tools</c> types (e.g.
    /// <c>list_datasets</c>, <c>describe_feature</c>, <c>sample</c>);
    /// host-supplied tools must not collide with them.
    /// </returns>
    public static IReadOnlyList<McpServerTool> Create(IDatasetCatalog catalog, IPassageRouteProvider? passageRoutes = null, RouteCheckSourceCache? routeCheckSources = null, IMapDrawState? mapDrawState = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var transforms = new ProjNetCrsTransformFactory();
        routeCheckSources ??= new RouteCheckSourceCache(transforms);
        var checkRoute = new CheckRouteTool(catalog, passageRoutes, routeCheckSources, TimeProvider.System);

        return S100McpServerToolFactory.CreateTools(
            new ListDatasetsTool(catalog),
            new DescribeFeatureTool(catalog),
            new DescribeFeatureTypeTool(),
            new SampleTool(catalog, transforms, routeCheckSources, TimeProvider.System),
            mapDrawState is null ? new IdentifyFeaturesTool(catalog) : new IdentifyFeaturesTool(catalog, mapDrawState),
            new NearestFeaturesTool(catalog),
            new QueryFeaturesTool(catalog),
            new SearchFeaturesTool(catalog),
            new ListSpecsTool(catalog, EncDotNet.S100.Datasets.Pipelines.ProductRegistry.Default),
            new ListTimeStepsTool(catalog),
            checkRoute,
            new PlanRouteTool(catalog, routeCheckSources, checkRoute, passageRoutes as IPassageRouteWriter))
            .Append(McpToolsets.Assign(McpToolsets.Routes, NavigabilityGridMcpTool.Create(new NavigabilityGridTool(catalog, routeCheckSources, NavigabilityGrids))))
            .ToList();
    }

    /// <summary>
    /// The resources a host serves beside <paramref name="tools"/>: the
    /// <c>s100://grids/{id}</c> template, when <c>get_navigability_grid</c> is
    /// among them, so a client that can't read the server's files (HTTP from
    /// another machine) fetches a grid by the <c>resource_link</c> it was given.
    /// </summary>
    /// <param name="tools">The tools the host serves, after toolset selection.</param>
    /// <returns>The resources; empty when no served tool writes any.</returns>
    public static IReadOnlyList<McpServerResource> CreateResources(IEnumerable<McpServerTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return tools.Any(t => t.ProtocolTool.Name == NavigabilityGridTool.Name)
            ? [CreateGridResource(NavigabilityGrids)]
            : [];
    }

    /// <summary>The <c>s100://grids/{id}</c> resource template over <paramref name="store"/>.</summary>
    /// <param name="store">The grid files.</param>
    /// <returns>The resource.</returns>
    internal static McpServerResource CreateGridResource(ResultFileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var read = async (string id, CancellationToken ct) =>
        {
            if (!store.TryGet(id, out var file))
            {
                throw new McpProtocolException(
                    $"No navigability grid '{id}': it may have expired (grids are kept {store.MaxAge.TotalHours:0} hours, up to the {store.MaxFiles} newest). Call get_navigability_grid again.",
                    McpErrorCode.ResourceNotFound);
            }

            var bytes = await File.ReadAllBytesAsync(file!.Path, ct).ConfigureAwait(false);
            return BlobResourceContents.FromBytes(bytes, file.Uri, NavigabilityGridMimeType);
        };

        return McpServerResource.Create(read, new McpServerResourceCreateOptions
        {
            UriTemplate = store.UriPrefix + "{id}",
            Name = "navigability_grid",
            Title = "Navigability grid",
            Description = "A grid get_navigability_grid wrote (npy or raw, as its 'file' describes). Kept for a day, up to the 50 newest.",
            MimeType = NavigabilityGridMimeType,
        });
    }
}
