using Avalonia.Input;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// A pluggable map tool (Pick Mode, Measure Mode, future range/bearing,
/// parallel index, etc.). Tools own their own gesture interpretation and
/// any overlay layers they need; the host (<see cref="MapToolController"/>
/// + <c>MapInteractionController</c>) routes pointer/key events to the
/// active tool and respects its <c>true</c>/<c>false</c> "handled"
/// return value to suppress default Mapsui pan/zoom behaviour.
/// </summary>
internal interface IMapTool
{
    /// <summary>
    /// Stable identifier for this tool, e.g. <c>"pick"</c> or
    /// <c>"measure"</c>. Used by the controller for selection and by the UI
    /// layer to derive view-model toggle states.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Cursor to apply while the tool is active, or <c>null</c> to inherit
    /// the default panning cursor.
    /// </summary>
    Cursor? Cursor { get; }

    /// <summary>
    /// Called by the controller when this tool becomes active. The tool
    /// captures <paramref name="context"/> for the rest of its active
    /// lifetime; it must release any resources in <see cref="OnDeactivated"/>.
    /// </summary>
    void OnActivated(MapToolContext context);

    /// <summary>
    /// Called by the controller when this tool is being switched out. The
    /// tool must remove any overlay layers and stop subscribing to map
    /// events.
    /// </summary>
    void OnDeactivated();

    /// <summary>
    /// Routed pointer-pressed (tunnel phase). Return <c>true</c> to mark
    /// the event handled and prevent Mapsui's default pan-from-press.
    /// </summary>
    bool OnPointerPressed(PointerPressedEventArgs e);

    /// <summary>
    /// Routed pointer-moved (tunnel phase). Return <c>true</c> to suppress
    /// default behaviour. Most tools return <c>false</c> here so that the
    /// mouse lat/long readout still updates.
    /// </summary>
    bool OnPointerMoved(PointerEventArgs e);

    /// <summary>
    /// Routed pointer-released (tunnel phase). Return <c>true</c> to
    /// suppress default behaviour. Tools that drive on click typically
    /// finalise the click decision here using a drag-threshold comparison
    /// against the press position they captured in
    /// <see cref="OnPointerPressed"/>.
    /// </summary>
    bool OnPointerReleased(PointerReleasedEventArgs e);

    /// <summary>
    /// Bubble-phase double-tap from Mapsui. Tools return <c>true</c> to
    /// suppress the default zoom-on-double-tap.
    /// </summary>
    bool OnDoubleTapped(TappedEventArgs e);

    /// <summary>
    /// A discrete tool action triggered from a keyboard accelerator
    /// (Enter, Backspace, Delete). The controller invokes this with the
    /// action kind already classified so tools don't need to inspect
    /// modifier state. Return <c>true</c> when the action was consumed.
    /// </summary>
    bool OnAction(MapToolAction action);
}

/// <summary>
/// Discrete tool action triggered from a keyboard accelerator.
/// </summary>
internal enum MapToolAction
{
    /// <summary>Enter / Return — typically "finalise current input".</summary>
    Commit,

    /// <summary>Backspace — typically "remove the most recent input".</summary>
    Backstep,

    /// <summary>Delete — typically "discard the current input entirely".</summary>
    Discard,
}
