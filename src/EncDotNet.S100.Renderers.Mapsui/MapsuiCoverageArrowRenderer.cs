using System.Runtime.CompilerServices;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Rendering.Scene;
using Mapsui.Layers;
using Mapsui.Styles;

[assembly: InternalsVisibleTo("EncDotNet.S100.Datasets.S111.Tests")]
[assembly: InternalsVisibleTo("EncDotNet.S100.Pipelines.Tests")]

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders oriented symbols (e.g. S-111 current arrows) from a
/// <see cref="StyledCoverageLayer"/> as a <see cref="ThinnedSymbolLayer"/>:
/// one vector <see cref="PointFeature"/> per grid cell that survives
/// zoom-dependent thinning. Each feature carries a Mapsui
/// <see cref="ImageStyle"/> that wraps the bundled SVG symbol via the
/// <c>"svg-content://"</c> URI scheme so Mapsui re-rasterises the symbol
/// at the active screen DPI on every viewport change.
/// </summary>
/// <remarks>
/// <para>
/// Per-feature symbols keep arrows at a stable on-screen size and sharp at
/// every zoom — S-111 Ed 2.0.0 §9.2.4 sizes arrows in millimetres on the
/// display, not in ground units.
/// </para>
/// <para>
/// Per-band scaling follows the bundled portrayal catalogue
/// (S-111 Ed 2.0.0, <c>content/S111/pc/Rules/select_arrow.xsl</c>):
/// bands 1-3 share <c>scaleFloor = 0.40</c>, bands 4-8 use
/// <c>scaleFactorIntermediate = 0.20</c> multiplied by
/// <c>surfaceCurrentSpeed</c>, and band 9 uses
/// <c>scaleCeiling = 2.60</c> — Eqn 9.1 with <c>Href</c> = 10 mm,
/// <c>Sref</c> = 5 kn, <c>Slow</c> = 2 kn and <c>Shigh</c> = 13 kn. These
/// per-band factors multiply <see cref="BaseSymbolScale"/> to produce the
/// Mapsui <see cref="BasePointStyle.SymbolScale"/>.
/// </para>
/// <para>
/// Thinning is done per frame by the returned layer, for the resolution it is
/// drawn at, with the S-98 Appendix G-1.1 grid algorithm (see
/// <see cref="SymbolThinning.ThinGrid"/>). The arrow length it spaces by is the
/// scheme's <see cref="CoverageSymbolScheme.NominalSymbolLengthMillimetres"/> ×
/// band scale × <see cref="BaseSymbolScale"/>, converted at
/// <see cref="SymbolThinning.PixelsPerMillimetre"/>.
/// </para>
/// </remarks>
public sealed class MapsuiCoverageArrowRenderer
{
    private readonly ICrsTransformFactory _transformFactory;
    private readonly Dictionary<string, string?> _resolvedSvgCache =
        new(StringComparer.OrdinalIgnoreCase);
    private ColorPalette? _cachedFor;

    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "Coverage Arrows";

    /// <summary>Layer opacity (0.0–1.0). Defaults to 1.0.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Multiplier applied to each band's scale factor to produce the
    /// Mapsui <see cref="BasePointStyle.SymbolScale"/>.  The bundled SCAROW
    /// SVGs declare <c>width="6mm" height="11mm"</c> with viewBox
    /// <c>-3 -5.5 6 11</c>; Mapsui rasterises them at roughly
    /// 23×42 pixels at 96 dpi when <c>SymbolScale = 1.0</c>.  Callers
    /// (typically <c>S111DatasetProcessor</c>) should multiply the
    /// user-facing <c>RenderContext.SymbolScale</c> into this value so
    /// the Symbol Scale slider continues to affect arrow size. Thinning
    /// spaces arrows by their scaled size, so a larger symbol scale also
    /// draws fewer arrows.
    /// </summary>
    public double BaseSymbolScale { get; set; } = 1.0;

    /// <summary>
    /// The colour palette used to resolve SVG CSS fill/stroke tokens
    /// (e.g. <c>fSCBN1</c> → palette token <c>SCBN1</c>).
    /// </summary>
    public ColorPalette? Palette { get; set; }

    /// <summary>
    /// Returns raw SVG content for a symbol reference name
    /// (e.g. <c>"SCAROW01"</c>).
    /// </summary>
    public required Func<string, string?> SymbolProvider { get; set; }

    /// <summary>
    /// Creates an arrow renderer that places symbols in Web Mercator.
    /// </summary>
    /// <param name="transformFactory">
    /// Creates the transform from a coverage's native CRS to WGS84, which is
    /// then projected to Web Mercator.
    /// </param>
    public MapsuiCoverageArrowRenderer(ICrsTransformFactory transformFactory)
    {
        _transformFactory = transformFactory;
    }

    /// <summary>
    /// Renders the layer's symbol scheme as a <see cref="ThinnedSymbolLayer"/>
    /// of rotated, palette-coloured <see cref="PointFeature"/>s, one per grid
    /// cell with data. Returns <c>null</c> when the layer has no symbol scheme.
    /// </summary>
    /// <param name="layer">The styled coverage layer carrying the symbol scheme.</param>
    public ILayer? Render(StyledCoverageLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var symbolScheme = layer.SymbolScheme;
        if (symbolScheme is null)
            return null;

        var field = CoverageSymbolField.Build(
            layer, _transformFactory.Create(layer.Georeferencer.CRS, "EPSG:4326"));

        double lengthPixelsPerScale = symbolScheme.NominalSymbolLengthMillimetres
            * SymbolThinning.PixelsPerMillimetre * BaseSymbolScale;

        var thinned = new ThinnedSymbolLayer(
            field.Rows,
            field.Cols,
            field.X,
            field.Y,
            field.Scale,
            field.Priority,
            lengthPixelsPerScale,
            symbolScheme.MaxSymbolToSpacingRatio,
            i => CreateFeature(field, i))
        {
            Name = LayerName,
            Style = null,
            Opacity = Opacity,
        };
        return thinned;
    }

    private PointFeature? CreateFeature(CoverageSymbolField field, int index)
    {
        var band = field.Bands[index];
        if (band is null)
            return null;

        var svgSource = GetResolvedSvg(band.SymbolRef);
        if (svgSource is null)
            return null;

        var feature = new PointFeature(field.X[index], field.Y[index]);
        feature.Styles.Add(new ImageStyle
        {
            Image = new Image { Source = svgSource, RasterizeSvg = true },
            SymbolScale = BaseSymbolScale * field.Scale[index],
            // SymbolRotation in Mapsui is degrees clockwise from map-up;
            // surfaceCurrentDirection is degrees true (0=N, 90=E), which is
            // the same convention (S-111 §9.2.2: a Mercator display preserves
            // angles, so no further correction is needed).
            SymbolRotation = field.Rotation[index],
            RotateWithMap = true,
            Opacity = (float)Opacity,
        });
        return feature;
    }

    /// <summary>
    /// Returns the palette-resolved, externally-CSS-free SVG source for
    /// <paramref name="symbolRef"/>, wrapped in the
    /// <c>"svg-content://"</c> URI scheme expected by Mapsui's SVG
    /// rasteriser.  Results are cached per palette; the cache is
    /// invalidated when <see cref="Palette"/> changes by reference.
    /// </summary>
    internal string? GetResolvedSvg(string symbolRef)
    {
        if (!ReferenceEquals(_cachedFor, Palette))
        {
            _resolvedSvgCache.Clear();
            _cachedFor = Palette;
        }

        if (_resolvedSvgCache.TryGetValue(symbolRef, out var cached))
            return cached;

        string? result = null;
        try
        {
            var raw = SymbolProvider(symbolRef);
            if (raw is not null)
            {
                var processed = SvgProcessor.Process(raw, Palette);
                result = "svg-content://" + processed;
            }
        }
        catch
        {
            // Symbol not found or malformed — cache the miss so we do
            // not repeatedly retry the same broken symbol.
        }

        _resolvedSvgCache[symbolRef] = result;
        return result;
    }
}
