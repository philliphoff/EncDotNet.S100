using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Request payload for <see cref="SetDisplayModeTool"/>.</summary>
internal sealed record SetDisplayModeRequest(string Mode, string? Spec);

/// <summary>Result payload for <see cref="SetDisplayModeTool"/>.</summary>
internal sealed record SetDisplayModeResult(string Spec, string? Mode, string? Previous, bool Provisional);

/// <summary>
/// MCP tool that mutates the live viewer's explicit per-spec display
/// mode (S-100 Part 9 §11.7). Currently only S-411 sea ice declares more
/// than one mode: total <c>concentration</c> (default), stage of
/// development (<c>sod</c>), or the provisional <c>navigational</c>
/// preview. Accepts the same friendly tokens as the CLI
/// <c>render --display-mode</c> flag, plus raw spec-native mode ids.
/// </summary>
internal sealed class SetDisplayModeTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "set_display_mode";

    private const string DefaultSpec = "S-411";

    private readonly IRenderStateControllerAccessor _accessor;

    public SetDisplayModeTool(IRenderStateControllerAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <summary>
    /// Sets the live per-spec display mode. Idempotent. Returns the
    /// previous mode id (if any) and whether the applied mode is the
    /// provisional navigational preview.
    /// </summary>
    public async Task<ToolResult<SetDisplayModeResult>> InvokeAsync(
        SetDisplayModeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Mode))
        {
            return ToolResult<SetDisplayModeResult>.Err(new InvalidArgument(
                "mode",
                "value is required; allowed tokens are 'ice-concentration', 'ice-sod', 'ice-navigational' "
                + "(or the bare 'concentration' / 'sod' / 'navigational' aliases)"));
        }

        var rawSpec = string.IsNullOrWhiteSpace(request.Spec) ? DefaultSpec : request.Spec.Trim();
        var raw = request.Mode.Trim();

        // The friendly mode tokens are S-411-specific and S-411 is the only
        // product that declares more than one display mode today, so reject a
        // spec that declares none — keeping this path consistent with the CLI,
        // which hard-errors on `--display-mode` for non-S-411 products.
        if (!TryCanonicalizeSpec(rawSpec, out var spec))
        {
            return ToolResult<SetDisplayModeResult>.Err(new InvalidArgument(
                "spec",
                $"product '{rawSpec}' declares no selectable display modes; only 'S-411' does today"));
        }

        // Prefer the shared friendly-token map; fall back to a recognised
        // spec-native id so callers may pass either form.
        string? modeId;
        if (S411DisplayModes.TryParseToken(raw, out var parsed) && parsed is not null)
        {
            modeId = parsed;
        }
        else if (TryCanonicalizeModeId(raw, out var canonicalModeId))
        {
            modeId = canonicalModeId;
        }
        else
        {
            return ToolResult<SetDisplayModeResult>.Err(new InvalidArgument(
                "mode",
                $"value '{request.Mode}' is not one of: 'ice-concentration', 'ice-sod', 'ice-navigational' "
                + "(or the bare 'concentration' / 'sod' / 'navigational' aliases), "
                + "or a raw S-411 mode id (case-insensitive): "
                + $"'{S411DisplayModes.ConcentrationModeId}', "
                + $"'{S411DisplayModes.StageOfDevelopmentModeId}', "
                + $"'{S411DisplayModes.NavigationalModeId}'"));
        }

        var controller = _accessor.Current;
        if (controller is null)
        {
            return ToolResult<SetDisplayModeResult>.Err(new MapNotReady(
                "the viewer's render-state controller has not been initialised yet"));
        }

        var previous = controller.GetDisplayMode(spec);
        await controller.SetDisplayModeAsync(spec, modeId, ct).ConfigureAwait(false);
        return ToolResult<SetDisplayModeResult>.Ok(
            new SetDisplayModeResult(spec, modeId, previous, S411DisplayModes.IsProvisional(modeId)));
    }

    /// <summary>
    /// Canonicalizes a product specifier that declares more than one
    /// selectable display mode. Tolerates the hyphen-less <c>S411</c> form and
    /// casing. Today only S-411 sea ice qualifies.
    /// </summary>
    private static bool TryCanonicalizeSpec(string spec, out string canonicalSpec)
    {
        var normalized = spec.Replace("-", string.Empty).Trim();
        if (normalized.Equals("S411", StringComparison.OrdinalIgnoreCase))
        {
            canonicalSpec = DefaultSpec;
            return true;
        }

        canonicalSpec = string.Empty;
        return false;
    }

    /// <summary>
    /// Canonicalizes a recognised S-411 spec-native display-mode id.
    /// </summary>
    private static bool TryCanonicalizeModeId(string raw, out string? modeId)
    {
        modeId = raw switch
        {
            var value when value.Equals(S411DisplayModes.ConcentrationModeId, StringComparison.OrdinalIgnoreCase)
                => S411DisplayModes.ConcentrationModeId,
            var value when value.Equals(S411DisplayModes.StageOfDevelopmentModeId, StringComparison.OrdinalIgnoreCase)
                => S411DisplayModes.StageOfDevelopmentModeId,
            var value when value.Equals(S411DisplayModes.NavigationalModeId, StringComparison.OrdinalIgnoreCase)
                => S411DisplayModes.NavigationalModeId,
            _ => null
        };

        return modeId is not null;
    }
}
