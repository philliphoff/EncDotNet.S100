using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// The measure tool's keys (#1130, D20): <c>G</c> switches the geometry,
/// <c>Delete</c> clears the path and Ctrl+C (⌘C) copies the measurement.
/// Enter, Backspace and Esc go through the window's tool bindings.
/// </summary>
/// <remarks>
/// They act only while Measure is on and the map or the card's buttons have
/// focus; in a text field they edit the text.
/// </remarks>
internal static class MeasureKeys
{
    /// <summary>Handles the measure keys under <paramref name="root"/>, ahead of the focused control.</summary>
    /// <param name="root">The window to listen on.</param>
    /// <param name="session">The measure state the keys act on.</param>
    /// <param name="card">The card whose copy Ctrl+C runs.</param>
    /// <param name="isMap">Whether a focused visual belongs to the map (the card is on the map).</param>
    public static void Attach(InputElement root, MeasureSession session, MeasureCardViewModel card, Func<Visual, bool>? isMap = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(card);

        root.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Handled)
                    return;
                var focused = TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() as Visual;
                if (Handle(session, card, e.Key, e.KeyModifiers, RouteEditorKeys.ScopeOf(focused, root, isMap)))
                    e.Handled = true;
            },
            RoutingStrategies.Tunnel);
    }

    /// <summary>Applies the key, if it's a measure key in <paramref name="scope"/>.</summary>
    /// <returns><c>true</c> when the key was handled.</returns>
    internal static bool Handle(MeasureSession session, MeasureCardViewModel card, Key key, KeyModifiers modifiers, RouteEditorKeys.KeyScope scope)
    {
        if (!session.IsActive || scope != RouteEditorKeys.KeyScope.Map)
            return false;

        if (key == Key.G && modifiers == KeyModifiers.None)
        {
            session.ToggleGeometry();
            return true;
        }

        if (key == Key.Delete && modifiers == KeyModifiers.None)
        {
            if (session.State.Discard())
                session.NotifyChanged();
            return true;
        }

        if (key == Key.C && modifiers is KeyModifiers.Control or KeyModifiers.Meta && card.CanCopy)
        {
            card.CopyCommand.Execute(null);
            return true;
        }

        return false;
    }
}
