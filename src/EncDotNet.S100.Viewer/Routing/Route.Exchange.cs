using EncDotNet.S100.Viewer.Routing.Exchange;

namespace EncDotNet.S100.Viewer.Routing;

// Copy to edit and re-export (#960, decisions 12 and 17; item F6).
public sealed partial class Route
{
    /// <summary>
    /// The received route an owned copy was made from (its file, route ID and
    /// edition), or <c>null</c>. A copy is exported as a new route that
    /// references it (decision 17).
    /// </summary>
    public RouteSource? Origin { get; internal set; }

    /// <summary>
    /// What the source file holds that the viewer's model can't (action
    /// points, further schedules, extensions…), kept so export can write it
    /// back; <c>null</c> when there's none.
    /// </summary>
    public RoutePreservedContent? Preserved { get; internal set; }

    /// <summary>
    /// Whether preserved schedules no longer line up with the route: they
    /// refer to waypoints by number, and a waypoint was inserted or deleted
    /// since the copy was made (#391 concern 8).
    /// </summary>
    public bool HasStalePreservedSchedules
        => !IsReadOnly && Preserved is { ScheduleCount: > 0 } p && p.WaypointCount != _waypoints.Count;

    /// <summary>
    /// Whether stale preserved schedules are written on export as they are
    /// (the header's "Keep"). Off by default: export drops them and says so.
    /// </summary>
    public bool KeepStalePreservedSchedules { get; private set; }

    /// <summary>Chooses whether stale preserved schedules are kept on export.</summary>
    /// <param name="keep"><c>true</c> to keep and export them as they are.</param>
    public void SetKeepStalePreservedSchedules(bool keep)
    {
        if (KeepStalePreservedSchedules == keep)
            return;
        KeepStalePreservedSchedules = keep;
        OnChanged();
    }

    /// <summary>Drops preserved schedules that no longer match (the header's "Drop them").</summary>
    public void DropStalePreservedSchedules()
    {
        if (!HasStalePreservedSchedules)
            return;
        // A schedule count of 0 tells the format to write none of them back.
        Preserved = Preserved! with { ScheduleCount = 0 };
        KeepStalePreservedSchedules = false;
        OnChanged();
    }

    /// <summary>
    /// Whether export writes the preserved schedules back: they exist, and
    /// they still match or the mariner chose to keep them.
    /// </summary>
    public bool WritesPreservedSchedules
        => Preserved is { ScheduleCount: > 0 } && (!HasStalePreservedSchedules || KeepStalePreservedSchedules);
}
