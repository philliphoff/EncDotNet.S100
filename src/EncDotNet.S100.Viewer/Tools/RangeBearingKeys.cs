using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// The Range &amp; bearing tool's bare keys (#1130 D20): <c>G</c> switches the
/// geometry, <c>Delete</c> clears the origin and <c>Cmd/Ctrl+C</c> copies the
/// result, while the tool is on and the map or the card's buttons have focus.
/// In a text field they edit the text. Cmd/Ctrl+R, Enter, Backspace and Esc
/// go through the window's key bindings like the other map tools'.
/// </summary>
internal static class RangeBearingKeys
{
    /// <summary>Handles the keys for presses anywhere under <paramref name="root"/>, ahead of the focused control.</summary>
    /// <param name="root">The window.</param>
    /// <param name="state">The tool's model.</param>
    /// <param name="copy">Puts the result on the clipboard.</param>
    /// <param name="isMap">Whether a focused visual belongs to the map.</param>
    public static void Attach(InputElement root, RangeBearingState state, Action copy, Func<Visual, bool> isMap)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(isMap);

        root.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Handled || !state.IsToolActive)
                    return;
                var focused = TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() as Visual;
                if (!AppliesTo(focused, root, isMap))
                    return;
                if (Handle(state, e.Key, e.KeyModifiers, copy))
                    e.Handled = true;
            },
            RoutingStrategies.Tunnel);
    }

    /// <summary>Whether the keys apply with <paramref name="focused"/> focused: the map, nothing, or the card outside its text fields.</summary>
    internal static bool AppliesTo(Visual? focused, Visual root, Func<Visual, bool> isMap)
    {
        if (focused is null || ReferenceEquals(focused, root) || focused is TopLevel)
            return true;
        for (var visual = focused; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is TextBox)
                return false;
            if (visual is RangeBearingCardView || isMap(visual))
                return true;
        }

        return false;
    }

    /// <summary>Applies the key, if it's one of the tool's.</summary>
    /// <returns><c>true</c> when it was handled.</returns>
    internal static bool Handle(RangeBearingState state, Key key, KeyModifiers modifiers, Action copy)
    {
        var command = (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) is KeyModifiers.Control or KeyModifiers.Meta;
        if (key == Key.G && modifiers == KeyModifiers.None)
        {
            state.Geometry = state.Geometry == LegGeometry.Rhumb ? LegGeometry.GreatCircle : LegGeometry.Rhumb;
            return true;
        }

        if (key == Key.Delete && modifiers == KeyModifiers.None)
            return state.Discard();

        if (key == Key.C && command && (modifiers & (KeyModifiers.Shift | KeyModifiers.Alt)) == 0 && state.Origin is not null)
        {
            copy();
            return true;
        }

        return false;
    }
}
