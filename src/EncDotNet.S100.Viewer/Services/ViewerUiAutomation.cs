using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// <see cref="IViewerUiAutomation"/> over Avalonia's automation peers: the same
/// tree and patterns a screen reader or UI-automation client uses, so anything
/// the tools can do, an accessibility client can do too.
/// </summary>
/// <remarks>
/// Elements are found by <c>AutomationProperties.AutomationId</c> (see the
/// convention in <c>tests/EncDotNet.S100.Viewer.Tests/README.md</c>) or by a
/// ref handed out by <see cref="GetTreeAsync"/>. Refs are weak: they stop
/// resolving once their element leaves the screen.
/// </remarks>
internal sealed partial class ViewerUiAutomation : IViewerUiAutomation
{
    /// <summary>The patterns whose elements a user can act on; they keep an element in the interactive tree.</summary>
    private static readonly string[] ActionPatterns =
        ["invoke", "toggle", "value", "rangeValue", "selectionItem", "expandCollapse"];

    private const int MaxTextLength = 120;

    private readonly Func<IEnumerable<TopLevel>> _windows;
    private readonly ConditionalWeakTable<Control, string> _refs = new();
    private readonly Dictionary<string, WeakReference<Control>> _byRef = new(StringComparer.Ordinal);
    private int _nextRef;

    /// <summary>Creates the automation over the given windows plus every open popup.</summary>
    /// <param name="windows">The windows to expose, main window first.</param>
    public ViewerUiAutomation(Func<IEnumerable<TopLevel>> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        _windows = windows;
        OpenPopups.EnsureTracking();
    }

    /// <summary>Creates the automation over the running application's windows.</summary>
    public static ViewerUiAutomation ForApplication() => new(() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.OrderByDescending(w => ReferenceEquals(w, desktop.MainWindow))
            : []);

    /// <inheritdoc />
    public Task<UiTreeSnapshot> GetTreeAsync(UiTreeQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        return Dispatcher.UIThread.InvokeAsync(() => BuildTree(query)).GetTask();
    }

    /// <inheritdoc />
    public async Task<UiElementSnapshot> ActAsync(UiTarget target, UiAction action, string? value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ct.ThrowIfCancellationRequested();
        var control = await Dispatcher.UIThread.InvokeAsync(() => Act(target, action, value)).GetTask().ConfigureAwait(false);

        // Read the element back once the action's bindings, layout and any
        // dispatcher work it queued have run.
        return await Dispatcher.UIThread.InvokeAsync(
            () => Snapshot(control, depth: 0, interactiveOnly: false, budget: null),
            DispatcherPriority.Background).GetTask().ConfigureAwait(false);
    }

    // ── tree ─────────────────────────────────────────────────────────────

    private UiTreeSnapshot BuildTree(UiTreeQuery query)
    {
        var budget = new Budget(query.MaxNodes);
        var roots = new List<UiRootSnapshot>();
        if (query.Root is { } rootTarget)
        {
            var control = Resolve(rootTarget);
            roots.Add(new UiRootSnapshot(KindOf(TopLevel.GetTopLevel(control)), TitleOf(TopLevel.GetTopLevel(control)),
                Snapshot(control, query.Depth, query.InteractiveOnly, budget)));
        }
        else
        {
            foreach (var topLevel in Roots())
            {
                if (budget.Exhausted)
                    break;
                roots.Add(new UiRootSnapshot(KindOf(topLevel), TitleOf(topLevel),
                    Snapshot(topLevel, query.Depth, query.InteractiveOnly, budget)));
            }
        }

        return new UiTreeSnapshot(roots, budget.Used, budget.Truncated);
    }

    private UiElementSnapshot Snapshot(Control control, int depth, bool interactiveOnly, Budget? budget)
    {
        budget?.Take();
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        IReadOnlyList<UiElementSnapshot>? children = null;
        if (depth > 0 && budget is not null)
        {
            var list = new List<UiElementSnapshot>();
            CollectChildren(peer, depth, interactiveOnly, budget, list);
            children = list.Count > 0 ? list : null;
        }

        return Describe(control, peer, children);
    }

    /// <summary>
    /// Adds <paramref name="peer"/>'s children to <paramref name="into"/>. In the
    /// interactive view an element without an id or an action pattern is left
    /// out and its own children are promoted in its place.
    /// </summary>
    private void CollectChildren(AutomationPeer peer, int depth, bool interactiveOnly, Budget budget, List<UiElementSnapshot> into)
    {
        foreach (var childPeer in peer.GetChildren())
        {
            if (budget.Exhausted)
                return;
            if (childPeer is not ControlAutomationPeer { Owner: { IsEffectivelyVisible: true } child })
                continue;

            if (interactiveOnly && !IsInteresting(child, childPeer))
            {
                CollectChildren(childPeer, depth, interactiveOnly, budget, into);
                continue;
            }

            into.Add(Snapshot(child, depth - 1, interactiveOnly, budget));
        }
    }

    private static bool IsInteresting(Control control, AutomationPeer peer)
        => AutomationProperties.GetAutomationId(control) is { Length: > 0 }
            || PatternsOf(peer, control).Any(p => ActionPatterns.Contains(p));

    private UiElementSnapshot Describe(Control control, AutomationPeer peer, IReadOnlyList<UiElementSnapshot>? children)
    {
        var name = NameOf(peer);
        var toggle = peer.GetProvider<IToggleProvider>();
        var value = peer.GetProvider<IValueProvider>();
        var range = peer.GetProvider<IRangeValueProvider>();
        var selection = peer.GetProvider<ISelectionItemProvider>();
        var expand = peer.GetProvider<IExpandCollapseProvider>();
        return new UiElementSnapshot(
            Ref: RefOf(control),
            Id: IdOf(control),
            Role: Camel(peer.GetAutomationControlType().ToString()),
            ClassName: peer.GetClassName(),
            Name: name,
            Text: name is null ? VisibleText(control) ?? ToolTipText(control) : null,
            Enabled: peer.IsEnabled(),
            Focused: peer.HasKeyboardFocus(),
            Patterns: PatternsOf(peer, control),
            Toggle: toggle is null ? null : Camel(toggle.ToggleState.ToString()),
            Value: value?.Value ?? range?.Value.ToString(CultureInfo.InvariantCulture),
            Selected: selection?.IsSelected,
            Expanded: expand is not null
                ? expand.ExpandCollapseState is ExpandCollapseState.Expanded or ExpandCollapseState.PartiallyExpanded
                : control is TreeViewItem { ItemCount: > 0 } node ? node.IsExpanded : null,
            Bounds: BoundsOf(control),
            Children: children);
    }

    private static IReadOnlyList<string> PatternsOf(AutomationPeer peer, Control control)
    {
        var patterns = new List<string>(3);
        if (peer.GetProvider<IInvokeProvider>() is not null || control is MenuItem { HasSubMenu: false }) patterns.Add("invoke");
        if (peer.GetProvider<IToggleProvider>() is not null) patterns.Add("toggle");
        if (peer.GetProvider<IValueProvider>() is not null) patterns.Add("value");
        if (peer.GetProvider<IRangeValueProvider>() is not null) patterns.Add("rangeValue");
        if (peer.GetProvider<ISelectionItemProvider>() is not null) patterns.Add("selectionItem");
        if (peer.GetProvider<IExpandCollapseProvider>() is not null
            || IsExpandableTreeItem(control)
            || control is MenuItem { HasSubMenu: true })
        {
            patterns.Add("expandCollapse");
        }
        return patterns;
    }

    // ── actions ──────────────────────────────────────────────────────────

    private Control Act(UiTarget target, UiAction action, string? value)
    {
        var control = Resolve(target);
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        var where = target.ToString();
        if (action is not UiAction.Focus && !peer.IsEnabled())
            throw new UiAutomationException(UiFailure.Disabled, where, $"'{where}' is disabled.");

        switch (action)
        {
            case UiAction.Invoke:
                Invoke(control, peer, where);
                break;
            case UiAction.SetValue:
                SetValue(peer, where, value);
                break;
            case UiAction.Toggle:
                Toggle(Require<IToggleProvider>(peer, where, "toggle"), where, value);
                break;
            case UiAction.Select:
                Require<ISelectionItemProvider>(peer, where, "selectionItem").Select();
                break;
            case UiAction.Expand:
            case UiAction.Collapse:
                SetExpanded(control, peer, where, action == UiAction.Expand);
                break;
            case UiAction.Focus:
                if (!peer.IsKeyboardFocusable())
                    throw NotSupported(peer, where, "focus");
                peer.SetFocus();
                break;
            case UiAction.ContextMenu:
                ShowContextMenu(control, peer, where);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }

        return control;
    }

    /// <summary>
    /// Opens the context menu as a right-click does: a right-click on a list row
    /// or tree node selects it first (menus such as the Library tree's act on the
    /// selection), then <c>ContextRequested</c> is raised from the element so the
    /// view's handlers run as well. The peer's own <c>ShowContextMenu</c> opens
    /// the nearest menu directly, skipping both, so it is only the fallback.
    /// </summary>
    private static void ShowContextMenu(Control control, AutomationPeer peer, string where)
    {
        if (peer.GetProvider<ISelectionItemProvider>() is { IsSelected: false } item)
            item.Select();

        var request = new ContextRequestedEventArgs();
        control.RaiseEvent(request);
        if (!request.Handled && !peer.ShowContextMenu())
            throw NotSupported(peer, where, "contextMenu");
    }

    /// <summary>
    /// Invokes through the invoke pattern. Avalonia's menu items have no such
    /// pattern (only toggle, for check and radio items), so a menu item without
    /// a submenu is clicked as the pointer does: its <c>Click</c> runs its
    /// command and handlers, then its menu closes.
    /// </summary>
    private static void Invoke(Control control, AutomationPeer peer, string where)
    {
        if (peer.GetProvider<IInvokeProvider>() is { } provider)
        {
            provider.Invoke();
            return;
        }

        if (control is MenuItem { HasSubMenu: false } item)
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            item.FindLogicalAncestorOfType<MenuBase>()?.Close();
            return;
        }

        throw NotSupported(peer, where, "invoke");
    }

    /// <summary>
    /// Expands or collapses through the expand / collapse pattern. Avalonia's
    /// tree items and menu items have no such pattern (a tree item's expander is
    /// a separate toggle), so a tree item with children, or a menu item with a
    /// submenu, is expanded directly, as its expander or a hover does.
    /// </summary>
    private static void SetExpanded(Control control, AutomationPeer peer, string where, bool expand)
    {
        if (peer.GetProvider<IExpandCollapseProvider>() is { } provider)
        {
            if (expand)
                provider.Expand();
            else
                provider.Collapse();
            return;
        }

        if (control is TreeViewItem node && IsExpandableTreeItem(node))
        {
            node.IsExpanded = expand;
            return;
        }

        if (control is MenuItem { HasSubMenu: true } menu)
        {
            menu.IsSubMenuOpen = expand;
            return;
        }

        throw NotSupported(peer, where, "expandCollapse");
    }

    private static bool IsExpandableTreeItem(Control control) => control is TreeViewItem { ItemCount: > 0 };

    private static void SetValue(AutomationPeer peer, string where, string? value)
    {
        if (peer.GetProvider<IValueProvider>() is { } text)
        {
            if (text.IsReadOnly)
                throw new UiAutomationException(UiFailure.NotSupported, where, $"'{where}' is read-only.") { Patterns = PatternsOf(peer, ControlOf(peer)) };
            text.SetValue(value ?? string.Empty);
            return;
        }

        var range = Require<IRangeValueProvider>(peer, where, "value");
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || number < range.Minimum || number > range.Maximum)
        {
            throw new UiAutomationException(UiFailure.InvalidValue, where,
                $"'{where}' takes a number from {range.Minimum.ToString(CultureInfo.InvariantCulture)} to {range.Maximum.ToString(CultureInfo.InvariantCulture)}.");
        }

        range.SetValue(number);
    }

    private static void Toggle(IToggleProvider toggle, string where, string? wanted)
    {
        if (wanted is null)
        {
            toggle.Toggle();
            return;
        }

        var state = wanted.Trim().ToLowerInvariant() switch
        {
            "on" or "true" => ToggleState.On,
            "off" or "false" => ToggleState.Off,
            _ => throw new UiAutomationException(UiFailure.InvalidValue, where, "The state is 'on' or 'off'."),
        };

        // A three-state check box cycles through indeterminate, so allow a full turn.
        for (var i = 0; i < 3 && toggle.ToggleState != state; i++)
            toggle.Toggle();
    }

    private static T Require<T>(AutomationPeer peer, string where, string pattern)
        where T : class
        => peer.GetProvider<T>() ?? throw NotSupported(peer, where, pattern);

    private static UiAutomationException NotSupported(AutomationPeer peer, string where, string pattern)
        => new(UiFailure.NotSupported, where, $"'{where}' ({peer.GetClassName()}) does not support {pattern}.")
        {
            Patterns = PatternsOf(peer, ControlOf(peer)),
        };

    private static Control ControlOf(AutomationPeer peer) => ((ControlAutomationPeer)peer).Owner;

    // ── resolution ───────────────────────────────────────────────────────

    private Control Resolve(UiTarget target)
    {
        if (target.Ref is { Length: > 0 } reference)
            return ByRef(reference, target.ToString());

        var id = target.Id ?? throw new UiAutomationException(UiFailure.NotFound, target.ToString(), "Give an id or a ref.");
        IEnumerable<Control> scope;
        if (target.Within is { Length: > 0 } within)
        {
            var ancestor = McpTools.UiTargets.IsRef(within)
                ? ByRef(within, within)
                : Resolve(new UiTarget(within, null));
            scope = ancestor.GetVisualDescendants().OfType<Control>();
        }
        else
        {
            scope = Roots().SelectMany(r => r.GetVisualDescendants().OfType<Control>());
        }

        var matches = scope.Where(c => c.IsEffectivelyVisible && IdOf(c) == id).Distinct().ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new UiAutomationException(UiFailure.NotFound, target.ToString(), $"No element on screen has id '{id}'.")
            {
                Suggestions = Suggest(id),
            },
            _ => throw new UiAutomationException(UiFailure.Ambiguous, target.ToString(),
                $"{matches.Count} elements have id '{id}'; target one by its ref, or scope the id with 'within'.")
            {
                Candidates = matches.Select(c => new UiCandidate(RefOf(c), ContextOf(c))).ToList(),
            },
        };
    }

    private Control ByRef(string reference, string where)
    {
        if (_byRef.TryGetValue(reference, out var weak)
            && weak.TryGetTarget(out var control)
            && TopLevel.GetTopLevel(control) is not null
            && control.IsEffectivelyVisible)
        {
            return control;
        }

        throw new UiAutomationException(UiFailure.NotFound, where,
            $"Ref '{reference}' is not on screen any more; call ui_tree for current refs.");
    }

    /// <summary>Ids on screen that contain the one asked for, or share its first segment.</summary>
    private IReadOnlyList<string> Suggest(string id)
    {
        var prefix = id.Split('.')[0];
        return Roots()
            .SelectMany(r => r.GetVisualDescendants().OfType<Control>())
            .Where(c => c.IsEffectivelyVisible)
            .Select(c => AutomationProperties.GetAutomationId(c))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Where(candidate => candidate.Contains(id, StringComparison.OrdinalIgnoreCase)
                || id.Contains(candidate, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Take(10)
            .ToList();
    }

    private IEnumerable<TopLevel> Roots()
    {
        var seen = new HashSet<TopLevel>();
        foreach (var window in _windows())
        {
            if (window.IsVisible && seen.Add(window))
                yield return window;
        }

        foreach (var popup in OpenPopups.Current())
        {
            if (seen.Add(popup))
                yield return popup;
        }
    }

    // ── describing ───────────────────────────────────────────────────────

    private string RefOf(Control control)
    {
        if (_refs.TryGetValue(control, out var existing))
            return existing;

        var reference = "e" + (++_nextRef).ToString(CultureInfo.InvariantCulture);
        _refs.Add(control, reference);
        _byRef[reference] = new WeakReference<Control>(control);
        if (_nextRef % 1000 == 0)
            PruneRefs();
        return reference;
    }

    private void PruneRefs()
    {
        foreach (var dead in _byRef.Where(kv => !kv.Value.TryGetTarget(out _)).Select(kv => kv.Key).ToList())
            _byRef.Remove(dead);
    }

    /// <summary>The automation id, else a control name that isn't a template part.</summary>
    private static string? IdOf(Control control)
        => AutomationProperties.GetAutomationId(control) is { Length: > 0 } id
            ? id
            : control.Name is { Length: > 0 } name && !name.StartsWith("PART_", StringComparison.Ordinal) ? name : null;

    /// <summary>
    /// The automation name, unless it is only a type name: a peer without an
    /// explicit name falls back to its content's <c>ToString()</c>, which for a
    /// row's view model or an icon is the class name, not anything a user sees.
    /// </summary>
    private static string? NameOf(AutomationPeer peer)
    {
        var name = NullIfEmpty(peer.GetName());
        return name is not null && TypeName().IsMatch(name) ? null : name;
    }

    [GeneratedRegex(@"^[A-Za-z_][\w]*(\.[A-Za-z_][\w`]*)+$")]
    private static partial Regex TypeName();

    /// <summary>A string tooltip, which names an icon-only button for a user.</summary>
    private static string? ToolTipText(Control control) => NullIfEmpty(ToolTip.GetTip(control) as string);

    /// <summary>The visible text under an element, joined, to name rows and templated buttons.</summary>
    private static string? VisibleText(Control control)
    {
        // A tree node's or list row's own text, not that of the items nested in it.
        var parts = control.GetSelfAndVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text))
            .Where(t => !t.GetVisualAncestors().TakeWhile(a => !ReferenceEquals(a, control))
                .Any(a => a is TreeViewItem or ListBoxItem))
            .Select(t => t.Text!.Trim())
            .Distinct()
            .Take(6);
        var text = string.Join(" · ", parts);
        return text.Length == 0 ? null : text.Length <= MaxTextLength ? text : text[..MaxTextLength] + "…";
    }

    /// <summary>The text of the list row or item holding <paramref name="control"/>, to tell matches apart.</summary>
    private static string? ContextOf(Control control)
    {
        var item = control.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(c => c is ListBoxItem or TreeViewItem or TabItem or ComboBoxItem
                || ControlAutomationPeer.CreatePeerForElement(c).GetProvider<ISelectionItemProvider>() is not null);
        return VisibleText(item ?? control);
    }

    private static UiBounds BoundsOf(Control control)
    {
        var origin = TopLevel.GetTopLevel(control) is { } topLevel
            ? control.TranslatePoint(default, topLevel) ?? default
            : default;
        return new UiBounds(Math.Round(origin.X, 1), Math.Round(origin.Y, 1),
            Math.Round(control.Bounds.Width, 1), Math.Round(control.Bounds.Height, 1));
    }

    private static string KindOf(TopLevel? topLevel) => topLevel is PopupRoot ? "popup" : "window";

    private static string? TitleOf(TopLevel? topLevel) => (topLevel as Window)?.Title;

    private static string Camel(string value) => value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Counts listed elements against <see cref="UiTreeQuery.MaxNodes"/>.</summary>
    private sealed class Budget(int max)
    {
        public int Used { get; private set; }

        public bool Truncated { get; private set; }

        public bool Exhausted => Used >= max;

        public void Take()
        {
            Used++;
            if (Used >= max)
                Truncated = true;
        }
    }

    /// <summary>
    /// Tracks open popups (menus, flyouts, combo box drop-downs), which are top
    /// levels of their own rather than part of a window's tree.
    /// </summary>
    private static class OpenPopups
    {
        private static readonly List<WeakReference<Popup>> Popups = [];
        private static int _tracking;

        public static void EnsureTracking()
        {
            if (Interlocked.Exchange(ref _tracking, 1) == 1)
                return;
            Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, e) =>
            {
                if (e.NewValue is true)
                    Popups.Add(new WeakReference<Popup>(popup));
            });
        }

        public static IEnumerable<TopLevel> Current()
        {
            Popups.RemoveAll(w => !w.TryGetTarget(out var p) || !p.IsOpen);
            foreach (var weak in Popups.ToList())
            {
                // An overlay popup lives in its window's tree; only a popup root is a top level of its own.
                if (weak.TryGetTarget(out var popup)
                    && popup.Child is { } child
                    && TopLevel.GetTopLevel(child) is PopupRoot { IsVisible: true } host)
                {
                    yield return host;
                }
            }
        }
    }
}
