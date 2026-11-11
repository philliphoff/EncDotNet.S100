namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Which dock surface an <see cref="IActivityTab"/> belongs to. PR-M4
/// unifies the viewer's three side-panel docks (left activity pane, right
/// pick-style pane, bottom timeline-style pane) under the same registry
/// so each dock can carry one or more tabs.
/// </summary>
internal enum TabDock
{
    /// <summary>The main activity pane on the left of the window (Datasets, Search, Settings, …).</summary>
    Left,

    /// <summary>The right-hand companion pane (currently Pick Report).</summary>
    Right,

    /// <summary>The bottom strip (currently Timeline).</summary>
    Bottom,
}
