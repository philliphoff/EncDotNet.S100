using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>Builds <see cref="CheckRouteTool"/>s for tests: their own sources over ProjNet, the system clock unless given.</summary>
internal static class CheckRouteTools
{
    public static CheckRouteTool Create(IDatasetCatalog catalog, IPassageRouteProvider? routes = null, TimeProvider? time = null) =>
        new(catalog, routes, new RouteCheckSourceCache(new ProjNetCrsTransformFactory()), time ?? TimeProvider.System);
}
