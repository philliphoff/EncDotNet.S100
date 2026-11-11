using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>Request payload for <see cref="SetDisplayModeTool"/>.</summary>
public sealed record SetDisplayModeRequest(
    [property: Description("Display mode token: 'ice-concentration', 'ice-sod', or 'ice-navigational' (or the bare 'concentration'/'sod'/'navigational' aliases), or a raw S-411 mode id.")] string Mode,
    [property: Description("Product spec whose display mode is set; defaults to 'S-411' (the only product declaring selectable modes today).")] string? Spec = null);

/// <summary>Result payload for <see cref="SetDisplayModeTool"/>.</summary>
public sealed record SetDisplayModeResult(
    [property: Description("The product spec whose mode was set.")] string Spec,
    [property: Description("The spec-native display-mode id now applied, or null when cleared.")] string? Mode,
    [property: Description("The mode id applied before this call, or null when none was set.")] string? Previous,
    [property: Description("True when the applied mode is the provisional navigational preview.")] bool Provisional);

/// <summary>
/// Mutating tool that sets an explicit per-spec display mode (S-100 Part 9
/// §11.7). Only S-411 sea ice declares more than one mode today. Reads the
/// current presentation, updates only the per-spec entry in
/// <see cref="EcdisDisplaySettings.ActiveDisplayModes"/>, and applies the result.
/// </summary>
public sealed class SetDisplayModeTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "set_display_mode";

    private const string DefaultSpec = "S-411";

    private readonly ICapabilityAccessor<IPresentationController> _presentation;

    /// <summary>Creates the tool bound to a presentation-controller accessor.</summary>
    public SetDisplayModeTool(ICapabilityAccessor<IPresentationController> presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        _presentation = presentation;
    }

    /// <summary>
    /// Sets the per-spec display mode. Idempotent. Returns the previous mode id
    /// (if any) and whether the applied mode is the provisional navigational
    /// preview.
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
        // spec that declares none — matching the CLI, which hard-errors on
        // `--display-mode` for non-S-411 products.
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

        var controller = _presentation.Current;
        if (controller is null)
        {
            return ToolResult<SetDisplayModeResult>.Err(
                new HostNotReady("the presentation controller is not attached yet"));
        }

        var state = controller.Current;
        var previous = state.EcdisDisplay.ActiveDisplayModes.GetValueOrDefault(spec);

        var modes = new Dictionary<string, string?>(
            state.EcdisDisplay.ActiveDisplayModes, StringComparer.OrdinalIgnoreCase)
        {
            [spec] = modeId,
        };
        var updated = state.WithEcdisDisplay(
            state.EcdisDisplay with { ActiveDisplayModes = modes });

        await controller.SetPresentationAsync(updated, ct).ConfigureAwait(false);
        return ToolResult<SetDisplayModeResult>.Ok(
            new SetDisplayModeResult(spec, modeId, previous, S411DisplayModes.IsProvisional(modeId)));
    }

    /// <summary>
    /// Canonicalizes a product specifier that declares more than one selectable
    /// display mode. Tolerates the hyphen-less <c>S411</c> form and casing.
    /// Today only S-411 sea ice qualifies.
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

    /// <summary>Canonicalizes a recognised S-411 spec-native display-mode id.</summary>
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
            _ => null,
        };

        return modeId is not null;
    }
}
