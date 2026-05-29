using System.Runtime.CompilerServices;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;

using PipelineViewport = EncDotNet.S100.Pipelines.Viewport;

[assembly: InternalsVisibleTo("EncDotNet.S100.Datasets.S111.Tests")]

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders oriented symbols (e.g. S-111 current arrows) from a
/// <see cref="StyledCoverageLayer"/> as one vector <see cref="PointFeature"/>
/// per selected grid cell.  Each feature carries a Mapsui
/// <see cref="ImageStyle"/> that wraps the bundled SVG symbol via the
/// <c>"svg-content://"</c> URI scheme so Mapsui re-rasterises the symbol
/// at the active screen DPI on every viewport change.
/// </summary>
/// <remarks>
/// <para>
/// The previous implementation rasterised every arrow into a single
/// georeferenced PNG (<see cref="RasterFeature"/>) sized to the dataset
/// extent.  At low map zoom that bitmap was downscaled (arrows shrank
/// to a few screen pixels and were hard to read); at high zoom it was
/// upscaled (arrows pixelated).  Per-feature symbols keep arrows at a
/// stable on-screen size and sharp at every zoom — the convention used
/// by ECDIS-style symbology in S-100 Part 9 §11.
/// </para>
/// <para>
/// Per-band scaling follows the bundled portrayal catalogue
/// (S-111 Ed 2.0.0, <c>content/S111/pc/Rules/select_arrow.xsl</c>):
/// bands 1-3 share <c>scaleFloor = 0.40</c>, bands 4-8 use
/// <c>scaleFactorIntermediate = 0.20</c> multiplied by
/// <c>surfaceCurrentSpeed</c>, and band 9 uses
/// <c>scaleCeiling = 2.60</c>.  These per-band factors multiply
/// <see cref="BaseSymbolScale"/> to produce the Mapsui
/// <see cref="ImageStyle.SymbolScale"/>.
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
    /// Target maximum number of arrows along the longest grid axis.
    /// The renderer computes a stride so that no more than roughly
    /// <c>MaxArrowsPerAxis²</c> arrows are emitted.
    /// Set to 0 to disable subsampling.
    /// </summary>
    public int MaxArrowsPerAxis { get; set; } = 80;

    /// <summary>
    /// Multiplier applied to each band's scale factor to produce the
    /// Mapsui <see cref="ImageStyle.SymbolScale"/>.  The bundled SCAROW
    /// SVGs declare <c>width="6mm" height="11mm"</c> with viewBox
    /// <c>-3 -5.5 6 11</c>; Mapsui rasterises them at roughly
    /// 23×42 pixels at 96 dpi when <c>SymbolScale = 1.0</c>.  Callers
    /// (typically <c>S111DatasetProcessor</c>) should multiply the
    /// user-facing <c>RenderContext.SymbolScale</c> into this value so
    /// the Symbol Scale slider continues to affect arrow size.
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

    public MapsuiCoverageArrowRenderer(ICrsTransformFactory transformFactory)
    {
        _transformFactory = transformFactory;
    }

    /// <summary>
    /// Renders the layer's symbol scheme as one rotated, palette-coloured
    /// <see cref="PointFeature"/> per selected grid cell.  Returns
    /// <c>null</c> when the layer has no symbol scheme.
    /// </summary>
    public ILayer? Render(StyledCoverageLayer layer, PipelineViewport viewport)
    {
        _ = viewport; // Per-feature rendering is zoom-agnostic by design.

        var symbolScheme = layer.SymbolScheme;
        if (symbolScheme is null)
            return null;

        var sampled = layer.Coverage;
        var georeferencer = layer.Georeferencer;
        var valueData = sampled.GetField(symbolScheme.ValueFieldName);
        var rotationData = sampled.GetField(symbolScheme.RotationFieldName);
        int srcRows = valueData.GetLength(0);
        int srcCols = valueData.GetLength(1);

        float noDataValue = layer.NoDataValue;
        bool noDataIsNaN = float.IsNaN(noDataValue);

        var nativeToWgs84 = _transformFactory.Create(georeferencer.CRS, "EPSG:4326");

        // Subsample so dense grids do not emit hundreds of thousands of
        // features at zoom-out; matches the spacing the old bitmap path used.
        int stride = 1;
        if (MaxArrowsPerAxis > 0)
        {
            int longestAxis = Math.Max(srcRows, srcCols);
            stride = Math.Max(1, (longestAxis + MaxArrowsPerAxis - 1) / MaxArrowsPerAxis);
        }

        var features = new List<IFeature>();

        for (int r = 0; r < srcRows; r += stride)
        for (int c = 0; c < srcCols; c += stride)
        {
            float value = valueData[r, c];
            bool isNoData = noDataIsNaN ? float.IsNaN(value) : value == noDataValue;
            if (isNoData) continue;

            float direction = rotationData[r, c];
            bool dirNoData = noDataIsNaN ? float.IsNaN(direction) : direction == noDataValue;
            if (dirNoData) continue;

            var band = symbolScheme.Resolve(value);
            if (band is null) continue;

            var svgSource = GetResolvedSvg(band.SymbolRef);
            if (svgSource is null) continue;

            // Project grid cell centre to Mercator for the PointFeature.
            var (nativeX, nativeY) = georeferencer.ToNative(r, c);
            double lon, lat;
            if (nativeToWgs84.IsIdentity) { lon = nativeX; lat = nativeY; }
            else { (lon, lat) = nativeToWgs84.Transform(nativeX, nativeY); }
            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

            double bandScale = band.ScaleByValue
                ? band.ScaleFactor * value
                : band.ScaleFactor;

            var feature = new PointFeature(mx, my);
            feature.Styles.Add(new ImageStyle
            {
                Image = new Image { Source = svgSource, RasterizeSvg = true },
                SymbolScale = BaseSymbolScale * bandScale,
                // SymbolRotation in Mapsui is degrees clockwise from
                // map-up; surfaceCurrentDirection is degrees true (0=N,
                // 90=E), which is the same convention.
                SymbolRotation = direction,
                Opacity = (float)Opacity,
            });
            features.Add(feature);
        }

        return new MemoryLayer
        {
            Name = LayerName,
            Features = features,
            Style = null,
            Opacity = Opacity,
        };
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
