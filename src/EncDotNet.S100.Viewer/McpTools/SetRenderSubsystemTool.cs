using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Request payload for <see cref="SetRenderSubsystemTool"/>.</summary>
internal sealed record SetRenderSubsystemRequest(string Subsystem);

/// <summary>Result payload for <see cref="SetRenderSubsystemTool"/>.</summary>
internal sealed record SetRenderSubsystemResult(string Subsystem, string Previous);

/// <summary>
/// MCP tool that switches the live viewer's base-plane render subsystem
/// between "A" (<see cref="RenderSubsystemKind.Mapsui"/>) and "B"
/// (<see cref="RenderSubsystemKind.TiledScene"/>). The companion to
/// <c>set_palette</c> for scripted soak/measurement runs that need to
/// exercise the A↔B switch path — historically a source of native and
/// threading teardown bugs — from outside the GUI.
/// </summary>
internal sealed class SetRenderSubsystemTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "set_render_subsystem";

    private readonly IRenderStateControllerAccessor _accessor;

    public SetRenderSubsystemTool(IRenderStateControllerAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <summary>
    /// Sets the live render subsystem to the requested value. Idempotent.
    /// Returns the previous subsystem so callers can detect no-op
    /// invocations and stitch repeated runs cleanly. Refused with
    /// <see cref="RenderSubsystemPinned"/> when the subsystem is pinned by
    /// the <c>S100_RENDER_SUBSYSTEM</c> environment variable.
    /// </summary>
    public async Task<ToolResult<SetRenderSubsystemResult>> InvokeAsync(
        SetRenderSubsystemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Subsystem))
        {
            return ToolResult<SetRenderSubsystemResult>.Err(new InvalidArgument(
                "subsystem",
                "value is required; allowed values are 'Mapsui' (A) or 'TiledScene' (B)"));
        }

        // Reject pure-numeric input. Enum.TryParse accepts the underlying
        // integer (e.g. "0" → Mapsui), but the public contract here is the
        // string name only; numeric coupling would be brittle. See SetPaletteTool.
        var trimmed = request.Subsystem.Trim();
        if (long.TryParse(trimmed, out _)
            || !TryParseSubsystem(trimmed, out var subsystem))
        {
            return ToolResult<SetRenderSubsystemResult>.Err(new InvalidArgument(
                "subsystem",
                $"value '{request.Subsystem}' is not one of: Mapsui (A), TiledScene (B)"));
        }

        var controller = _accessor.Current;
        if (controller is null)
        {
            return ToolResult<SetRenderSubsystemResult>.Err(new MapNotReady(
                "the viewer's render-state controller has not been initialised yet"));
        }

        if (controller.RenderSubsystemPinned)
        {
            return ToolResult<SetRenderSubsystemResult>.Err(new RenderSubsystemPinned(
                "the S100_RENDER_SUBSYSTEM environment variable pins the subsystem at startup"));
        }

        var previous = controller.CurrentRenderSubsystem;
        await controller.SetRenderSubsystemAsync(subsystem, ct).ConfigureAwait(false);
        return ToolResult<SetRenderSubsystemResult>.Ok(
            new SetRenderSubsystemResult(subsystem.ToString(), previous.ToString()));
    }

    /// <summary>
    /// Parses a subsystem name, accepting the canonical enum names plus the
    /// "A"/"B" shorthand and the same aliases honoured by
    /// <c>S100_RENDER_SUBSYSTEM</c> (so a caller can use the env-var spelling).
    /// </summary>
    private static bool TryParseSubsystem(string value, out RenderSubsystemKind subsystem)
    {
        switch (value.ToLowerInvariant())
        {
            case "tiledscene" or "tiled" or "tile" or "b":
                subsystem = RenderSubsystemKind.TiledScene;
                return true;
            case "mapsui" or "a":
                subsystem = RenderSubsystemKind.Mapsui;
                return true;
            default:
                return Enum.TryParse(value, ignoreCase: true, out subsystem)
                    && Enum.IsDefined(subsystem);
        }
    }
}
