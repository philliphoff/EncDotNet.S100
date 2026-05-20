namespace EncDotNet.S100.Interoperability;

/// <summary>
/// The cross-dataset display plane each rendered layer is assigned to,
/// per the S-98 ECDIS and Interoperability Specification (Edition
/// 2.0.0, October 2025).
/// </summary>
/// <remarks>
/// <para>
/// S-98 widens S-100 Part 9 §11.6's intra-product two-plane model
/// (<c>UnderRadar</c> / <c>OverRadar</c>, see
/// <see cref="EncDotNet.S100.Pipelines.Vector.DisplayPlane"/>) with
/// catalogue-declarable additional planes (S-98 Annex A §A-3.2.1.1).
/// For the v1 plumbing PR (PR-L1) we hard-wire the nine canonical
/// planes derived from MSC.530(106)/Rev.1 §Appendix 2 "priority of
/// information" via S-98 Main §9.2.1; an
/// <see cref="LayerStackEntry.ExtensionId"/> escape hatch on
/// <c>LayerStackEntry</c> carries IC-declared non-canonical plane ids
/// for the future PR-L2 rule evaluator.
/// </para>
/// <para>
/// The numeric ordering is informative — lower values draw earlier
/// (farther back) than higher values. Numeric gaps (10 between
/// canonical values) leave headroom for IC-declared planes whose
/// <c>order</c> attribute lands between two canonical positions.
/// </para>
/// <para>
/// The intra-product <see cref="EncDotNet.S100.Pipelines.Vector.DisplayPlane"/>
/// enum is unchanged — it still controls UnderRadar / OverRadar
/// ordering within a single product's display list. The two enums
/// collaborate: a given drawing instruction's intra-product plane
/// stays UnderRadar / OverRadar; the rendered <em>layer</em> the
/// instruction lives in is assigned an outer
/// <see cref="S98DisplayPlane"/> by the processor.
/// </para>
/// </remarks>
public enum S98DisplayPlane
{
    /// <summary>
    /// Base-chart skin-of-the-earth fills (S-101 area / colour-fill
    /// features; S-57 fallback). MSC.530(106)/Rev.1 §Appendix 2
    /// "priority of information" layer 5 ("Official colour-fill area
    /// data"); S-98 Main §9.2.1 layer 5.
    /// </summary>
    BaseChartUnder = 0,

    /// <summary>
    /// Gridded bathymetry surfaces (S-102). S-98 Annex A §A-6.9.1
    /// "high definition gridded bathymetry replaces (overwrites)
    /// depth area and depth contours, but soundings, aids to
    /// navigation, and obstructions are over the high definition
    /// bathymetry (interoperability Level 1)".
    /// </summary>
    Bathymetry = 10,

    /// <summary>
    /// Official on-demand coverage surfaces — water-level (S-104),
    /// surface-current colour band (S-111), under-keel clearance
    /// (S-129). MSC.530(106)/Rev.1 §Appendix 2 layer 6 "Official
    /// on demand data"; S-98 Main §9.2.1 layer 6. PR-L0 TBD-2
    /// resolved: surface sits <em>under</em> ENC line work.
    /// </summary>
    OnDemandSurface = 20,

    /// <summary>
    /// Base-chart line work, points, symbols, and text (S-101
    /// curves / points / text; S-57 fallback). MSC.530(106)/Rev.1
    /// §Appendix 2 layer 2 "Official data: points/curves and
    /// surfaces"; S-98 Main §9.2.1 layer 2.
    /// </summary>
    BaseChartOver = 30,

    /// <summary>
    /// Catch-all official overlays that don't have a dedicated plane
    /// in S-98 v2.0.0 — S-122, S-125, S-127, S-128, S-131, S-201,
    /// S-411, S-421, and station-glyph sub-layers for S-104 / S-111
    /// (PR-I / PR-J). Derived from MSC.530(106)/Rev.1 §Appendix 2
    /// layer 6 catch-all reading.
    /// </summary>
    OtherChartOverlays = 40,

    /// <summary>
    /// Navigational warnings and notices to mariners (S-124).
    /// MSC.530(106)/Rev.1 §Appendix 2 layers 3-4 ("Notices to
    /// Mariners…", "Official-caution"); S-98 Main §9.2.1 layers
    /// 3-4.
    /// </summary>
    CautionsAndWarnings = 50,

    /// <summary>
    /// Dynamic vector overlays drawn above the cautions plane —
    /// S-111 arrow sub-layer (the catalogue marks its instructions
    /// with intra-product <c>displayPlane id="OverRadar"</c>) and
    /// any future dynamic glyph layers.
    /// </summary>
    DynamicArrows = 60,

    /// <summary>
    /// Mariner's own annotations (future). MSC.530(106)/Rev.1
    /// §Appendix 2 layers 8-9. Reserved for PR-L3+ scope; no
    /// processor emits onto this plane today.
    /// </summary>
    MarinerOverlay = 70,

    /// <summary>
    /// ECDIS visual alerts / indications (overscale, caution, AIO).
    /// MSC.530(106)/Rev.1 §Appendix 2 layer 1; S-98 Main §9.2.1
    /// layer 1. Reserved for PR-L3+ scope.
    /// </summary>
    EcdisAlerts = 80,
}
