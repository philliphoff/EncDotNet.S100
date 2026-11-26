using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What each map of a split needs to know about the others (#1732, #1733):
/// the maps and panes, the map the status bar describes, where the pointer
/// is for the cursor echo, and which maps draw a pick.
/// </summary>
/// <param name="Views">Every map, with their views.</param>
/// <param name="Split">The panes, their numbers and whether the window is split.</param>
/// <param name="StatusFocus">The map the status bar describes.</param>
/// <param name="CursorEcho">Where the pointer is, over which map (D16).</param>
/// <param name="Picks">Which maps draw a picked feature (D10, D11).</param>
internal sealed record MapSplitServices(
    MapViews Views,
    MapSplitViewModel Split,
    MapStatusFocus StatusFocus,
    MapCursorEcho CursorEcho,
    PickPresence Picks);
