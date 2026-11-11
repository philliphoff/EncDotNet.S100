using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>Request payload for <see cref="SetDisplayCategoryTool"/>.</summary>
public sealed record SetDisplayCategoryRequest(
    [property: Description("ECDIS display category: 'DisplayBase', 'Standard', 'OtherInformation', or 'All' (case-insensitive).")] string DisplayCategory);

/// <summary>Result payload for <see cref="SetDisplayCategoryTool"/>.</summary>
public sealed record SetDisplayCategoryResult(
    [property: Description("The display category now applied.")] string DisplayCategory,
    [property: Description("The display category applied before this call; equal to DisplayCategory when the call was a no-op.")] string Previous);

/// <summary>
/// Mutating tool that sets the map-wide ECDIS display category
/// (DisplayBase / Standard / OtherInformation / All). Reads the current
/// presentation, swaps only <see cref="EcdisDisplaySettings.Category"/>, and
/// applies the result.
/// </summary>
public sealed class SetDisplayCategoryTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "set_display_category";

    private readonly ICapabilityAccessor<IPresentationController> _presentation;

    /// <summary>Creates the tool bound to a presentation-controller accessor.</summary>
    public SetDisplayCategoryTool(ICapabilityAccessor<IPresentationController> presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        _presentation = presentation;
    }

    /// <summary>
    /// Sets the ECDIS display category. Idempotent. Returns the previous
    /// category for caller bookkeeping.
    /// </summary>
    public async Task<ToolResult<SetDisplayCategoryResult>> InvokeAsync(
        SetDisplayCategoryRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.DisplayCategory))
        {
            return ToolResult<SetDisplayCategoryResult>.Err(new InvalidArgument(
                "displayCategory",
                "value is required; allowed values are 'DisplayBase', 'Standard', 'OtherInformation', 'All'"));
        }

        // Reject pure-numeric input — see SetPaletteTool for rationale.
        var trimmed = request.DisplayCategory.Trim();
        if (long.TryParse(trimmed, out _)
            || !Enum.TryParse<EcdisDisplayCategory>(trimmed, ignoreCase: true, out var category)
            || !Enum.IsDefined(category))
        {
            return ToolResult<SetDisplayCategoryResult>.Err(new InvalidArgument(
                "displayCategory",
                $"value '{request.DisplayCategory}' is not one of: DisplayBase, Standard, OtherInformation, All"));
        }

        var controller = _presentation.Current;
        if (controller is null)
        {
            return ToolResult<SetDisplayCategoryResult>.Err(
                new HostNotReady("the presentation controller is not attached yet"));
        }

        var state = controller.Current;
        var previous = state.EcdisDisplay.Category;
        var updated = state.WithEcdisDisplay(state.EcdisDisplay with { Category = category });
        await controller.SetPresentationAsync(updated, ct).ConfigureAwait(false);
        return ToolResult<SetDisplayCategoryResult>.Ok(
            new SetDisplayCategoryResult(category.ToString(), previous.ToString()));
    }
}
