using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;

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

    /// <summary>
    /// Whether the sub-layer is drawn until the user changes it. Defaults to
    /// <see langword="true"/>. A processor can offer an optional layer, such
    /// as the weather pressure isobars, with <see langword="false"/>: it is
    /// still built and listed (a viewer's layer stack shows it, with a
    /// visibility toggle), but a viewer starts it hidden
    /// (<see cref="MapDatasetSubLayer.IsVisible"/>) and the headless
    /// compositor leaves it out unless
    /// <c>HeadlessCompositeOptions.ShownLayerKeys</c> names its
    /// <see cref="LayerKey"/> (issue #1072).
    /// </summary>
    public bool IsVisibleByDefault { get; init; } = true;
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

    /// <summary>
    /// Optional land-area surface geometries (WGS84 <c>(latitude, longitude)</c>)
    /// this surface must be clipped <em>out of</em> so it only covers water. The
    /// renderers project these polygons into destination pixel space and clip the
    /// rasterised surface against them at output-pixel resolution (see
    /// <c>CoverageLandClip</c>). Set by the S-98 rule that clips the S-104
    /// water-level surface to water when an S-101 ENC is loaded alongside it
    /// (issue #483; S-98 Ed.2.0.0 Annex A §A-6.9.1).
    /// <see langword="null"/> or empty means "draw everywhere the grid has data".
    /// </summary>
    public IReadOnlyList<FeatureGeometry>? LandAreaMask { get; init; }

    /// <summary>
    /// The portrayal opacity of the surface, 0 (invisible) to 1 (opaque).
    /// Defaults to <c>1.0</c>. Renderers multiply it with any user opacity
    /// rather than replacing it. The S-104 water-level band is partly
    /// transparent so the ENC colour fills under it stay readable: S-98 Main
    /// §9.2.1 says on-demand data must not obscure official colour-fill data,
    /// and Annex A §4.4.1 allows overlays to use transparency for that. S-102
    /// stays opaque because it replaces the ENC depth areas (Part A §A-6.9.1).
    /// </summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>
    /// Whether this surface asks to be clipped to water when an S-101 ENC is
    /// loaded alongside it. The S-98 rule <c>R-101-WX-B</c> attaches the ENC's
    /// land geometry as <see cref="LandAreaMask"/> to every sub-layer that sets
    /// this, as <c>R-101-104-B</c> does for the S-104 surface. Set by the
    /// weather wave height fill (issue #1072); defaults to
    /// <see langword="false"/>, so other products are unaffected.
    /// </summary>
    public bool ClipToWater { get; init; }

    /// <summary>
    /// The fill's colour legend, band colours and labels lowest first, for a
    /// viewer to show next to the layer stack entry; <see langword="null"/>
    /// (the default) for no legend. Set by the weather fills (issue #1072,
    /// design note weather-sources.md §4.3). Renderers ignore it.
    /// </summary>
    public IReadOnlyList<CoverageLegendEntry>? Legend { get; init; }

    /// <summary>
    /// Returns a copy of this sub-layer with <see cref="LandAreaMask"/> replaced.
    /// Used by the S-98 water-area clip rule, which attaches the S-101 land
    /// geometry it discovers to the (processor-produced) S-104 surface sub-layer.
    /// </summary>
    /// <param name="landAreaMask">The land-area surfaces to clip out, or null.</param>
    public GridCoverageSubLayer WithLandAreaMask(IReadOnlyList<FeatureGeometry>? landAreaMask) =>
        new()
        {
            LayerKey = LayerKey,
            LayerName = LayerName,
            Plane = Plane,
            WithinPlanePriority = WithinPlanePriority,
            SourceFeatureType = SourceFeatureType,
            IsVisibleByDefault = IsVisibleByDefault,
            Coverage = Coverage,
            Viewport = Viewport,
            LandAreaMask = landAreaMask,
            Opacity = Opacity,
            ClipToWater = ClipToWater,
            Legend = Legend,
        };
}

/// <summary>
/// A sub-layer of labelled isolines over a gridded coverage (the S-104
/// water-level surface). The renderers draw <see cref="Isolines"/> in screen
/// space: dashed lines of constant pixel width, with labels spaced along them.
/// </summary>
/// <remarks>
/// S-104 Ed 2.0.0 §9 defines no portrayal; the S-104 isolines are SoundCharts'
/// own (see <c>S104CoverageStyle</c>).
/// </remarks>
public sealed class IsolineCoverageSubLayer : CoverageSubLayerBase
{
    /// <summary>The isolines, in EPSG:3857 metres.</summary>
    public required IReadOnlyList<Isoline> Isolines { get; init; }

    /// <summary>
    /// The EPSG:3857 extent of the coverage the isolines were traced from, or
    /// <see langword="null"/> when it is unknown. Covers the whole grid even
    /// when there are no isolines, so the dataset still has an extent.
    /// </summary>
    public MercatorBounds? Extent { get; init; }

    /// <summary>Line and label colour.</summary>
    public required RgbaColor Ink { get; init; }

    /// <summary>Label halo colour.</summary>
    public required RgbaColor LabelHalo { get; init; }

    /// <summary>Line width in device-independent pixels.</summary>
    public double LineWidth { get; init; } = 1.1;

    /// <summary>Dash pattern (dash, gap) in device-independent pixels; empty for solid.</summary>
    public IReadOnlyList<double> DashPattern { get; init; } = [6, 3];

    /// <summary>Label font size in device-independent pixels, with the text scale applied.</summary>
    public double LabelFontSize { get; init; } = 10;

    /// <summary>Width of the halo under each label in device-independent pixels, with the text scale applied.</summary>
    public double LabelHaloWidth { get; init; } = 3;

    /// <summary>Line length between labels, in device-independent pixels.</summary>
    public double LabelSpacing { get; init; } = 250;

    /// <summary>
    /// Optional land-area surface geometries (WGS84 <c>(latitude, longitude)</c>)
    /// the isolines are clipped <em>out of</em> so they only cross water. Set by
    /// the S-98 rule that clips S-104 to water when an S-101 ENC is loaded
    /// (issue #483; R-101-104-B). <see langword="null"/> or empty draws them everywhere.
    /// </summary>
    public IReadOnlyList<FeatureGeometry>? LandAreaMask { get; init; }

    /// <summary>
    /// Returns a copy of this sub-layer with <see cref="LandAreaMask"/> replaced.
    /// </summary>
    /// <param name="landAreaMask">The land-area surfaces to clip out, or null.</param>
    public IsolineCoverageSubLayer WithLandAreaMask(IReadOnlyList<FeatureGeometry>? landAreaMask) =>
        new()
        {
            LayerKey = LayerKey,
            LayerName = LayerName,
            Plane = Plane,
            WithinPlanePriority = WithinPlanePriority,
            SourceFeatureType = SourceFeatureType,
            IsVisibleByDefault = IsVisibleByDefault,
            Isolines = Isolines,
            Extent = Extent,
            Ink = Ink,
            LabelHalo = LabelHalo,
            LineWidth = LineWidth,
            DashPattern = DashPattern,
            LabelFontSize = LabelFontSize,
            LabelHaloWidth = LabelHaloWidth,
            LabelSpacing = LabelSpacing,
            LandAreaMask = landAreaMask,
        };
}

/// <summary>
/// One isoline of an <see cref="IsolineCoverageSubLayer"/>: a polyline at a
/// single coverage value.
/// </summary>
public sealed class Isoline
{
    /// <summary>The coverage value the line follows (metres, for S-104).</summary>
    public required double Level { get; init; }

    /// <summary>The label drawn along the line, e.g. <c>"0.5"</c>; empty for an unlabelled line.</summary>
    public required string Label { get; init; }

    /// <summary>Interleaved EPSG:3857 vertices <c>x0, y0, x1, y1, …</c>.</summary>
    public required double[] MercatorCoordinates { get; init; }
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

    /// <summary>
    /// Optional zoom-dependent thinning. When set, the renderers draw only the
    /// glyphs that <see cref="SymbolThinning.ThinPoints"/> keeps for the scale
    /// the layer is drawn at (S-111 §9.3.2 point-by-point method); when
    /// <see langword="null"/>, every glyph is drawn.
    /// </summary>
    public GlyphThinning? Thinning { get; init; }
}

/// <summary>
/// A coverage sub-layer portrayed with ordinary S-100 Part 9 drawing
/// instructions (point symbols and text), so its symbols and labels follow the
/// same rules as a vector product's: symbol and text scale, scale minimum
/// (SCAMIN), label declutter, upright symbols and text on a rotated display,
/// and picking. Used for the S-104 station gauges.
/// </summary>
public sealed class VectorCoverageSubLayer : CoverageSubLayerBase
{
    /// <summary>The drawing instructions, in any order (renderers sort them by priority).</summary>
    public required IReadOnlyList<DrawingInstruction> Instructions { get; init; }

    /// <summary>Resolves each instruction's feature reference to its geometry.</summary>
    public required IFeatureGeometryProvider GeometryProvider { get; init; }

    /// <summary>The palette the instructions' colour tokens resolve against.</summary>
    public required ColorPalette Palette { get; init; }

    /// <summary>Resolves a symbol reference to its S-100 SVG.</summary>
    public required Func<string, string?> SymbolProvider { get; init; }

    /// <summary>The mariner's symbol scale.</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>The mariner's text scale.</summary>
    public double TextScale { get; init; } = 1.0;

    /// <summary>The product name used for telemetry and tile cache keys, e.g. <c>"S-104"</c>.</summary>
    public required string Product { get; init; }

    /// <summary>The EPSG:3857 extent of the features, or null when there are none.</summary>
    public MercatorBounds? Extent { get; init; }
}

/// <summary>
/// Zoom-dependent thinning parameters for a <see cref="GlyphCoverageSubLayer"/>.
/// </summary>
public sealed class GlyphThinning
{
    /// <summary>
    /// On-screen length, in device-independent pixels, of a glyph drawn at
    /// <see cref="PointGlyph.SymbolScale"/> 1 — the dimension glyphs are spaced by.
    /// </summary>
    public required double SymbolLengthPixelsPerScale { get; init; }

    /// <summary>
    /// The thinning ratio <c>Rmax</c> between a glyph's length and the spacing it
    /// clears around itself. Defaults to
    /// <see cref="SymbolThinning.DefaultMaxSymbolToSpacingRatio"/>.
    /// </summary>
    public double MaxSymbolToSpacingRatio { get; init; } = SymbolThinning.DefaultMaxSymbolToSpacingRatio;
}

/// <summary>
/// The symbol shape a <see cref="PointGlyph"/> is drawn with.
/// </summary>
public enum PointGlyphSymbol
{
    /// <summary>Filled ellipse with an outline (S-104 water-level stations).</summary>
    Ellipse,

    /// <summary>Filled triangle with an outline.</summary>
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

    /// <summary>
    /// The final symbol rotation, in degrees clockwise on screen (Mapsui's
    /// <c>SymbolRotation</c> convention). A current direction in degrees true
    /// maps to this unchanged on a north-up Mercator display.
    /// </summary>
    public double Rotation { get; init; }

    /// <summary>
    /// Priority when the sub-layer is thinned (see
    /// <see cref="GlyphCoverageSubLayer.Thinning"/>): higher is kept first. For
    /// S-111, the current speed.
    /// </summary>
    public double ThinningPriority { get; init; }
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
    /// A content identity of everything that shapes this dataset's
    /// <see cref="VectorCoverageSubLayer"/>s beyond their drawing instructions:
    /// a hash of the dataset's bytes plus the style's identity, as
    /// <see cref="VectorPortrayalResult.ContentScope"/> is for a vector product.
    /// For a style defined in code it carries the style's version, so a new
    /// style version drops the warm tiles drawn with the old one.
    /// </summary>
    /// <remarks>
    /// The Mapsui renderer folds it into the warm tile disk-cache namespace of
    /// the vector sub-layers. When <see langword="null"/>, they bypass the disk
    /// cache. The other sub-layer kinds don't use the disk cache.
    /// </remarks>
    public string? ContentScope { get; init; }

    /// <summary>
    /// Optional stable per-sub-layer keys for the viewer's disclosure UI.
    /// When null the renderer derives them from each sub-layer's key.
    /// </summary>
    public IReadOnlyList<string>? LayerNames { get; init; }

    /// <summary>
    /// The WGS-84 extent of the whole coverage grid, independent of the
    /// viewport a render sampled, or <see langword="null"/> when the producer
    /// does not report one. S-98 rules that act only where the coverage is
    /// displayed read it: S-102 replaces ENC depth features inside this extent
    /// and nowhere else (S-98 Part A §A-6.9.1).
    /// </summary>
    public GeographicBounds? CoverageExtent { get; init; }
}
