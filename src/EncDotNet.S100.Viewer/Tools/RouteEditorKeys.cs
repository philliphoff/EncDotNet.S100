using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// The route editor's keyboard shortcuts. Ctrl+E (⌘E) toggles edit mode,
/// Ctrl+Z (⌘Z) undoes and Ctrl+Shift+Z (⇧⌘Z) redoes an edit to the active
/// route, and Delete removes the selected waypoint while editing.
/// </summary>
/// <remarks>
/// <para>
/// None of the shortcuts fire while a text field has focus, so typing (an E,
/// or the text box's own undo) is never taken over, and there is no bare-letter
/// shortcut. Undo, redo and Delete act only from the map (or with nothing
/// focused) and the Routes panel, so they don't reach into another panel's
/// list; Delete also needs edit mode.
/// </para>
/// <para>
/// Esc leaves edit mode through the window's existing Esc binding, which
/// exits whichever map tool is active.
/// </para>
/// </remarks>
internal static class RouteEditorKeys
{
    /// <summary>Where keyboard focus is, as far as the route shortcuts care.</summary>
    internal enum KeyScope
    {
        /// <summary>A text field: no route shortcut fires.</summary>
        TextInput,

        /// <summary>The Routes panel.</summary>
        RoutesPanel,

        /// <summary>The map, or nothing in particular.</summary>
        Map,

        /// <summary>Another panel or control.</summary>
        Other,
    }

    /// <summary>
    /// Handles the route shortcuts for key presses anywhere under
    /// <paramref name="root"/>, ahead of the focused control.
    /// </summary>
    /// <param name="root">The window (or other root) to listen on.</param>
    /// <param name="routes">The route store the shortcuts act on.</param>
    /// <param name="isMap">Whether a focused visual belongs to the map; focus
    /// there counts as <see cref="KeyScope.Map"/>.</param>
    public static void Attach(InputElement root, RoutesService routes, Func<Visual, bool>? isMap = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(routes);

        root.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Handled)
                    return;
                var focused = TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() as Visual;
                if (Handle(routes, e.Key, e.KeyModifiers, ScopeOf(focused, root, isMap)))
                    e.Handled = true;
            },
            RoutingStrategies.Tunnel);
    }

    /// <summary>Whether a text field in <paramref name="topLevel"/> has keyboard focus.</summary>
    public static bool IsTextInputFocused(TopLevel topLevel)
        => ScopeOf(topLevel.FocusManager?.GetFocusedElement() as Visual, topLevel, null) == KeyScope.TextInput;

    /// <summary>Classifies the focused visual.</summary>
    internal static KeyScope ScopeOf(Visual? focused, Visual root, Func<Visual, bool>? isMap)
    {
        if (focused is null || ReferenceEquals(focused, root) || focused is TopLevel)
            return KeyScope.Map;

        for (var visual = focused; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is TextBox)
                return KeyScope.TextInput;
            if (visual is RoutesView)
                return KeyScope.RoutesPanel;
            if (isMap?.Invoke(visual) == true)
                return KeyScope.Map;
        }
        return KeyScope.Other;
    }

    /// <summary>
    /// Applies the shortcut for <paramref name="key"/> with
    /// <paramref name="modifiers"/>, if any applies in <paramref name="scope"/>.
    /// </summary>
    /// <returns><c>true</c> when the key was a route shortcut and was handled.</returns>
    internal static bool Handle(RoutesService routes, Key key, KeyModifiers modifiers, KeyScope scope)
    {
        if (scope == KeyScope.TextInput)
            return false;

        var command = (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) is KeyModifiers.Control or KeyModifiers.Meta;
        var shift = (modifiers & KeyModifiers.Shift) != 0;
        var alt = (modifiers & KeyModifiers.Alt) != 0;
        if (alt)
            return false;

        if (key == Key.E && command && !shift)
        {
            routes.IsEditModeActive = !routes.IsEditModeActive;
            return true;
        }

        if (scope is not (KeyScope.RoutesPanel or KeyScope.Map))
            return false;

        if (key == Key.Z && command)
        {
            if (shift)
                routes.RedoActiveRoute();
            else
                routes.UndoActiveRoute();
            return true;
        }

        if (key == Key.Delete && modifiers == KeyModifiers.None && routes.IsEditModeActive)
            return routes.DeleteSelectedWaypoint();

        return false;
    }
}
