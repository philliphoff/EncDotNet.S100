using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;
using Svg.Skia;

namespace EncDotNet.S100.Renderers.Skia;

/// <summary>
/// Draws oriented overlay symbols (e.g. S-111 surface-current arrows) from a
/// <see cref="StyledCoverageLayer.SymbolScheme"/> directly onto an
/// <see cref="SKCanvas"/>, with no Mapsui dependency. This is the headless,
/// direct-Skia analogue of <c>MapsuiCoverageArrowRenderer</c>: it shares the
/// same per-band scaling contract (S-111 Ed 2.0.0,
/// <c>content/S111/pc/Rules/select_arrow.xsl</c>) and palette-token SVG
/// processing, but rasterises each arrow with <see cref="SKSvg"/> and projects
/// grid-cell centres through <see cref="WebMercator"/> instead of Mapsui's
/// navigator.
/// </summary>
public sealed class SkiaCoverageArrowRenderer
{
    private readonly Dictionary<string, SKSvg?> _svgCache = new(StringComparer.OrdinalIgnoreCase);
    private ColorPalette? _cachedFor;

    /// <summary>
    /// The colour palette used to resolve SVG CSS fill/stroke tokens
    /// (e.g. <c>fSCBN1</c> → palette token <c>SCBN1</c>).
    /// </summary>
    public ColorPalette? Palette { get; init; }

    /// <summary>
    /// Returns raw SVG content for a symbol reference name
    /// (e.g. <c>"SCAROW01"</c>), or <c>null</c> when unavailable.
    /// </summary>
    public required Func<string, string?> SymbolProvider { get; init; }

    /// <summary>
    /// Multiplier applied to each band's scale factor. Callers fold the
    /// user-facing <c>RenderContext.SymbolScale</c> into this value so the
    /// symbol-scale preference continues to affect arrow size.
    /// </summary>
    public double BaseSymbolScale { get; init; } = 1.0;

    /// <summary>
    /// Target maximum number of arrows along the longest grid axis; the
    /// renderer strides the grid so no more than roughly
    /// <c>MaxArrowsPerAxis²</c> arrows are emitted. Set to 0 to disable
    /// subsampling.
    /// </summary>
    public int MaxArrowsPerAxis { get; init; } = 80;

    /// <summary>
    /// Draws the layer's symbol scheme onto <paramref name="canvas"/>. Each
    /// selected grid cell becomes one rotated, palette-coloured symbol placed
    /// at its projected pixel position. No-ops when the layer has no symbol
    /// scheme.
    /// </summary>
    /// <param name="canvas">Target canvas (already sized / cleared by the caller).</param>
    /// <param name="layer">The styled coverage layer carrying the symbol scheme.</param>
    /// <param name="nativeToWgs84">
    /// Transform from the grid's native CRS to WGS84 (EPSG:4326); pass
    /// <see cref="IdentityCrsTransform.Instance"/> for geographic grids.
    /// </param>
    /// <param name="project">
    /// Projects an EPSG:3857 (x, y) world coordinate to an output pixel.
    /// </param>
    public void Draw(
        SKCanvas canvas,
        StyledCoverageLayer layer,
        ICrsTransform nativeToWgs84,
        Func<(double X, double Y), (float X, float Y)> project)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(nativeToWgs84);
        ArgumentNullException.ThrowIfNull(project);

        var symbolScheme = layer.SymbolScheme;
        if (symbolScheme is null)
            return;

        var sampled = layer.Coverage;
        var georeferencer = layer.Georeferencer;
        var valueData = sampled.GetField(symbolScheme.ValueFieldName);
        var rotationData = sampled.GetField(symbolScheme.RotationFieldName);
        int srcRows = valueData.GetLength(0);
        int srcCols = valueData.GetLength(1);

        float noDataValue = layer.NoDataValue;
        bool noDataIsNaN = float.IsNaN(noDataValue);

        int stride = 1;
        if (MaxArrowsPerAxis > 0)
        {
            int longestAxis = Math.Max(srcRows, srcCols);
            stride = Math.Max(1, (longestAxis + MaxArrowsPerAxis - 1) / MaxArrowsPerAxis);
        }

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

            var picture = GetPicture(band.SymbolRef);
            if (picture is null) continue;

            var (nativeX, nativeY) = georeferencer.ToNative(r, c);
            double lon, lat;
            if (nativeToWgs84.IsIdentity) { lon = nativeX; lat = nativeY; }
            else { (lon, lat) = nativeToWgs84.Transform(nativeX, nativeY); }
            var (mx, my) = WebMercator.FromLonLat(lon, lat);
            var (px, py) = project((mx, my));

            double bandScale = band.ScaleByValue
                ? band.ScaleFactor * value
                : band.ScaleFactor;
            float scale = (float)(BaseSymbolScale * bandScale);
            if (scale <= 0) continue;

            var bounds = picture.CullRect;

            canvas.Save();
            canvas.Translate(px, py);
            // surfaceCurrentDirection is degrees true (0=N, 90=E), which matches
            // Skia's clockwise-from-up rotation convention.
            canvas.RotateDegrees(direction);
            canvas.Scale(scale);
            // Centre the symbol's bbox on the (now rotated/scaled) origin.
            canvas.Translate(-(bounds.Left + bounds.Width / 2f), -(bounds.Top + bounds.Height / 2f));
            canvas.DrawPicture(picture);
            canvas.Restore();
        }
    }

    private SKPicture? GetPicture(string symbolRef)
    {
        if (!ReferenceEquals(_cachedFor, Palette))
        {
            DisposePictures();
            _cachedFor = Palette;
        }

        if (_svgCache.TryGetValue(symbolRef, out var cached))
            return cached?.Picture;

        SKSvg? svg = null;
        try
        {
            var raw = SymbolProvider(symbolRef);
            if (raw is not null)
            {
                var processed = SvgProcessor.Process(raw, Palette);
                // The SKSvg owns its Picture; it must be kept alive (cached) for
                // as long as the Picture may be drawn, hence it is NOT disposed
                // here. It is released in DisposePictures.
                var created = SKSvg.CreateFromSvg(processed);
                if (created?.Picture is not null)
                    svg = created;
                else
                    created?.Dispose();
            }
        }
        catch
        {
            // Symbol not found or malformed — cache the miss so we do not
            // repeatedly retry the same broken symbol.
        }

        _svgCache[symbolRef] = svg;
        return svg?.Picture;
    }

    private void DisposePictures()
    {
        foreach (var svg in _svgCache.Values)
            svg?.Dispose();
        _svgCache.Clear();
    }
}
