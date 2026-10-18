namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// The labels <see cref="RouteEditHistory"/> steps are recorded under. They
/// name the edit for agents (the MCP undo tools report them) and tests; they
/// are identifiers, not UI text.
/// </summary>
public static class RouteEditLabels
{
    /// <summary>A waypoint appended to the end of the route.</summary>
    public const string AppendWaypoint = "append waypoint";

    /// <summary>A waypoint inserted into the route.</summary>
    public const string InsertWaypoint = "insert waypoint";

    /// <summary>A waypoint moved, by drag or by typed coordinates.</summary>
    public const string MoveWaypoint = "move waypoint";

    /// <summary>A waypoint deleted.</summary>
    public const string DeleteWaypoint = "delete waypoint";

    /// <summary>A waypoint renamed or given other metadata.</summary>
    public const string EditWaypoint = "edit waypoint";

    /// <summary>The route reversed.</summary>
    public const string Reverse = "reverse route";

    /// <summary>The route renamed.</summary>
    public const string Rename = "rename route";

    /// <summary>A leg's geometry type or attributes changed.</summary>
    public const string EditLeg = "edit leg";

    /// <summary>The route's passage values (draft, speed, departure, …) changed.</summary>
    public const string EditPassage = "edit passage";

    /// <summary>The route's metadata or vessel changed.</summary>
    public const string EditRouteInfo = "edit route info";

    /// <summary>Copy to edit: making an owned copy of a received route (#960).</summary>
    public const string CopyRoute = "copy route";
}
