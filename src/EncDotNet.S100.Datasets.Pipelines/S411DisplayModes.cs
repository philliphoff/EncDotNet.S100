namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Shared registry of the three S-411 sea-ice portrayal display modes
/// (S-411 Ed 1.2.1; PC Ed 2.0.0, S-100 Part 9 §11.7). A single S-411
/// dataset carries the full WMO egg code per polygon, so the same data can
/// be portrayed as total concentration, stage of development, or a
/// provisional navigational preview. This helper is the single source of
/// truth for the spec-native mode ids, their friendly CLI tokens, and the
/// provisional marker, so the CLI (<c>render --display-mode</c> /
/// <c>info</c>) and the Avalonia viewer selector agree on the mapping.
/// </summary>
/// <remarks>
/// Friendly, localized <em>display</em> labels are intentionally not defined
/// here — the viewer resolves them through its localized resources. This
/// helper only carries stable, culture-invariant identifiers and tokens.
/// </remarks>
public static class S411DisplayModes
{
    /// <summary>
    /// Total-concentration mode id (WMO <c>iceact</c> colour ramp). The
    /// S-411 default look.
    /// </summary>
    public const string ConcentrationModeId = "IceScientificIceactDisplayMode";

    /// <summary>
    /// Stage-of-development mode id (WMO <c>icesod</c> colour ramp).
    /// </summary>
    public const string StageOfDevelopmentModeId = "IceScientificIcesodDisplayMode";

    /// <summary>
    /// Navigational mode id. <b>Provisional</b>: a concentration-derived
    /// preview, <em>not</em> a POLARIS/RIO navigational-risk computation.
    /// </summary>
    public const string NavigationalModeId = "IceNavigationalDisplayMode";

    /// <summary>
    /// The mode activated when no explicit selection is made
    /// (<see cref="ConcentrationModeId"/>).
    /// </summary>
    public const string DefaultModeId = ConcentrationModeId;

    /// <summary>
    /// Parses a case-insensitive <c>render --display-mode</c> token into its
    /// spec-native mode id. Accepts the canonical
    /// <c>ice-concentration</c> / <c>ice-sod</c> / <c>ice-navigational</c>
    /// tokens plus the bare <c>concentration</c> / <c>sod</c> /
    /// <c>navigational</c> aliases. A <c>null</c>/whitespace value resolves to
    /// <see langword="null"/> (meaning "use each catalogue's default mode")
    /// and still returns <see langword="true"/>. Returns <see langword="false"/>
    /// only for a non-empty, unrecognised token.
    /// </summary>
    /// <param name="token">The CLI token (or <c>null</c>).</param>
    /// <param name="modeId">The resolved spec-native mode id, or <c>null</c>.</param>
    /// <returns><see langword="true"/> when the token is empty or recognised.</returns>
    public static bool TryParseToken(string? token, out string? modeId)
    {
        modeId = null;
        if (string.IsNullOrWhiteSpace(token))
            return true;

        switch (token.Trim().ToLowerInvariant())
        {
            case "ice-concentration":
            case "concentration":
                modeId = ConcentrationModeId;
                return true;
            case "ice-sod":
            case "sod":
                modeId = StageOfDevelopmentModeId;
                return true;
            case "ice-navigational":
            case "navigational":
                modeId = NavigationalModeId;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Maps a spec-native display-mode id to its friendly
    /// <c>render --display-mode</c> token, falling back to the raw id for
    /// modes with no CLI alias.
    /// </summary>
    /// <param name="modeId">The spec-native mode id.</param>
    /// <returns>The CLI token, or the raw id when unmapped.</returns>
    public static string ToCliToken(string modeId)
    {
        ArgumentNullException.ThrowIfNull(modeId);
        return modeId switch
        {
            ConcentrationModeId => "ice-concentration",
            StageOfDevelopmentModeId => "ice-sod",
            NavigationalModeId => "ice-navigational",
            _ => modeId,
        };
    }

    /// <summary>
    /// Whether the given mode id is the provisional navigational preview,
    /// which must be labelled as such in user-facing surfaces (it is not a
    /// POLARIS/RIO risk product).
    /// </summary>
    /// <param name="modeId">The spec-native mode id.</param>
    /// <returns><see langword="true"/> for the navigational mode.</returns>
    public static bool IsProvisional(string? modeId) =>
        string.Equals(modeId, NavigationalModeId, StringComparison.OrdinalIgnoreCase);
}
