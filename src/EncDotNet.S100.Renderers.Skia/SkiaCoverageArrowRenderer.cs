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
    /// Draws the layer's symbol scheme onto <paramref name="canvas"/>. Each grid
    /// cell that survives thinning becomes one rotated, palette-coloured symbol
    /// placed at its projected pixel position. No-ops when the layer has no
    /// symbol scheme.
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
    /// <remarks>
    /// Thinning uses the S-98 Appendix G-1.1 grid algorithm
    /// (<see cref="SymbolThinning.ThinGrid"/>) over the canvas's clip bounds,
    /// with the same arrow length and <c>Rmax</c> as
    /// <c>MapsuiCoverageArrowRenderer</c>, so a headless render and the viewer
    /// draw the same arrows for the same view.
    /// </remarks>
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

        var field = CoverageSymbolField.Build(layer, nativeToWgs84);
        int count = field.X.Length;
        if (count == 0)
            return;

        // Thin in output-pixel space.
        var screenX = new double[count];
        var screenY = new double[count];
        for (int i = 0; i < count; i++)
        {
            var (px, py) = project((field.X[i], field.Y[i]));
            screenX[i] = px;
            screenY[i] = py;
        }

        double lengthPerScale = symbolScheme.NominalSymbolLengthMillimetres
            * SymbolThinning.PixelsPerMillimetre * BaseSymbolScale;
        float maxScale = 0;
        foreach (float s in field.Scale)
        {
            if (s > maxScale)
                maxScale = s;
        }

        var clip = canvas.LocalClipBounds;
        var displayed = new SymbolRect(clip.Left, clip.Top, clip.Right, clip.Bottom)
            .Inflate(maxScale * lengthPerScale / 2.0);

        var selected = new List<int>();
        SymbolThinning.ThinGrid(
            field.Rows, field.Cols, screenX, screenY, field.Scale, field.Priority,
            lengthPerScale, displayed, symbolScheme.MaxSymbolToSpacingRatio, selected);

        foreach (int i in selected)
        {
            var band = field.Bands[i];
            if (band is null)
                continue;

            var picture = GetPicture(band.SymbolRef);
            if (picture is null)
                continue;

            float scale = (float)(BaseSymbolScale * field.Scale[i]);
            if (scale <= 0)
                continue;

            var bounds = picture.CullRect;

            canvas.Save();
            canvas.Translate((float)screenX[i], (float)screenY[i]);
            // surfaceCurrentDirection is degrees true (0=N, 90=E), which matches
            // Skia's clockwise-from-up rotation convention.
            canvas.RotateDegrees(field.Rotation[i]);
            canvas.Scale(scale);
            // Centre the symbol's bbox on the (now rotated/scaled) origin: the
            // SCAROW pivot point (0, 0) is the centre of its viewBox.
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
