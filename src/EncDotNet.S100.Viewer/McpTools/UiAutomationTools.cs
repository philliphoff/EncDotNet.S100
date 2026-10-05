using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

// Test-only tools (registered with --mcp-test-hooks) that drive the viewer's UI
// through Avalonia's automation peers: list what is on screen, then click,
// type, toggle, select, expand or focus one element by automation id or by a
// ref from the listing (#776 chunk 2).

// ---------------------------------------------------------------------------
// Errors
// ---------------------------------------------------------------------------

/// <summary>No element on screen matched the target.</summary>
[Description("Raised when no element on screen matches the id or ref (call ui_tree for what is on screen).")]
internal sealed record UiElementNotFound(
    [property: Description("The target as given.")] string Target,
    [property: Description("Ids on screen that look like the one asked for.")] IReadOnlyList<string> Suggestions)
    : ToolError("ui_element_not_found", $"No element on screen matches '{Target}'.");

/// <summary>An id matched several elements.</summary>
[Description("Raised when an id matches several elements, such as a button repeated on every list row. Target one by its ref, or scope the id with 'within'.")]
internal sealed record UiElementAmbiguous(
    [property: Description("The target as given.")] string Target,
    [property: Description("The matches: each one's ref and the visible text of its row or item.")] IReadOnlyList<UiCandidate> Candidates)
    : ToolError("ui_element_ambiguous", $"'{Target}' matches {Candidates.Count} elements; use a ref or 'within'.");

/// <summary>The element is disabled.</summary>
[Description("Raised when the element is disabled, as a greyed-out button is for a user.")]
internal sealed record UiElementDisabled(
    [property: Description("The target as given.")] string Target)
    : ToolError("ui_element_disabled", $"'{Target}' is disabled.");

/// <summary>The element does not support the action.</summary>
[Description("Raised when the element does not support the action, e.g. ui_invoke on a list row (use ui_select).")]
internal sealed record UiActionNotSupported(
    [property: Description("The target as given.")] string Target,
    [property: Description("The tool that was called.")] string Action,
    [property: Description("The actions the element supports: invoke, toggle, value, rangeValue, selectionItem, expandCollapse.")] IReadOnlyList<string> Patterns,
    [property: Description("Why, when there is more to say.")] string Reason)
    : ToolError("ui_action_not_supported", Reason);

/// <summary>Maps <see cref="UiAutomationException"/> to the tools' errors.</summary>
internal static class UiAutomationErrors
{
    /// <summary>The tool error for <paramref name="ex"/>.</summary>
    public static ToolError From(UiAutomationException ex, string tool) => ex.Failure switch
    {
        UiFailure.NotFound => new UiElementNotFound(ex.Target, ex.Suggestions),
        UiFailure.Ambiguous => new UiElementAmbiguous(ex.Target, ex.Candidates),
        UiFailure.Disabled => new UiElementDisabled(ex.Target),
        UiFailure.NotSupported => new UiActionNotSupported(ex.Target, tool, ex.Patterns, ex.Message),
        _ => new InvalidArgument("value", ex.Message),
    };

    /// <summary>Validates an id / ref / within triple.</summary>
    public static InvalidArgument? Validate(string? id, string? reference)
    {
        var hasId = !string.IsNullOrWhiteSpace(id);
        var hasRef = !string.IsNullOrWhiteSpace(reference);
        return hasId == hasRef
            ? new InvalidArgument(hasId ? "ref" : "id", "give exactly one of 'id' (an automation id) or 'ref' (from ui_tree)")
            : null;
    }
}

// ---------------------------------------------------------------------------
// ui_tree
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="UiTreeTool"/>.</summary>
internal sealed record UiTreeRequest(string? Root, int? Depth, string? Filter, int? MaxNodes);

/// <summary>Lists the elements on screen (MCP <c>ui_tree</c>).</summary>
internal sealed class UiTreeTool(IViewerUiAutomation automation)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "ui_tree";

    /// <summary>Levels listed when the request does not say.</summary>
    public const int DefaultDepth = 30;

    /// <summary>Elements listed when the request does not say.</summary>
    public const int DefaultMaxNodes = 400;

    private readonly IViewerUiAutomation _automation = automation ?? throw new ArgumentNullException(nameof(automation));

    /// <summary>Lists the tree.</summary>
    public async Task<ToolResult<UiTreeSnapshot>> InvokeAsync(UiTreeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var depth = request.Depth ?? DefaultDepth;
        if (depth is < 0 or > 100)
            return ToolResult<UiTreeSnapshot>.Err(new InvalidArgument("depth", "expected 0–100"));
        var maxNodes = request.MaxNodes ?? DefaultMaxNodes;
        if (maxNodes is < 1 or > 5000)
            return ToolResult<UiTreeSnapshot>.Err(new InvalidArgument("maxNodes", "expected 1–5000"));
        var interactive = (request.Filter?.Trim().ToLowerInvariant() ?? "interactive") switch
        {
            "interactive" => (bool?)true,
            "all" => false,
            _ => null,
        };
        if (interactive is null)
            return ToolResult<UiTreeSnapshot>.Err(new InvalidArgument("filter", "expected 'interactive' or 'all'"));

        var root = string.IsNullOrWhiteSpace(request.Root) ? null : UiTargets.Parse(request.Root.Trim());
        try
        {
            return ToolResult<UiTreeSnapshot>.Ok(await _automation.GetTreeAsync(
                new UiTreeQuery(root, depth, interactive.Value, maxNodes), ct).ConfigureAwait(false));
        }
        catch (UiAutomationException ex)
        {
            return ToolResult<UiTreeSnapshot>.Err(UiAutomationErrors.From(ex, Name));
        }
    }
}

// ---------------------------------------------------------------------------
// ui_invoke / ui_set_value / ui_toggle / ui_select / ui_expand / ui_collapse /
// ui_focus / ui_context_menu
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="UiActionTool"/>.</summary>
internal sealed record UiActionRequest(string? Id, string? Ref, string? Within, string? Value);

/// <summary>Performs one <see cref="UiAction"/> on one element (MCP <c>ui_invoke</c> and siblings).</summary>
internal sealed class UiActionTool(IViewerUiAutomation automation, UiAction action)
{
    private readonly IViewerUiAutomation _automation = automation ?? throw new ArgumentNullException(nameof(automation));

    /// <summary>The action this tool performs.</summary>
    public UiAction Action { get; } = action;

    /// <summary>The MCP tool name for <see cref="Action"/>.</summary>
    public string Name { get; } = NameOf(action);

    /// <summary>The MCP tool name for <paramref name="action"/>.</summary>
    public static string NameOf(UiAction action) => action switch
    {
        UiAction.Invoke => "ui_invoke",
        UiAction.SetValue => "ui_set_value",
        UiAction.Toggle => "ui_toggle",
        UiAction.Select => "ui_select",
        UiAction.Expand => "ui_expand",
        UiAction.Collapse => "ui_collapse",
        UiAction.Focus => "ui_focus",
        UiAction.ContextMenu => "ui_context_menu",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>Performs the action.</summary>
    public async Task<ToolResult<UiElementSnapshot>> InvokeAsync(UiActionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (UiAutomationErrors.Validate(request.Id, request.Ref) is { } invalid)
            return ToolResult<UiElementSnapshot>.Err(invalid);
        if (Action == UiAction.SetValue && request.Value is null)
            return ToolResult<UiElementSnapshot>.Err(new InvalidArgument("value", "the text or number to set is required"));

        var target = new UiTarget(Trim(request.Id), Trim(request.Ref), Trim(request.Within));
        try
        {
            return ToolResult<UiElementSnapshot>.Ok(
                await _automation.ActAsync(target, Action, request.Value, ct).ConfigureAwait(false));
        }
        catch (UiAutomationException ex)
        {
            return ToolResult<UiElementSnapshot>.Err(UiAutomationErrors.From(ex, Name));
        }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Reads a single id-or-ref argument.</summary>
internal static class UiTargets
{
    /// <summary>A ref (<c>e</c> and digits, from ui_tree) or else an automation id.</summary>
    public static UiTarget Parse(string idOrRef)
        => IsRef(idOrRef) ? new UiTarget(null, idOrRef) : new UiTarget(idOrRef, null);

    /// <summary>Whether <paramref name="value"/> has the shape of a ref.</summary>
    public static bool IsRef(string value)
        => value.Length > 1 && value[0] == 'e' && value.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;
}
