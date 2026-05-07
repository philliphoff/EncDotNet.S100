using System;
using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The four ECDIS standard display categories (S-100 Part 9 §11.7,
/// S-101 §10.4). The viewer exposes these as a cross-spec selection
/// and <see cref="EcdisCategoryMapper"/> resolves each to a
/// per-spec display-mode id.
/// </summary>
public enum EcdisDisplayCategory
{
    /// <summary>
    /// "Display Base" — the minimum information required for safe
    /// navigation that cannot be removed by the mariner.
    /// </summary>
    DisplayBase,

    /// <summary>
    /// "Standard Display" — the default ECDIS display.
    /// </summary>
    Standard,

    /// <summary>
    /// "Other Information" — Standard plus the producer-tagged
    /// supplementary content. Subset of "All".
    /// </summary>
    OtherInformation,

    /// <summary>
    /// "All" — every viewing group is visible (no display-mode filter).
    /// </summary>
    All,
}

/// <summary>
/// Cross-spec ECDIS display settings carried on every
/// <see cref="RenderContext"/>. Producers (the viewer) populate this
/// from <c>EcdisDisplayState</c>; consumers (per-spec processors)
/// apply it to their <see cref="IVectorPortrayalCatalogue"/> via
/// <see cref="EcdisDisplayExtensions.ApplyTo"/>.
/// </summary>
public sealed record EcdisDisplaySettings
{
    /// <summary>The active ECDIS category. Defaults to Standard.</summary>
    public EcdisDisplayCategory Category { get; init; } = EcdisDisplayCategory.Standard;

    /// <summary>
    /// Per-spec viewing-group ids the user has explicitly hidden via
    /// the ECDIS panel. Keys are spec codes (e.g. <c>"S-101"</c>).
    /// Empty by default.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<int>> HiddenViewingGroups { get; init; }
        = new Dictionary<string, IReadOnlySet<int>>();
}

/// <summary>
/// Maps a cross-spec <see cref="EcdisDisplayCategory"/> to the
/// per-spec <c>displayMode</c> id declared in that spec's bundled
/// portrayal catalogue. Specs that only declare a single mode
/// (typically just <c>StandardDisplay</c>) fall back to <c>null</c>
/// for non-Standard categories — meaning "no mode filter, render
/// everything", matching the bundled catalogue's authoring intent.
/// </summary>
public static class EcdisCategoryMapper
{
    /// <summary>
    /// Resolves the ECDIS category for a given spec to a
    /// <see cref="DisplayModeController.ActiveDisplayModeId"/> value.
    /// Returns <c>null</c> for "All" (no filter) or for specs that
    /// don't declare per-category modes.
    /// </summary>
    /// <param name="productSpec">Spec code, e.g. <c>"S-101"</c>.</param>
    /// <param name="category">Cross-spec ECDIS category.</param>
    /// <param name="availableModeIds">
    /// Display-mode ids declared by the spec's portrayal catalogue.
    /// </param>
    public static string? Map(
        string productSpec,
        EcdisDisplayCategory category,
        IReadOnlySet<string> availableModeIds)
    {
        ArgumentNullException.ThrowIfNull(productSpec);
        ArgumentNullException.ThrowIfNull(availableModeIds);

        if (category == EcdisDisplayCategory.All)
            return null;

        // Canonical S-101 ids are well-known; other specs reuse the
        // same names where they declare them.
        var preferred = category switch
        {
            EcdisDisplayCategory.DisplayBase => "DisplayBase",
            EcdisDisplayCategory.Standard => "StandardDisplay",
            EcdisDisplayCategory.OtherInformation => "OtherInformation",
            _ => null,
        };

        if (preferred is not null && availableModeIds.Contains(preferred))
            return preferred;

        // Fallback: spec only declares Standard (or fewer); render
        // everything for non-Standard requests.
        return null;
    }
}

/// <summary>
/// Extensions that apply <see cref="EcdisDisplaySettings"/> to a
/// freshly-constructed per-spec vector portrayal catalogue. Called
/// by every vector dataset processor immediately after it builds
/// the catalogue and before <c>VectorPipeline</c> consumes it.
/// </summary>
public static class EcdisDisplayExtensions
{
    /// <summary>
    /// Applies the supplied settings to <paramref name="catalogue"/>:
    /// resolves the ECDIS category to the spec's display-mode id and
    /// activates it; then writes per-spec hidden-VG ids as user-off
    /// overrides. Idempotent — calling on a fresh catalogue always
    /// produces the same effective visibility for a given settings
    /// value.
    /// </summary>
    public static void ApplyTo(this EcdisDisplaySettings settings, IVectorPortrayalCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogue);

        var modeId = EcdisCategoryMapper.Map(
            catalogue.ProductSpec, settings.Category, catalogue.DisplayModes.DeclaredModeIds);
        catalogue.DisplayModes.SetActive(modeId);

        if (settings.HiddenViewingGroups.TryGetValue(catalogue.ProductSpec, out var hidden))
        {
            foreach (var vg in hidden)
                catalogue.ViewingGroups.SetUserOverride(vg, false);
        }
    }
}
