using System.ComponentModel;
using Avalonia.Threading;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services.DisplayCombinations;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

// Viewer tools for predefined display combinations (#1192; design note
// docs/design/predefined-display-combinations.md §5). An agent can drive the
// picture but not edit the mariner's saved views: there is no save or delete.

// ---------------------------------------------------------------------------
// Wire types
// ---------------------------------------------------------------------------

/// <summary>One display combination, as the View menu lists it.</summary>
[Description("A display combination: a named description of which products are in the map's picture and how they're shown.")]
internal sealed record DisplayCombinationDto(
    [property: Description("Stable id; pass it to set_display_combination. Built-ins: charted, charted-bathymetry, pilotage, passage-planning, all-loaded.")] string Id,
    [property: Description("The name shown in the View menu, e.g. 'Pilotage'.")] string Label,
    [property: Description("'built_in' (ships with SoundCharts) or 'user' (saved by the mariner).")] string Origin,
    [property: Description("Products in the picture ('S-101' covers S-57 and S-401 cells, 'Weather' covers GRIB2 and NetCDF); empty means every product.")] IReadOnlyList<string> Products,
    [property: Description("The products it names that are loaded.")] IReadOnlyList<string> LoadedProducts,
    [property: Description("The products it names that aren't loaded. Applying it never fetches or opens them.")] IReadOnlyList<string> NotLoadedProducts,
    [property: Description("The display category it sets ('DisplayBase', 'Standard', 'OtherInformation', 'All'), or null when it leaves it.")] string? Category,
    [property: Description("The one-line note the View menu shows under it, or null.")] string? Description,
    [property: Description("Its step on the 'Build up the picture' ladder (1-3), or null.")] int? LadderStep);

/// <summary>Result of list_display_combinations.</summary>
[Description("The display combinations and which one the map shows.")]
internal sealed record DisplayCombinationListDto(
    [property: Description("Every combination: built-ins in menu order, then the mariner's own.")] IReadOnlyList<DisplayCombinationDto> Combinations,
    [property: Description("Product keys of the loaded datasets.")] IReadOnlyList<string> LoadedProducts,
    [property: Description("Id of the active combination, or null for Custom (none).")] string? ActiveId,
    [property: Description("True when a setting the active combination covers has changed since it was applied ('Pilotage (modified)').")] bool Modified,
    [property: Description("What differs from the active combination, as the Display controls show it ('Changed: Display category Standard → All.'); null when not modified.")] string? Changes);

/// <summary>A dataset whose Active flag set_display_combination changed.</summary>
[Description("A dataset whose S-98 Active flag the combination changed.")]
internal sealed record DisplayCombinationDatasetDto(
    [property: Description("Dataset id, as list_datasets reports it.")] string Id,
    [property: Description("Its product key, e.g. 'S-102'.")] string Product,
    [property: Description("Its Active flag after the call: false takes it out of the S-98 rules and pick, not just the drawing.")] bool Active);

/// <summary>Result of set_display_combination.</summary>
[Description("What applying a display combination did.")]
internal sealed record SetDisplayCombinationDto(
    [property: Description("The combination active before the call, or null for Custom.")] string? PreviousId,
    [property: Description("The combination active after the call.")] string ActiveId,
    [property: Description("Datasets whose Active flag changed.")] IReadOnlyList<DisplayCombinationDatasetDto> Datasets,
    [property: Description("Products the combination names that aren't loaded; nothing is fetched for them.")] IReadOnlyList<string> NotLoadedProducts);

/// <summary>No display combination has the given id.</summary>
[Description("Raised when no display combination has the requested id (call list_display_combinations).")]
internal sealed record DisplayCombinationNotFound(
    [property: Description("The id that could not be resolved.")] string Id)
    : ToolError("display_combination_not_found", $"No display combination with id '{Id}'.");

/// <summary>set_display_combination reapply with none active.</summary>
[Description("Raised when set_display_combination is asked to reapply but no combination is active.")]
internal sealed record NoActiveDisplayCombination()
    : ToolError("no_active_display_combination", "No display combination is active, so there is nothing to reapply.");

// ---------------------------------------------------------------------------
// Tools
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SetDisplayCombinationTool"/>.</summary>
internal sealed record SetDisplayCombinationRequest(string? Id, bool? Reapply);

/// <summary>Lists the display combinations (MCP <c>list_display_combinations</c>).</summary>
internal sealed class ListDisplayCombinationsTool(DisplayCombinationService service, Func<Action, Task>? dispatch = null)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_display_combinations";

    private readonly DisplayCombinationService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly Func<Action, Task> _dispatch = dispatch ?? DisplayCombinationDispatch.UiThread;

    /// <summary>Reads the combinations on the UI thread.</summary>
    public async Task<ToolResult<DisplayCombinationListDto>> InvokeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        DisplayCombinationListDto? result = null;
        await _dispatch(() =>
        {
            var loaded = _service.LoadedProducts;
            result = new DisplayCombinationListDto(
                [.. _service.Combinations.Select(c => ToDto(c, loaded))],
                [.. loaded.Order(DisplayCombinationProducts.Order)],
                _service.Active?.Id,
                _service.IsModified,
                _service.IsModified ? ViewModels.DisplayCombinationsViewModel.DescribeChanges(_service.Changes) : null);
        }).ConfigureAwait(false);
        return ToolResult<DisplayCombinationListDto>.Ok(result!);
    }

    private static DisplayCombinationDto ToDto(DisplayCombination c, IReadOnlySet<string> loaded)
    {
        var products = c.Products.Order(DisplayCombinationProducts.Order).ToArray();
        return new DisplayCombinationDto(
            c.Id,
            c.Label,
            c.Origin switch
            {
                DisplayCombinationOrigin.User => "user",
                DisplayCombinationOrigin.InteroperabilityCatalogue => "interoperability_catalogue",
                _ => "built_in",
            },
            products,
            [.. products.Where(loaded.Contains)],
            [.. products.Where(p => !loaded.Contains(p))],
            c.Category?.ToString(),
            c.Description,
            c.LadderStep);
    }
}

/// <summary>Applies a display combination (MCP <c>set_display_combination</c>).</summary>
internal sealed class SetDisplayCombinationTool(DisplayCombinationService service, Func<Action, Task>? dispatch = null)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "set_display_combination";

    private readonly DisplayCombinationService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly Func<Action, Task> _dispatch = dispatch ?? DisplayCombinationDispatch.UiThread;

    /// <summary>Applies the request on the UI thread.</summary>
    public async Task<ToolResult<SetDisplayCombinationDto>> InvokeAsync(SetDisplayCombinationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        var reapply = request.Reapply == true;
        var id = request.Id?.Trim();
        if (reapply == !string.IsNullOrEmpty(id))
            return ToolResult<SetDisplayCombinationDto>.Err(new InvalidArgument("id", "give either an id or reapply: true; call list_display_combinations for the ids"));

        DisplayCombinationApplyResult? applied = null;
        await _dispatch(() => applied = reapply ? _service.Reapply() : _service.Apply(id!)).ConfigureAwait(false);
        if (applied is null)
        {
            return reapply
                ? ToolResult<SetDisplayCombinationDto>.Err(new NoActiveDisplayCombination())
                : ToolResult<SetDisplayCombinationDto>.Err(new DisplayCombinationNotFound(id!));
        }
        return ToolResult<SetDisplayCombinationDto>.Ok(new SetDisplayCombinationDto(
            applied.PreviousId,
            applied.ActiveId,
            [.. applied.Datasets.Select(d => new DisplayCombinationDatasetDto(d.Id, d.Product, d.Active))],
            applied.NotLoaded));
    }
}

internal static class DisplayCombinationDispatch
{
    /// <summary>Runs on the UI thread, where the display state and the datasets live.</summary>
    public static Task UiThread(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();
}

/// <summary>Wraps the display-combination tools as MCP server tools.</summary>
internal static class DisplayCombinationMcpAdapters
{
    /// <summary>Creates <c>list_display_combinations</c>.</summary>
    public static McpServerTool Create(ListDisplayCombinationsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(ct));
        return Tool(del, ListDisplayCombinationsTool.Name,
            "Lists the live viewer's display combinations, as its View menu does: the built-ins (Charted only, "
            + "Chart + bathymetry, Pilotage, Passage planning, Everything loaded) and the mariner's own, each with the "
            + "products it puts in the picture and which of them are loaded; the active combination's id (null for "
            + "Custom) and whether it's modified. A combination sets which products are active for S-98, the display "
            + "category, viewing groups and product layers; it never changes mariner safety settings, the "
            + "Navigational warnings switch, display planes, the palette, own ship, AIS or routes, and route checks "
            + "still use every loaded dataset. Read-only; viewer-injected tool.");
    }

    /// <summary>Creates <c>set_display_combination</c>.</summary>
    public static McpServerTool Create(SetDisplayCombinationTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Id of the combination to apply, from list_display_combinations, e.g. 'pilotage'. Omit with reapply.")] string? id = null,
            [Description("True applies the active combination again, putting back every setting it covers (the Reapply button). Omit with id.")] bool? reapply = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetDisplayCombinationRequest(id, reapply), ct));
        return Tool(del, SetDisplayCombinationTool.Name,
            "Applies a display combination in the live viewer, as choosing it in the View menu does: products it "
            + "leaves out have every loaded dataset made inactive (out of the S-98 rules and pick, not just hidden), "
            + "products it includes are made active, and its display category, viewing groups, layers and sub-layers "
            + "are set. Products it names that aren't loaded are reported, never fetched; they take the combination's "
            + "rule when they load. It can't save, rename or delete the mariner's views. Returns the combination "
            + "before and after and the datasets whose Active flag changed; call await_render_idle before a "
            + "screenshot. Fails with display_combination_not_found or no_active_display_combination. Mutating; "
            + "viewer-injected tool.");
    }

    private static McpServerTool Tool(Delegate del, string name, string description) =>
        McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = name,
            Description = description,
            SerializerOptions = McpAdapterShared.Options,
        });
}
