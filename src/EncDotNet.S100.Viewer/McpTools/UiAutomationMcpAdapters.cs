using System.ComponentModel;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Wraps the UI-automation tools (<see cref="UiTreeTool"/>, <see cref="UiActionTool"/>)
/// as MCP server tools (#776). Registered only with <c>--mcp-test-hooks</c>.
/// </summary>
internal static class UiAutomationMcpAdapters
{
    private const string Targeting =
        "Target the element by 'id' (its automation id, e.g. 'Datasets.DatasetsTab'; ids follow <View>.<Element>) "
        + "or by 'ref' (e.g. 'e12', from ui_tree; valid while the element stays on screen). An id repeated per row "
        + "(e.g. 'Datasets.Row.Remove') is ambiguous on its own: scope it with 'within' (the row's ref or an ancestor's id), "
        + "or use the ref. Fails with ui_element_not_found (with similar ids), ui_element_ambiguous (with each match's "
        + "ref and row text), ui_element_disabled, or ui_action_not_supported (with the actions the element supports). "
        + "Returns the element afterwards, once bindings and layout have settled. Test-only (--mcp-test-hooks); mutating.";

    /// <summary>Creates <c>ui_tree</c>.</summary>
    public static McpServerTool Create(UiTreeTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("List from this element: an automation id or a ref. Omit for every window and open popup.")] string? root = null,
            [Description("Levels to list below each root, 0–100 (default 30).")] int? depth = null,
            [Description("'interactive' (default) lists elements that have an automation id or can be acted on; the rest are skipped and their children shown in their place. 'all' lists every element.")] string? filter = null,
            [Description("Most elements to return, 1–5000 (default 400); 'truncated' says when the listing was cut.")] int? maxNodes = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new UiTreeRequest(root, depth, filter, maxNodes), ct));
        return Tool(del, UiTreeTool.Name,
            "Lists the viewer's UI as an accessibility client sees it: one root per window and open popup (menus, "
            + "flyouts), each a tree of elements with a ref, automation id, role (button, listItem, tabItem, treeItem, "
            + "edit, checkBox…), name, visible text (for rows and icon buttons without a name), enabled and focus state, "
            + "the actions it supports (invoke, toggle, value, rangeValue, selectionItem, expandCollapse) and their state "
            + "(toggle on/off, value, selected, expanded), and bounds in its window. Use it to find what to act on with "
            + "ui_invoke, ui_set_value, ui_toggle, ui_select, ui_expand, ui_collapse, ui_focus and ui_context_menu. The map "
            + "itself is one element; use the map tools (set_viewport, pick_features…) for chart content. Test-only "
            + "(--mcp-test-hooks); read-only.");
    }

    /// <summary>Creates the tool for <paramref name="inner"/>'s action.</summary>
    public static McpServerTool Create(UiActionTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return inner.Action switch
        {
            UiAction.SetValue => CreateSetValue(inner),
            UiAction.Toggle => CreateToggle(inner),
            _ => Tool(TargetOnly(inner), inner.Name, DescribeTargetOnly(inner.Action) + " " + Targeting),
        };
    }

    private static Delegate TargetOnly(UiActionTool inner) => (
        [Description("The element's automation id (with 'within' when it repeats per row).")] string? id = null,
        [Description("The element's ref from ui_tree, instead of 'id'.")] string? @ref = null,
        [Description("Scope for 'id': a row's ref or an ancestor's automation id.")] string? within = null,
        CancellationToken ct = default) =>
        McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new UiActionRequest(id, @ref, within, null), ct));

    private static McpServerTool CreateSetValue(UiActionTool inner)
    {
        var del = (
            [Description("The text to put in a text box (replacing what is there), or a number for a slider.")] string value,
            [Description("The element's automation id (with 'within' when it repeats per row).")] string? id = null,
            [Description("The element's ref from ui_tree, instead of 'id'.")] string? @ref = null,
            [Description("Scope for 'id': a row's ref or an ancestor's automation id.")] string? within = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new UiActionRequest(id, @ref, within, value), ct));
        return Tool(del, inner.Name,
            "Sets a text box's text (as typing over it does; bindings update, filters and searches run) or a slider's "
            + "or numeric field's value. A box that commits on Enter or on leaving it (e.g. an in-place rename) commits "
            + "when focus moves on: follow with ui_focus on another element. " + Targeting);
    }

    private static McpServerTool CreateToggle(UiActionTool inner)
    {
        var del = (
            [Description("The element's automation id (with 'within' when it repeats per row).")] string? id = null,
            [Description("The element's ref from ui_tree, instead of 'id'.")] string? @ref = null,
            [Description("Scope for 'id': a row's ref or an ancestor's automation id.")] string? within = null,
            [Description("'on' or 'off' to set that state (no-op when already there); omit to flip it.")] string? state = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new UiActionRequest(id, @ref, within, state), ct));
        return Tool(del, inner.Name,
            "Flips a check box, toggle button or checkable menu item, or sets it 'on' or 'off', as a click does. " + Targeting);
    }

    private static string DescribeTargetOnly(UiAction action) => action switch
    {
        UiAction.Invoke =>
            "Clicks a button or menu item, as the user does: its command or click handler runs. For list rows, tabs "
            + "and tree nodes use ui_select; for check boxes ui_toggle.",
        UiAction.Select =>
            "Selects a list row, tab, tree node, radio button or combo box item, as a click on it does.",
        UiAction.Expand =>
            "Expands a tree node, expander, combo box or menu item with a submenu.",
        UiAction.Collapse =>
            "Collapses a tree node, expander, combo box or menu item with a submenu.",
        UiAction.Focus =>
            "Moves keyboard focus to the element, as clicking into it or tabbing to it does. Leaving a text box this "
            + "way runs its lost-focus behaviour (an in-place rename commits).",
        UiAction.ContextMenu =>
            "Opens the element's context menu, as a right-click does; the menu then appears in ui_tree (a 'popup' "
            + "root on the desktop) for ui_invoke.",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private static McpServerTool Tool(Delegate del, string name, string description) =>
        McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = name,
            Description = description,
            SerializerOptions = McpAdapterShared.Options,
        });
}
