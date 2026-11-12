using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Builds an <see cref="McpServerHost"/> with none of the viewer's own tools,
/// for tests of the server's lifecycle: start, stop, port and settings.
/// </summary>
internal static class TestMcpServerHost
{
    public static McpServerHost Create(IDatasetCatalog catalog, ViewerSettings settings) =>
        new(
            catalog,
            settings,
            new ViewerPassageRouteProvider(new RoutesService(new ClockViewTime(TimeProvider.System)), new DispatcherRouteEditInvoker()),
            new EncDotNet.S100.Datasets.Pipelines.Navigation.RouteCheckSourceCache(new EncDotNet.S100.Crs.ProjNet.ProjNetCrsTransformFactory()),
            new NothingHidden(),
            toolGroups: []);

    private sealed class NothingHidden : IMapDrawState
    {
        public DateTimeOffset? Time => null;
        public IReadOnlySet<string>? HiddenFeatures(LoadedDataset dataset) => null;
    }
}
