using EncDotNet.S100.Mcp.Tides;
using EncDotNet.S100.Mcp.Tools.Tides;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The read-only tide prediction tools, list_tide_stations and predict_tide (#1452).</summary>
internal sealed class TidesMcpToolGroup : IViewerMcpToolGroup
{
    private readonly ITideCatalogueSource _catalogue;
    private readonly TimeProvider _time;

    public TidesMcpToolGroup(ITideCatalogueSource catalogue, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(time);
        _catalogue = catalogue;
        _time = time;
    }

    /// <summary>The catalogue source for the viewer's data paths.</summary>
    public static TideCatalogueFileSource FileSource(ViewerDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var path = paths.TideStationCatalogueFile;
        return new TideCatalogueFileSource(
            path,
            $"SoundCharts reads the station catalogue from {path}. Download NOAA's stations there with 's100 tides update --catalogue \"{path}\"' (about 8 minutes).");
    }

    public IEnumerable<McpServerTool> CreateTools() => TideMcpTools.Create(_catalogue, _time);
}
