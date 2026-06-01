using System;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Request payload for <see cref="SetDisplayCategoryTool"/>.</summary>
internal sealed record SetDisplayCategoryRequest(string DisplayCategory);

/// <summary>Result payload for <see cref="SetDisplayCategoryTool"/>.</summary>
internal sealed record SetDisplayCategoryResult(string DisplayCategory, string Previous);

/// <summary>
/// MCP tool that mutates the live viewer's active ECDIS display
/// category (DisplayBase / Standard / OtherInformation / All).
/// </summary>
internal sealed class SetDisplayCategoryTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "set_display_category";

    private readonly IRenderStateControllerAccessor _accessor;

    public SetDisplayCategoryTool(IRenderStateControllerAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <summary>
    /// Sets the live ECDIS display category to the requested value.
    /// Idempotent. Returns the previous category for caller bookkeeping.
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

        var controller = _accessor.Current;
        if (controller is null)
        {
            return ToolResult<SetDisplayCategoryResult>.Err(new MapNotReady(
                "the viewer's render-state controller has not been initialised yet"));
        }

        var previous = controller.CurrentDisplayCategory;
        await controller.SetDisplayCategoryAsync(category, ct).ConfigureAwait(false);
        return ToolResult<SetDisplayCategoryResult>.Ok(
            new SetDisplayCategoryResult(category.ToString(), previous.ToString()));
    }
}
