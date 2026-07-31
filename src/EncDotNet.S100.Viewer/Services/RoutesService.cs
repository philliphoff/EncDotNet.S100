using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Application-scoped owner of the editable <see cref="RouteCollection"/>
/// plus the transient editor selection (which waypoint of the active route
/// is currently highlighted). A single source of truth shared by the route
/// overlay renderer, the <c>RouteEditTool</c>, the routes side panel, and
/// the agent-facing MCP route tools.
/// </summary>
/// <remarks>
/// Routes are persistent: unlike the Measure Mode overlay, which only
/// exists while its tool is active, the route overlay reflects this
/// collection at all times. The service re-raises
/// <see cref="RouteCollection.Changed"/> and adds its own
/// <see cref="SelectionChanged"/> so a host controller can drive a single
/// redraw path.
/// </remarks>
internal sealed class RoutesService
{
    /// <summary>The editable route collection. Never <c>null</c>.</summary>
    public RouteCollection Routes { get; } = new();

    private int? _selectedWaypointIndex;

    /// <summary>
    /// Index of the highlighted waypoint within
    /// <see cref="RouteCollection.ActiveRoute"/>, or <c>null</c> when none
    /// is selected. Cleared automatically whenever the active route changes
    /// or the index would fall out of range.
    /// </summary>
    public int? SelectedWaypointIndex
    {
        get => _selectedWaypointIndex;
        set
        {
            var normalized = Normalize(value);
            if (_selectedWaypointIndex == normalized)
                return;
            _selectedWaypointIndex = normalized;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Raised when the collection changes (routes added/removed, active
    /// route changed, or any member route edited) or when the selection
    /// changes. Subscribers typically rebuild the overlay and refresh the
    /// side panel.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Raised when <see cref="SelectedWaypointIndex"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    public RoutesService()
    {
        Routes.Changed += (_, _) =>
        {
            // Keep the selection valid as routes/waypoints change.
            var normalized = Normalize(_selectedWaypointIndex);
            if (normalized != _selectedWaypointIndex)
            {
                _selectedWaypointIndex = normalized;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        };

        SelectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    private int? Normalize(int? candidate)
    {
        if (candidate is not { } index || index < 0)
            return null;
        var active = Routes.ActiveRoute;
        if (active is null || index >= active.Waypoints.Count)
            return null;
        return index;
    }
}
