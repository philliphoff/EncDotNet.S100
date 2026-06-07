using System;
using System.Collections.Generic;
using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// A Mapsui-free feature tag attached to a built vector feature so the
/// S-98 cross-dataset suppression rules can filter without re-running
/// portrayal (R-101-102-B, S-98 Annex A §8.4.1). The Mapsui renderer
/// copies these onto each <c>IFeature</c> after building the layer.
/// </summary>
/// <param name="FeatureType">The S-100 Part 5 feature-type code.</param>
/// <param name="DepthContourValue">
/// For <c>DepthContour</c> features, the numeric VALDCO depth value
/// (preserves the safety-contour exception); otherwise <see langword="null"/>.
/// </param>
public readonly record struct VectorFeatureTag(string FeatureType, object? DepthContourValue);

/// <summary>
/// The Mapsui-free result of building a vector dataset's portrayal —
/// everything the Mapsui renderer needs to construct the dataset's
/// layers and place them in the S-98 cross-dataset stack, with no
/// Mapsui type crossing the seam.
/// </summary>
/// <remarks>
/// Produced under the processor's render gate as an immutable snapshot:
/// the resolver delegates close over a pre-warmed, palette-resolved asset
/// snapshot, and the instruction slices, palette, and geometry provider do
/// not read mutable portrayal-catalogue state. The result is therefore safe
/// to convert to Mapsui layers in another assembly.
/// </remarks>
public sealed class VectorPortrayalResult
{
    /// <summary>The vector sub-layers to build, in producer order.</summary>
    public required IReadOnlyList<VectorSubLayer> SubLayers { get; init; }

    /// <summary>The resolved colour palette (immutable snapshot).</summary>
    public required ColorPalette Palette { get; init; }

    /// <summary>The geometry provider resolving feature references to geometry.</summary>
    public required IFeatureGeometryProvider GeometryProvider { get; init; }

    /// <summary>The product specification name (e.g. <c>"S-101"</c>).</summary>
    public required string Product { get; init; }

    /// <summary>The product specification (name + edition) of the dataset.</summary>
    public required SpecRef Spec { get; init; }

    /// <summary>
    /// Stable identifier for the source dataset (typically the file name).
    /// Used as the S-98 stack tiebreaker and suppression join key.
    /// </summary>
    public required string SourceDatasetId { get; init; }

    /// <summary>Human-readable status line describing the dataset.</summary>
    public required string Info { get; init; }

    /// <summary>Symbol scale factor applied by the renderer.</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>Text scale factor applied by the renderer.</summary>
    public double TextScale { get; init; } = 1.0;

    /// <summary>
    /// Resolves an SVG symbol name to its SVG content (or null). Closes over
    /// the pre-warmed, palette-resolved symbol snapshot.
    /// </summary>
    public Func<string, string?>? SymbolProvider { get; init; }

    /// <summary>Resolves a line-style name to its resolved style (or null).</summary>
    public Func<string, LineStyle?>? LineStyleProvider { get; init; }

    /// <summary>Resolves an area-fill name to its resolved fill (or null).</summary>
    public Func<string, AreaFill?>? AreaFillProvider { get; init; }

    /// <summary>
    /// Optional stable per-sub-layer keys for the viewer's disclosure UI,
    /// parallel by index to <see cref="SubLayers"/>. When null the renderer
    /// derives them from each sub-layer's <see cref="VectorSubLayer.LayerKey"/>.
    /// </summary>
    public IReadOnlyList<string>? LayerNames { get; init; }

    /// <summary>
    /// S-101 feature tags keyed by feature id, copied onto built features so
    /// S-98 depth-feature suppression can run without re-portrayal. Null /
    /// empty for products that need no tagging.
    /// </summary>
    public IReadOnlyDictionary<long, VectorFeatureTag>? FeatureTags { get; init; }

    /// <summary>
    /// The most-permissive (largest) <c>DataCoverage.minimumDisplayScale</c>
    /// denominator across the cell, or null when no out-of-scale-band cap
    /// applies (e.g. the mariner's IgnoreScaleMinimum is set, or no usable
    /// value is present). The Mapsui renderer multiplies this by its
    /// denominator-to-resolution constant and clamps the styles of any
    /// sub-layer with <see cref="VectorSubLayer.ApplyOutOfBandCap"/> set.
    /// </summary>
    public int? OutOfBandMinDisplayScale { get; init; }

    /// <summary>
    /// Optional pre-padded geographic extent (lat / lon) that is used
    /// <em>verbatim</em> as the dataset's extent — taking precedence over the
    /// built layers' union. Products that compute their extent from raw feature
    /// geometry rather than the rendered layer (e.g. the GML XSLT products via
    /// <c>ComputeGeographicExtent</c>) set this. When null the renderer derives
    /// the extent from the built layers' union (falling back to
    /// <see cref="FallbackGeographicExtent"/> if that union is empty).
    /// </summary>
    public GeographicBounds? GeographicExtent { get; init; }

    /// <summary>
    /// Optional pre-padded geographic extent (lat / lon) used only as a
    /// <em>fallback</em> when the built layers' union is empty. Products that
    /// prefer the rendered layer's own extent but still need a padded geometry
    /// extent when the layer reports none (e.g. S-131, whose base behaviour was
    /// <c>layer.Extent ?? ComputeExtent()</c>) set this instead of
    /// <see cref="GeographicExtent"/>.
    /// </summary>
    public GeographicBounds? FallbackGeographicExtent { get; init; }
}
