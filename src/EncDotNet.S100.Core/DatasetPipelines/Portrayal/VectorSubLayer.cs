using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// One Mapsui-free vector sub-layer in a <see cref="VectorPortrayalResult"/>.
/// Carries a slice of the dataset's drawing instructions plus the S-98
/// stack metadata the Mapsui renderer needs to build a single layer and
/// place it in the cross-dataset paint order.
/// </summary>
/// <remarks>
/// S-101 emits two sub-layers — area fills and line work — so an S-102
/// bathymetric surface can interleave between them (S-98 Annex A §A-6.9.1).
/// GML products and S-57 emit a single sub-layer.
/// </remarks>
public sealed class VectorSubLayer
{
    /// <summary>
    /// Stable sub-layer key used by the viewer to preserve per-sub-layer
    /// visibility / opacity across re-renders (e.g. <c>"s101.areas"</c>,
    /// <c>"s101.linework"</c>). This is the value placed into
    /// <c>MapsuiDatasetResult.LayerNames</c>; it is NOT the Mapsui layer's
    /// display name.
    /// </summary>
    public required string LayerKey { get; init; }

    /// <summary>The Mapsui layer's display name (used for diagnostics / UI).</summary>
    public required string LayerName { get; init; }

    /// <summary>The drawing instructions to rasterise into this sub-layer.</summary>
    public required IReadOnlyList<DrawingInstruction> Instructions { get; init; }

    /// <summary>The S-98 display plane this sub-layer lives in.</summary>
    public required S98DisplayPlane Plane { get; init; }

    /// <summary>Intra-plane ordering hint, ascending — lower draws first.</summary>
    public int WithinPlanePriority { get; init; }

    /// <summary>
    /// Optional feature-type code when this entry represents a
    /// per-feature-type slice (e.g. the S-101 area / line split). Null for
    /// whole-layer entries. Consumed by S-98 suppression rules.
    /// </summary>
    public string? SourceFeatureType { get; init; }

    /// <summary>
    /// Mapsui-free pattern-fill priority-clip cache identity key, or null
    /// when this sub-layer carries no pattern fills (e.g. S-101 line work).
    /// The Mapsui renderer appends its own algorithm / format qualifiers to
    /// form the final cache key, so this value never references a Mapsui
    /// constant.
    /// </summary>
    public string? PatternClipCacheKey { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the Mapsui renderer caps this
    /// sub-layer's maximum visible resolution to the out-of-scale-band
    /// cutoff derived from <see cref="VectorPortrayalResult.OutOfBandMinDisplayScale"/>
    /// (S-101 detail declutter). Applied only to the line-work sub-layer.
    /// </summary>
    public bool ApplyOutOfBandCap { get; init; }
}
