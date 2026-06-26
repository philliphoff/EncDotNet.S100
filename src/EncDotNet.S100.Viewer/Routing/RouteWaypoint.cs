using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// A single, mutable waypoint in an editable <see cref="Route"/>. Fields
/// mirror the S-421 <c>RouteWaypoint</c> projection
/// (<see cref="EncDotNet.S100.Datasets.S421.DataModel.S421Waypoint"/>) so a
/// route can be exported to S-421 GML with a near-mechanical mapping.
/// </summary>
/// <remarks>
/// Unlike the immutable S-421 reader projection, this type is mutable: the
/// interactive editor and the agent MCP tools both manipulate waypoints in
/// place. Mutations are routed through <see cref="Route"/> so the owning
/// route can keep its leg list and change notifications consistent.
/// </remarks>
public sealed class RouteWaypoint
{
    /// <summary>
    /// The waypoint's geographic position (WGS-84, decimal degrees).
    /// </summary>
    public GeoPosition Position { get; internal set; }

    /// <summary>
    /// The author-assigned ordinal (S-421 <c>routeWaypointID</c>). Optional;
    /// the route's geometric order is authoritative regardless of this value.
    /// </summary>
    public int? Number { get; set; }

    /// <summary>Human-readable waypoint name (S-421 <c>routeWaypointName</c>).</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Whether the waypoint position is fixed and must not be moved
    /// (S-421 <c>routeWaypointFixed</c>).
    /// </summary>
    public bool? Fixed { get; set; }

    /// <summary>
    /// Planned turn radius at this waypoint, in nautical miles
    /// (S-421 <c>routeWaypointTurnRadius</c>).
    /// </summary>
    public double? TurnRadiusNm { get; set; }
}
