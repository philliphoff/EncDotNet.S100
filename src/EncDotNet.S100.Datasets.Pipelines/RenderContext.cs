using System;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Base class for spec-specific render contexts passed to dataset processors.
/// </summary>
public abstract record RenderContext
{
    /// <summary>The color palette (Day/Dusk/Night) to use for rendering.</summary>
    public PaletteType Palette { get; init; } = PaletteType.Day;

    /// <summary>Global symbol scale factor (1.0 = default).</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>Global text scale factor (1.0 = default).</summary>
    public double TextScale { get; init; } = 1.0;

    /// <summary>
    /// Cross-spec ECDIS display settings (S-100 Part 9 §11.7
    /// display-mode selection plus per-spec viewing-group overrides).
    /// When <c>null</c> processors render with no mode filter and no
    /// user overrides — equivalent to "All" with an empty hidden set.
    /// </summary>
    public EcdisDisplaySettings? EcdisDisplay { get; init; }

    /// <summary>
    /// Mariner-configurable display preferences (S-100 Part 9 §4.2 —
    /// safety/shallow/deep contours, S-101 boolean toggles, depth display
    /// unit, etc.). When <c>null</c> processors fall back to
    /// <see cref="MarinerSettings.Default"/>. Only consumed by processors
    /// whose portrayal pipeline honours mariner selections (S-101, S-102,
    /// S-104, S-111 today); other processors carry it transparently for
    /// future use.
    /// </summary>
    public MarinerSettings? Mariner { get; init; }

    /// <summary>
    /// Bitmask of <see cref="DrawingInstruction"/> categories to suppress from
    /// the rendered output (S-100 Part 9 instruction types — areas, lines,
    /// points, text). Hiding text is the canonical use case: at preview
    /// scales, label-dense products such as S-411 sea-ice draw the full
    /// "egg-code" labels which obscure the underlying fills, and a flag-driven
    /// suppression delivers a BSIS-style "clean fill" preview without
    /// re-running the portrayal pipeline. Defaults to
    /// <see cref="DrawingInstructionCategory.None"/> (render everything).
    /// </summary>
    /// <remarks>
    /// Honoured by the headless render path
    /// (<c>HeadlessVectorRenderer.Render</c>); the Mapsui path is not yet
    /// affected. The filter is applied after the portrayal pipeline produces
    /// the display list, so suppressing one category never re-flows the
    /// remaining instructions (areas → lines → points → text ordering and
    /// scale visibility are preserved).
    /// </remarks>
    public DrawingInstructionCategory HiddenInstructionCategories { get; init; }
        = DrawingInstructionCategory.None;

    /// <summary>
    /// Basemap drawn beneath the chart data in the headless render path (issue
    /// #411). When <see cref="BasemapKind.Offline"/>, the bundled Natural Earth
    /// 1:10m land layer is composited under the dataset, projected with the
    /// dataset's own auto-fitted viewport so it registers exactly. Defaults to
    /// <see cref="BasemapKind.None"/> (no basemap; output unchanged).
    /// </summary>
    /// <remarks>
    /// Honoured by the headless render path (<c>HeadlessVectorRenderer.Render</c>
    /// for vector products and <c>CoverageHeadlessRenderer.Render</c> for
    /// coverage products); the Mapsui viewer path selects its basemap
    /// independently via <c>BasemapMode</c>.
    /// </remarks>
    public BasemapKind Basemap { get; init; } = BasemapKind.None;

    /// <summary>
    /// The S-100 Part 9 §11.7 display-mode id to activate for this render,
    /// or <c>null</c> to leave the catalogue's current/default mode in place.
    /// Unlike <see cref="EcdisDisplay"/>'s ECDIS category (which maps to a
    /// display mode only for specs that declare the canonical DisplayBase /
    /// StandardDisplay / OtherInformation ids), this is an explicit,
    /// spec-native mode id — used by products whose modes select a
    /// <em>portrayal</em> rather than a viewing-group filter. S-411 uses it to
    /// choose between its concentration
    /// (<c>IceScientificIceactDisplayMode</c>), stage-of-development
    /// (<c>IceScientificIcesodDisplayMode</c>) and navigational
    /// (<c>IceNavigationalDisplayMode</c>) portrayals. When set, it wins over
    /// the ECDIS-category-derived mode. Ignored by processors whose catalogue
    /// does not declare the id.
    /// </summary>
    public string? DisplayModeId { get; init; }
}

public sealed record S101RenderContext : RenderContext;

public sealed record S102RenderContext : RenderContext;

public sealed record S111RenderContext(DateTime? TimeStep = null) : RenderContext;

public sealed record S104RenderContext(DateTime? TimeStep = null) : RenderContext;

public sealed record S122RenderContext : RenderContext;

public sealed record S124RenderContext : RenderContext;

public sealed record S125RenderContext : RenderContext;
public sealed record S127RenderContext : RenderContext;

public sealed record S129RenderContext : RenderContext;

public sealed record S201RenderContext : RenderContext;

public sealed record S411RenderContext(DateTime? TimeStep = null) : RenderContext;
