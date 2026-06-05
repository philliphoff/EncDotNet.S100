using System;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Request payload for <see cref="SetPaletteTool"/>.</summary>
internal sealed record SetPaletteRequest(string Palette);

/// <summary>Result payload for <see cref="SetPaletteTool"/>.</summary>
internal sealed record SetPaletteResult(string Palette, string Previous);

/// <summary>
/// MCP tool that mutates the live viewer's active map palette
/// (Day / Dusk / Night). The companion to <c>set_viewport</c> for
/// scripted measurement runs that need to drive palette changes
/// from outside the GUI.
/// </summary>
internal sealed class SetPaletteTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "set_palette";

    private readonly IRenderStateControllerAccessor _accessor;

    public SetPaletteTool(IRenderStateControllerAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <summary>
    /// Sets the live map palette to the requested value. Idempotent.
    /// Returns the previous palette so callers can detect no-op
    /// invocations and stitch repeated runs cleanly.
    /// </summary>
    public async Task<ToolResult<SetPaletteResult>> InvokeAsync(
        SetPaletteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Palette))
        {
            return ToolResult<SetPaletteResult>.Err(new InvalidArgument(
                "palette",
                "value is required; allowed values are 'Day', 'Dusk', 'Night'"));
        }

        // Reject pure-numeric input. Enum.TryParse accepts the underlying
        // integer (e.g. "0" → Day), but the public contract here is the
        // string name only; numeric coupling would be brittle.
        var trimmed = request.Palette.Trim();
        if (long.TryParse(trimmed, out _)
            || !Enum.TryParse<PaletteType>(trimmed, ignoreCase: true, out var palette)
            || !Enum.IsDefined(palette))
        {
            return ToolResult<SetPaletteResult>.Err(new InvalidArgument(
                "palette",
                $"value '{request.Palette}' is not one of: Day, Dusk, Night"));
        }

        var controller = _accessor.Current;
        if (controller is null)
        {
            return ToolResult<SetPaletteResult>.Err(new MapNotReady(
                "the viewer's render-state controller has not been initialised yet"));
        }

        var previous = controller.CurrentPalette;
        await controller.SetPaletteAsync(palette, ct).ConfigureAwait(false);
        return ToolResult<SetPaletteResult>.Ok(new SetPaletteResult(palette.ToString(), previous.ToString()));
    }
}
