using Mapsui;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Opens a navigational warning part (#1397): its Warning card in Object
/// Information, the map framed on it when asked, and the warning marked seen
/// (opening the card is reading it). The Navigational warnings panel's rows,
/// MCP <c>select_navigational_warning</c>, Warning card links and the route
/// findings all open warnings through the panel, which runs this.
/// </summary>
internal sealed class OpenNavigationalWarningWorkflow
{
    /// <summary>Half the side of the box a point warning frames, in EPSG:3857 metres (about 1 NM).</summary>
    internal const double PointFrameHalfMetres = 1852.0;

    private readonly IPickService _pick;
    private readonly IDatasetLoaderService _loader;
    private readonly ICapabilityAccessor<IMapViewportController> _viewport;
    private readonly NavigationalWarningsService _warnings;

    /// <summary>Creates the workflow.</summary>
    /// <param name="pick">Opens a feature in Object Information.</param>
    /// <param name="loader">The loaded datasets' processors.</param>
    /// <param name="viewport">The map, which is framed on the part.</param>
    /// <param name="warnings">Marks warnings seen.</param>
    public OpenNavigationalWarningWorkflow(
        IPickService pick,
        IDatasetLoaderService loader,
        ICapabilityAccessor<IMapViewportController> viewport,
        NavigationalWarningsService warnings)
    {
        ArgumentNullException.ThrowIfNull(pick);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(warnings);
        _pick = pick;
        _loader = loader;
        _viewport = viewport;
        _warnings = warnings;
    }

    /// <summary>
    /// Opens <paramref name="entry"/>'s Warning card in Object Information,
    /// frames the map on it when <paramref name="frame"/> is true and it has
    /// a location, and marks its warning seen.
    /// </summary>
    /// <param name="entry">The part to open.</param>
    /// <param name="frame">Whether to frame the map on it (the panel doesn't for a part that isn't in force).</param>
    /// <returns>What happened.</returns>
    public OpenNavigationalWarningOutcome Open(NavigationalWarningEntry entry, bool frame)
    {
        ArgumentNullException.ThrowIfNull(entry);
        bool opened;
        using (var processors = _loader.AcquireProcessors())
        {
            opened = processors.TryGetValue(entry.Dataset, out var processor)
                && _pick.OpenFeatureAt(processor, entry.Ordinal, entry.DatasetId);
        }

        var framed = frame && Frame(entry);
        _warnings.MarkSeen(entry);
        return new OpenNavigationalWarningOutcome(opened, framed);
    }

    /// <summary>
    /// Centres the map on <paramref name="entry"/> at a scale that fits its
    /// geometry, without opening its card.
    /// </summary>
    /// <param name="entry">The part to frame.</param>
    /// <returns>Whether the part has a location to frame.</returns>
    public bool Frame(NavigationalWarningEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (FrameExtent(entry) is not { } extent)
            return false;
        _viewport.Current?.ZoomToExtent(extent);
        return true;
    }

    /// <summary>
    /// The EPSG:3857 extent opening a part frames: its bounding box, never
    /// smaller than about 2 NM across, so a point opens at a usable scale.
    /// </summary>
    internal static MRect? FrameExtent(NavigationalWarningEntry entry)
    {
        if (entry.Extent is not { } box)
            return null;
        var halfW = Math.Max(box.Width / 2, PointFrameHalfMetres);
        var halfH = Math.Max(box.Height / 2, PointFrameHalfMetres);
        return new MRect(box.Centroid.X - halfW, box.Centroid.Y - halfH, box.Centroid.X + halfW, box.Centroid.Y + halfH);
    }
}

/// <summary>What opening a warning part did.</summary>
/// <param name="Opened">Whether the part was found in its dataset and its card opened.</param>
/// <param name="Framed">Whether the map was framed on it.</param>
internal sealed record OpenNavigationalWarningOutcome(bool Opened, bool Framed);
