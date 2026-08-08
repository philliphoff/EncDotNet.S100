using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>Request payload for <see cref="SetPaletteTool"/>.</summary>
public sealed record SetPaletteRequest(
    [property: Description("Colour palette to apply: 'Day', 'Dusk', or 'Night' (case-insensitive).")] string Palette);

/// <summary>Result payload for <see cref="SetPaletteTool"/>.</summary>
public sealed record SetPaletteResult(
    [property: Description("The palette now applied.")] string Palette,
    [property: Description("The palette that was applied before this call; equal to Palette when the call was a no-op.")] string Previous);

/// <summary>
/// Mutating tool that sets the map-wide colour palette (Day / Dusk / Night).
/// Renderer-neutral: it reads the current <see cref="EncDotNet.S100.Datasets.Pipelines.MapPresentationState"/>,
/// swaps only the palette, and applies the result — so the same tool drives the
/// desktop viewer's live map and the headless CLI session.
/// </summary>
public sealed class SetPaletteTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "set_palette";

    private readonly ICapabilityAccessor<IPresentationController> _presentation;

    /// <summary>Creates the tool bound to a presentation-controller accessor.</summary>
    public SetPaletteTool(ICapabilityAccessor<IPresentationController> presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        _presentation = presentation;
    }

    /// <summary>
    /// Sets the map palette. Idempotent. Returns the previous palette so callers
    /// can detect no-ops and stitch repeated runs cleanly.
    /// </summary>
    public async Task<ToolResult<SetPaletteResult>> InvokeAsync(
        SetPaletteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Palette))
        {
            return ToolResult<SetPaletteResult>.Err(new InvalidArgument(
                "palette", "value is required; allowed values are 'Day', 'Dusk', 'Night'"));
        }

        // Reject pure-numeric input. Enum.TryParse accepts the underlying
        // integer (e.g. "0" -> Day), but the public contract is the string name
        // only; numeric coupling would be brittle.
        var trimmed = request.Palette.Trim();
        if (long.TryParse(trimmed, out _)
            || !Enum.TryParse<PaletteType>(trimmed, ignoreCase: true, out var palette)
            || !Enum.IsDefined(palette))
        {
            return ToolResult<SetPaletteResult>.Err(new InvalidArgument(
                "palette", $"value '{request.Palette}' is not one of: Day, Dusk, Night"));
        }

        var controller = _presentation.Current;
        if (controller is null)
        {
            return ToolResult<SetPaletteResult>.Err(
                new HostNotReady("the presentation controller is not attached yet"));
        }

        var state = controller.Current;
        var previous = state.Palette;
        await controller.SetPresentationAsync(state.WithPalette(palette), ct).ConfigureAwait(false);
        return ToolResult<SetPaletteResult>.Ok(
            new SetPaletteResult(palette.ToString(), previous.ToString()));
    }
}
