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
    /// or the index would fall out of range. Selecting a waypoint clears
    /// <see cref="SelectedLegIndex"/>: one item is selected at a time.
    /// </summary>
    public int? SelectedWaypointIndex
    {
        get => _selectedWaypointIndex;
        set
        {
            var normalized = Normalize(value, waypoint: true);
            var leg = normalized is null ? _selectedLegIndex : null;
            if (_selectedWaypointIndex == normalized && _selectedLegIndex == leg)
                return;
            _selectedWaypointIndex = normalized;
            _selectedLegIndex = leg;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int? _selectedLegIndex;

    /// <summary>
    /// Index of the selected leg within <see cref="RouteCollection.ActiveRoute"/>,
    /// or <c>null</c>. The map gives the selected leg a halo and always shows
    /// its label. Selecting a leg clears <see cref="SelectedWaypointIndex"/>.
    /// </summary>
    public int? SelectedLegIndex
    {
        get => _selectedLegIndex;
        set
        {
            var normalized = Normalize(value, waypoint: false);
            var waypoint = normalized is null ? _selectedWaypointIndex : null;
            if (_selectedLegIndex == normalized && _selectedWaypointIndex == waypoint)
                return;
            _selectedLegIndex = normalized;
            _selectedWaypointIndex = waypoint;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _isEditModeActive;

    /// <summary>
    /// Whether the interactive route editor is on. The single source of truth
    /// for edit mode: the map's route tool, the Routes panel's Edit toggle,
    /// the Ctrl+E (⌘E) shortcut and the MCP <c>set_route_edit_mode</c> tool all
    /// read and set it, and the host activates or deactivates the route edit
    /// tool to match.
    /// </summary>
    public bool IsEditModeActive
    {
        get => _isEditModeActive;
        set
        {
            if (_isEditModeActive == value)
                return;
            _isEditModeActive = value;
            EditModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _isPassageCardExpanded;
    private Route? _passageCardRoute;

    /// <summary>
    /// Whether the Routes panel's Passage card is expanded. When the active
    /// route changes it resets: collapsed for a route with no passage values
    /// (a new route), expanded once any are set. The MCP
    /// <c>set_passage_card</c> tool sets it too.
    /// </summary>
    public bool IsPassageCardExpanded
    {
        get => _isPassageCardExpanded;
        set
        {
            if (_isPassageCardExpanded == value)
                return;
            _isPassageCardExpanded = value;
            PassageCardChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when <see cref="IsPassageCardExpanded"/> changes.</summary>
    public event EventHandler? PassageCardChanged;

    /// <summary>Raised when <see cref="IsEditModeActive"/> changes.</summary>
    public event EventHandler? EditModeChanged;

    /// <summary>
    /// Undoes the most recent edit to the active route.
    /// </summary>
    /// <returns><c>true</c> when an edit was undone.</returns>
    public bool UndoActiveRoute() => Routes.ActiveRoute?.History.Undo() ?? false;

    /// <summary>
    /// Redoes the most recently undone edit to the active route.
    /// </summary>
    /// <returns><c>true</c> when an edit was redone.</returns>
    public bool RedoActiveRoute() => Routes.ActiveRoute?.History.Redo() ?? false;

    /// <summary>
    /// Deletes the selected waypoint of the active route as one undoable edit
    /// and clears the selection.
    /// </summary>
    /// <returns><c>true</c> when a waypoint was deleted.</returns>
    public bool DeleteSelectedWaypoint()
    {
        if (Routes.ActiveRoute is not { } route || SelectedWaypointIndex is not { } index)
            return false;
        route.History.Execute(RouteEditLabels.DeleteWaypoint, () => route.RemoveWaypoint(index));
        SelectedWaypointIndex = null;
        return true;
    }

    /// <summary>
    /// Raised when the collection changes (routes added/removed, active
    /// route changed, or any member route edited) or when the selection
    /// changes. Subscribers typically rebuild the overlay and refresh the
    /// side panel.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Raised when <see cref="SelectedWaypointIndex"/> or <see cref="SelectedLegIndex"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    public RoutesService()
    {
        Routes.Changed += (_, _) =>
        {
            // Keep the selection valid as routes/waypoints change.
            var waypoint = Normalize(_selectedWaypointIndex, waypoint: true);
            var leg = Normalize(_selectedLegIndex, waypoint: false);
            if (waypoint != _selectedWaypointIndex || leg != _selectedLegIndex)
            {
                _selectedWaypointIndex = waypoint;
                _selectedLegIndex = leg;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            if (!ReferenceEquals(_passageCardRoute, Routes.ActiveRoute))
            {
                _passageCardRoute = Routes.ActiveRoute;
                IsPassageCardExpanded = _passageCardRoute is { Passage.IsEmpty: false };
            }
            Changed?.Invoke(this, EventArgs.Empty);
        };

        SelectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    private int? Normalize(int? candidate, bool waypoint)
    {
        if (candidate is not { } index || index < 0)
            return null;
        var active = Routes.ActiveRoute;
        var count = waypoint ? active?.Waypoints.Count : active?.Legs.Count;
        if (count is null || index >= count)
            return null;
        return index;
    }
}
