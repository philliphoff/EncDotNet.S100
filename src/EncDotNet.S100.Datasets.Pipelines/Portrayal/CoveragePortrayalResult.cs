using System;
using System.Collections.Generic;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// Base class for the Mapsui-free coverage sub-layers carried in a
/// <see cref="CoveragePortrayalResult"/>. Each variant carries the S-98
/// stack metadata plus the payload the Mapsui renderer needs to build one
/// <c>ILayer</c>.
/// </summary>
public abstract class CoverageSubLayerBase
{
    /// <summary>Stable sub-layer key (e.g. <c>"s111.arrows"</c>) for the viewer's disclosure UI.</summary>
    public required string LayerKey { get; init; }

    /// <summary>The Mapsui layer's display name.</summary>
    public required string LayerName { get; init; }

    /// <summary>The S-98 display plane this sub-layer lives in.</summary>
    public required S98DisplayPlane Plane { get; init; }

    /// <summary>Intra-plane ordering hint, ascending — lower draws first.</summary>
    public int WithinPlanePriority { get; init; }

    /// <summary>Optional feature-type code for S-98 rules; null for whole-layer entries.</summary>
    public string? SourceFeatureType { get; init; }
}

/// <summary>
/// A colour-band coverage sub-layer (S-102 bathymetry, S-104 water-level
/// surface): the Mapsui renderer rasterises <see cref="Coverage"/> through
/// <c>MapsuiCoverageRenderer</c>.
/// </summary>
public sealed class GridCoverageSubLayer : CoverageSubLayerBase
{
    /// <summary>The materialised, time-resolved styled coverage layer.</summary>
    public required StyledCoverageLayer Coverage { get; init; }

    /// <summary>The viewport (extent + grid dimensions) the renderer fits to.</summary>
    public required Viewport Viewport { get; init; }
}

/// <summary>
/// An arrow-overlay coverage sub-layer (S-111 surface currents): the Mapsui
/// renderer rasterises <see cref="Coverage"/> through
/// <c>MapsuiCoverageArrowRenderer</c>. The arrow renderer may legitimately
/// produce no layer (empty grid), in which case the renderer drops this
/// sub-layer and falls back to <see cref="FallbackExtent"/> for the extent.
/// </summary>
public sealed class ArrowCoverageSubLayer : CoverageSubLayerBase
{
    /// <summary>The materialised, time-resolved styled coverage layer.</summary>
    public required StyledCoverageLayer Coverage { get; init; }

    /// <summary>The viewport (extent + grid dimensions) the renderer fits to.</summary>
    public required Viewport Viewport { get; init; }

    /// <summary>The active palette used to colour the arrows.</summary>
    public ColorPalette? Palette { get; init; }

    /// <summary>Base symbol scale applied to each arrow glyph.</summary>
    public double BaseSymbolScale { get; init; } = 1.0;

    /// <summary>Resolves a symbol name to its SVG content (pre-warmed snapshot).</summary>
    public required Func<string, string?> SymbolProvider { get; init; }

    /// <summary>
    /// Geographic extent fallback (lat / lon) used when the arrow renderer
    /// produces no layer.
    /// </summary>
    public required GeographicBounds FallbackExtent { get; init; }
}

/// <summary>
/// A station / node point-glyph sub-layer (S-104 / S-111 fixed-station and
/// ungeorectified-grid datasets). The Mapsui renderer builds a memory layer
/// of point features from the pre-projected <see cref="Glyphs"/>.
/// </summary>
public sealed class GlyphCoverageSubLayer : CoverageSubLayerBase
{
    /// <summary>The pre-projected (EPSG:3857) point glyphs to build.</summary>
    public required IReadOnlyList<PointGlyph> Glyphs { get; init; }

    /// <summary>
    /// The EPSG:3857 extent of the glyphs, or null when there are no glyphs.
    /// </summary>
    public MercatorBounds? Extent { get; init; }
}

/// <summary>
/// The symbol shape a <see cref="PointGlyph"/> is drawn with.
/// </summary>
public enum PointGlyphSymbol
{
    /// <summary>Filled ellipse with an outline (S-104 water-level stations).</summary>
    Ellipse,

    /// <summary>Filled triangle with an outline (S-111 station arrow fallback).</summary>
    Triangle,

    /// <summary>SVG image symbol (S-111 PC arrow symbol).</summary>
    Svg,
}

/// <summary>
/// A single Mapsui-free point glyph (a station or grid-node symbol), carrying
/// the pre-projected position, the final style parameters, and the pick
/// attributes the Mapsui renderer stamps onto the built feature.
/// </summary>
public sealed class PointGlyph
{
    /// <summary>Projected easting (EPSG:3857 metres).</summary>
    public required double MercatorX { get; init; }

    /// <summary>Projected northing (EPSG:3857 metres).</summary>
    public required double MercatorY { get; init; }

    /// <summary>
    /// The feature-reference tag stamped under the renderer's feature-ref key
    /// so the viewer's pick service can route a click back to the processor.
    /// </summary>
    public required string FeatureRefTag { get; init; }

    /// <summary>Pick attributes copied onto the built feature.</summary>
    public required IReadOnlyDictionary<string, object> Attributes { get; init; }

    /// <summary>The symbol shape to draw.</summary>
    public required PointGlyphSymbol Symbol { get; init; }

    /// <summary>
    /// SVG content to render as an image style; required when
    /// <see cref="Symbol"/> is <see cref="PointGlyphSymbol.Svg"/>, ignored
    /// otherwise.
    /// </summary>
    public string? SvgSource { get; init; }

    /// <summary>
    /// Fill colour for the <see cref="PointGlyphSymbol.Ellipse"/> /
    /// <see cref="PointGlyphSymbol.Triangle"/> shapes.
    /// </summary>
    public RgbaColor FillColor { get; init; }

    /// <summary>
    /// Outline colour for the <see cref="PointGlyphSymbol.Ellipse"/> /
    /// <see cref="PointGlyphSymbol.Triangle"/> shapes.
    /// </summary>
    public RgbaColor OutlineColor { get; init; }

    /// <summary>Outline width (Mapsui pen units) for ellipse / triangle shapes.</summary>
    public double OutlineWidth { get; init; } = 1.0;

    /// <summary>The final symbol scale to apply to the style.</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>The final symbol rotation (degrees) to apply to the style.</summary>
    public double Rotation { get; init; }
}

/// <summary>
/// The Mapsui-free result of building a coverage dataset's portrayal.
/// </summary>
public sealed class CoveragePortrayalResult
{
    /// <summary>The coverage sub-layers to build, in producer order.</summary>
    public required IReadOnlyList<CoverageSubLayerBase> SubLayers { get; init; }

    /// <summary>The product specification (name + edition) of the dataset.</summary>
    public required EncDotNet.S100.Core.SpecRef Spec { get; init; }

    /// <summary>Stable identifier for the source dataset (typically the file name).</summary>
    public required string SourceDatasetId { get; init; }

    /// <summary>Human-readable status line describing the dataset.</summary>
    public required string Info { get; init; }

    /// <summary>
    /// Optional stable per-sub-layer keys for the viewer's disclosure UI.
    /// When null the renderer derives them from each sub-layer's key.
    /// </summary>
    public IReadOnlyList<string>? LayerNames { get; init; }
}
