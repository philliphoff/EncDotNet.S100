namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Drives the viewer's UI through Avalonia's automation peers, the way an
/// accessibility client does: lists the elements on screen and acts on one by
/// automation id or by a ref from that listing. Backs the <c>ui_*</c> MCP tools
/// (#776), registered only with <c>--mcp-test-hooks</c>. Every call marshals to
/// the UI thread.
/// </summary>
internal interface IViewerUiAutomation
{
    /// <summary>Lists the element tree of every open window and popup.</summary>
    /// <param name="query">What to list.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The tree, one root per window or popup.</returns>
    /// <exception cref="UiAutomationException">The root target did not resolve.</exception>
    Task<UiTreeSnapshot> GetTreeAsync(UiTreeQuery query, CancellationToken ct = default);

    /// <summary>Performs <paramref name="action"/> on the element <paramref name="target"/> resolves to.</summary>
    /// <param name="target">The element: an automation id or a ref, optionally scoped.</param>
    /// <param name="action">The action.</param>
    /// <param name="value">The value for <see cref="UiAction.SetValue"/>; the wanted state for <see cref="UiAction.Toggle"/> ("on" / "off"), or null to flip it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The element after the action, once layout and bindings have settled.</returns>
    /// <exception cref="UiAutomationException">The target did not resolve, or the element cannot do the action.</exception>
    Task<UiElementSnapshot> ActAsync(UiTarget target, UiAction action, string? value, CancellationToken ct = default);
}

/// <summary>An action on one element, mapped to an automation pattern.</summary>
internal enum UiAction
{
    /// <summary>Click a button or menu item (invoke pattern).</summary>
    Invoke,

    /// <summary>Set a text box's text or a slider's value (value / range value pattern).</summary>
    SetValue,

    /// <summary>Flip a check box or toggle button, or set it on or off (toggle pattern).</summary>
    Toggle,

    /// <summary>Select a list row, tab, tree node or combo box item (selection item pattern).</summary>
    Select,

    /// <summary>Expand a tree node, expander or combo box (expand / collapse pattern).</summary>
    Expand,

    /// <summary>Collapse a tree node, expander or combo box (expand / collapse pattern).</summary>
    Collapse,

    /// <summary>Move keyboard focus to the element.</summary>
    Focus,

    /// <summary>Open the element's context menu, as a right-click does.</summary>
    ContextMenu,
}

/// <summary>
/// Identifies one element: by automation id (unique within <see cref="Within"/>,
/// or across every window and popup) or by a ref from <c>ui_tree</c>.
/// </summary>
/// <param name="Id">The automation id (or control name), or null when <paramref name="Ref"/> is given.</param>
/// <param name="Ref">A ref such as <c>e12</c> from <c>ui_tree</c>, or null.</param>
/// <param name="Within">An id or ref of an ancestor that scopes <paramref name="Id"/>, e.g. a list row for a per-row button.</param>
internal sealed record UiTarget(string? Id, string? Ref, string? Within = null)
{
    /// <summary>The target as the caller wrote it, for messages.</summary>
    public override string ToString()
        => (Ref ?? Id ?? "?") + (Within is null ? string.Empty : $" within {Within}");
}

/// <summary>A <c>ui_tree</c> request.</summary>
/// <param name="Root">The element to list from, or null for every window and popup.</param>
/// <param name="Depth">Maximum levels below each root.</param>
/// <param name="InteractiveOnly">
/// True to keep only elements with an automation id or an action pattern
/// (their other descendants are promoted); false for every element.
/// </param>
/// <param name="MaxNodes">Maximum elements in the result; the rest are cut and <see cref="UiTreeSnapshot.Truncated"/> is set.</param>
internal sealed record UiTreeQuery(UiTarget? Root, int Depth, bool InteractiveOnly, int MaxNodes);

/// <summary>The element trees on screen.</summary>
/// <param name="Roots">One entry per window or open popup, main window first.</param>
/// <param name="NodeCount">Elements returned.</param>
/// <param name="Truncated">True when <see cref="UiTreeQuery.MaxNodes"/> cut the listing short.</param>
internal sealed record UiTreeSnapshot(IReadOnlyList<UiRootSnapshot> Roots, int NodeCount, bool Truncated);

/// <summary>A window or popup and its element tree.</summary>
/// <param name="Kind">"window" or "popup".</param>
/// <param name="Title">The window title, or null.</param>
/// <param name="Element">The root element.</param>
internal sealed record UiRootSnapshot(string Kind, string? Title, UiElementSnapshot Element);

/// <summary>One element as an automation client sees it.</summary>
/// <param name="Ref">A handle for this element, valid while it stays on screen in this viewer session.</param>
/// <param name="Id">The automation id, else the control name; null when it has neither.</param>
/// <param name="Role">The automation control type, e.g. "button", "listItem", "edit".</param>
/// <param name="ClassName">The control class, e.g. "Button".</param>
/// <param name="Name">The automation name (a button's caption, a text block's text), or null.</param>
/// <param name="HelpText">A longer description than the name (usually the tooltip's text), or null.</param>
/// <param name="AcceleratorKey">The keyboard shortcut that does the same thing, e.g. "Alt+Right", or null.</param>
/// <param name="Text">For an element without a name (a list row, a templated button), its visible text, or null.</param>
/// <param name="Enabled">Whether it is enabled.</param>
/// <param name="Focused">Whether it has keyboard focus.</param>
/// <param name="Patterns">Actions it supports: invoke, toggle, value, rangeValue, selectionItem, expandCollapse.</param>
/// <param name="Toggle">"on", "off" or "indeterminate" for a toggle element.</param>
/// <param name="Value">The text or value for a value element.</param>
/// <param name="Selected">For a selection item, whether it is selected.</param>
/// <param name="Expanded">For an expand / collapse element, whether it is expanded.</param>
/// <param name="Bounds">Position and size in its window, in device-independent pixels.</param>
/// <param name="Children">Child elements, or null for a leaf or when depth ran out.</param>
internal sealed record UiElementSnapshot(
    string Ref,
    string? Id,
    string Role,
    string ClassName,
    string? Name,
    string? HelpText,
    string? AcceleratorKey,
    string? Text,
    bool Enabled,
    bool Focused,
    IReadOnlyList<string> Patterns,
    string? Toggle,
    string? Value,
    bool? Selected,
    bool? Expanded,
    UiBounds Bounds,
    IReadOnlyList<UiElementSnapshot>? Children);

/// <summary>An element's position and size in its window.</summary>
internal sealed record UiBounds(double X, double Y, double Width, double Height);

/// <summary>An element that matched an ambiguous id.</summary>
/// <param name="Ref">Its ref, to target it directly.</param>
/// <param name="Context">The visible text of its row or item, or its own, to tell the matches apart.</param>
internal sealed record UiCandidate(string Ref, string? Context);

/// <summary>Why a <see cref="IViewerUiAutomation"/> call could not be done.</summary>
internal enum UiFailure
{
    /// <summary>No element on screen matched the target.</summary>
    NotFound,

    /// <summary>The id matched several elements; scope it with <c>within</c> or use a ref.</summary>
    Ambiguous,

    /// <summary>The element is disabled.</summary>
    Disabled,

    /// <summary>The element does not support the action.</summary>
    NotSupported,

    /// <summary>The value is not valid for the element.</summary>
    InvalidValue,
}

/// <summary>A <see cref="IViewerUiAutomation"/> call that could not be done.</summary>
internal sealed class UiAutomationException(UiFailure failure, string target, string message) : Exception(message)
{
    /// <summary>Why it failed.</summary>
    public UiFailure Failure { get; } = failure;

    /// <summary>The target as the caller wrote it.</summary>
    public string Target { get; } = target;

    /// <summary>For <see cref="UiFailure.Ambiguous"/>, the matches.</summary>
    public IReadOnlyList<UiCandidate> Candidates { get; init; } = [];

    /// <summary>For <see cref="UiFailure.NotFound"/>, ids on screen that look like the one asked for.</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = [];

    /// <summary>For <see cref="UiFailure.NotSupported"/>, the actions the element does support.</summary>
    public IReadOnlyList<string> Patterns { get; init; } = [];
}
