namespace EncDotNet.S100.Datasets.S129.Fusion.Routing;

/// <summary>
/// The kind of mapping <see cref="S129RouteBinder"/> produced for a
/// particular control point.
/// </summary>
public enum S129RouteMappingKind
{
    /// <summary>The control point snapped to a specific S-421 waypoint.</summary>
    OnWaypoint,

    /// <summary>The control point fell along a specific S-421 leg.</summary>
    OnLeg,

    /// <summary>
    /// The control point had no S-421 waypoint or leg within the
    /// configured tolerances.
    /// </summary>
    Unmapped,
}
