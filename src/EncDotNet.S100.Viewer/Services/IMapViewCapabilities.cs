using EncDotNet.S100.Rendering.Engine.Prediction;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What <see cref="MapViews"/> needs from one map: its id, the capabilities
/// the window-level accessors hand out while it's the active map, its own
/// viewport, and its demand planner.
/// </summary>
internal interface IMapViewCapabilities
{
    /// <summary>The map's stable id.</summary>
    MapViewId Id { get; }

    /// <summary>Moves and reads this map's viewport.</summary>
    IMapViewportController ViewportController { get; }

    /// <summary>Converts between this map's screen and map coordinates.</summary>
    IMapCoordinateConverter CoordinateConverter { get; }

    /// <summary>Renders this map to an image.</summary>
    IImageRenderer ImageRenderer { get; }

    /// <summary>This map's own viewport.</summary>
    IMapViewportNotifier Viewport { get; }

    /// <summary>This map's demand planner.</summary>
    DemandPlanner Planner { get; }

    /// <summary>This map's dataset session, which the dataset loader drives with every other map's.</summary>
    IMapDatasetSession DatasetSession { get; }
}
