using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;
using Svg.Skia;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// Identifies the primitive used to draw a point glyph in a headless composite.
/// </summary>
public enum SkiaPointGlyphSymbol
{
    /// <summary>An ellipse centred on the feature position.</summary>
    Ellipse,

    /// <summary>A triangle centred on the feature position.</summary>
    Triangle,

    /// <summary>An SVG symbol centred on the feature position.</summary>
    Svg,
}

/// <summary>
/// Describes one projected point glyph for Mapsui-free Skia rendering.
/// </summary>
public sealed class SkiaPointGlyph
{
    /// <summary>Feature X coordinate in EPSG:3857 metres.</summary>
    public required double MercatorX { get; init; }

    /// <summary>Feature Y coordinate in EPSG:3857 metres.</summary>
    public required double MercatorY { get; init; }

    /// <summary>Glyph primitive.</summary>
    public required SkiaPointGlyphSymbol Symbol { get; init; }

    /// <summary>Optional processed SVG content when <see cref="Symbol"/> is <see cref="SkiaPointGlyphSymbol.Svg"/>.</summary>
    public string? SvgSource { get; init; }

    /// <summary>Glyph fill colour.</summary>
    public required RgbaColor FillColor { get; init; }

    /// <summary>Glyph outline colour.</summary>
    public required RgbaColor OutlineColor { get; init; }

    /// <summary>Outline width in display pixels.</summary>
    public double OutlineWidth { get; init; } = 1.0;

    /// <summary>Scale applied to the primitive or SVG's natural pixel size.</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>Clockwise rotation in degrees.</summary>
    public double RotationDegrees { get; init; }

    /// <summary>Thinning priority (higher is kept first); see <see cref="PointGlyphCompositeLayer"/>.</summary>
    public double ThinningPriority { get; init; }
}

/// <summary>
/// Paints projected point glyphs into a shared headless composite viewport.
/// </summary>
public sealed class PointGlyphCompositeLayer : CompositeLayer
{
    private const float PrimitiveSizePixels = 32f;
    private readonly IReadOnlyList<SkiaPointGlyph> _glyphs;
    private readonly double _thinningLengthPerScale;
    private readonly double _thinningMaxRatio;

    /// <summary>
    /// Creates a point-glyph composite layer.
    /// </summary>
    /// <param name="glyphs">Glyphs to paint, in draw order.</param>
    /// <param name="thinningLengthPixelsPerScale">
    /// When positive, glyphs are thinned point by point
    /// (<see cref="SymbolThinning.ThinPoints"/>) for the viewport they are drawn
    /// into: this is the on-screen length, in pixels, of a glyph at
    /// <see cref="SkiaPointGlyph.SymbolScale"/> 1. Zero (the default) draws every glyph.
    /// </param>
    /// <param name="thinningMaxRatio">The thinning ratio <c>Rmax</c>.</param>
    public PointGlyphCompositeLayer(
        IReadOnlyList<SkiaPointGlyph> glyphs,
        double thinningLengthPixelsPerScale = 0,
        double thinningMaxRatio = SymbolThinning.DefaultMaxSymbolToSpacingRatio)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        _glyphs = glyphs;
        _thinningLengthPerScale = thinningLengthPixelsPerScale;
        _thinningMaxRatio = thinningMaxRatio;
    }

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(viewport);

        var (minX, minY) = WebMercator.FromLonLat(viewport.MinLongitude, viewport.MinLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(viewport.MaxLongitude, viewport.MaxLatitude);
        double spanX = maxX - minX;
        double spanY = maxY - minY;
        if (spanX <= 0 || spanY <= 0)
            return;

        double scaleX = viewport.WidthPixels / spanX;
        double scaleY = viewport.HeightPixels / spanY;
        var parsedSvgs = new Dictionary<string, SKSvg?>(StringComparer.Ordinal);

        try
        {
            foreach (var glyph in SelectGlyphs(scaleX, scaleY))
            {
                float x = (float)((glyph.MercatorX - minX) * scaleX);
                float y = (float)((maxY - glyph.MercatorY) * scaleY);

                canvas.Save();
                try
                {
                    canvas.Translate(x, y);
                    canvas.RotateDegrees((float)glyph.RotationDegrees);

                    switch (glyph.Symbol)
                    {
                        case SkiaPointGlyphSymbol.Ellipse:
                            DrawEllipse(canvas, glyph);
                            break;
                        case SkiaPointGlyphSymbol.Triangle:
                            DrawTriangle(canvas, glyph);
                            break;
                        case SkiaPointGlyphSymbol.Svg:
                            DrawSvg(canvas, glyph, parsedSvgs);
                            break;
                    }
                }
                finally
                {
                    canvas.Restore();
                }
            }
        }
        finally
        {
            foreach (var svg in parsedSvgs.Values)
                svg?.Dispose();
        }
    }

    /// <summary>
    /// The glyphs to draw: all of them, or the ones point-by-point thinning keeps
    /// at this viewport's scale. Thinning runs over every glyph (not just the
    /// visible ones), matching the viewer's Mapsui layer.
    /// </summary>
    private IEnumerable<SkiaPointGlyph> SelectGlyphs(double scaleX, double scaleY)
    {
        if (!(_thinningLengthPerScale > 0) || _glyphs.Count == 0)
            return _glyphs;

        int count = _glyphs.Count;
        var x = new double[count];
        var y = new double[count];
        var length = new double[count];
        var priority = new double[count];
        for (int i = 0; i < count; i++)
        {
            var glyph = _glyphs[i];
            x[i] = glyph.MercatorX * scaleX;
            y[i] = glyph.MercatorY * scaleY;
            length[i] = glyph.SymbolScale * _thinningLengthPerScale;
            priority[i] = glyph.ThinningPriority;
        }

        var kept = new List<int>();
        SymbolThinning.ThinPoints(x, y, length, priority, _thinningMaxRatio, kept);
        return kept.Select(i => _glyphs[i]);
    }

    private static void DrawEllipse(SKCanvas canvas, SkiaPointGlyph glyph)
    {
        float radius = PrimitiveSizePixels * (float)glyph.SymbolScale / 2f;
        using var fill = new SKPaint
        {
            Color = glyph.FillColor.ToSkia(),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var outline = CreateOutlinePaint(glyph);
        canvas.DrawCircle(0, 0, radius, fill);
        canvas.DrawCircle(0, 0, radius, outline);
    }

    private static void DrawTriangle(SKCanvas canvas, SkiaPointGlyph glyph)
    {
        float halfSize = PrimitiveSizePixels * (float)glyph.SymbolScale / 2f;
        using var path = new SKPath();
        path.MoveTo(0, -halfSize);
        path.LineTo(halfSize, halfSize);
        path.LineTo(-halfSize, halfSize);
        path.Close();

        using var fill = new SKPaint
        {
            Color = glyph.FillColor.ToSkia(),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var outline = CreateOutlinePaint(glyph);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, outline);
    }

    private static void DrawSvg(
        SKCanvas canvas,
        SkiaPointGlyph glyph,
        Dictionary<string, SKSvg?> parsedSvgs)
    {
        if (string.IsNullOrWhiteSpace(glyph.SvgSource))
            return;

        if (!parsedSvgs.TryGetValue(glyph.SvgSource, out var svg))
        {
            const string mapsuiSvgPrefix = "svg-content://";
            string svgContent = glyph.SvgSource.StartsWith(mapsuiSvgPrefix, StringComparison.Ordinal)
                ? glyph.SvgSource[mapsuiSvgPrefix.Length..]
                : glyph.SvgSource;
            try
            {
                svg = SKSvg.CreateFromSvg(svgContent);
            }
            catch
            {
                svg = null;
            }
            parsedSvgs.Add(glyph.SvgSource, svg);
        }

        var picture = svg?.Picture;
        if (picture is null)
            return;

        var bounds = picture.CullRect;
        canvas.Scale((float)glyph.SymbolScale);
        canvas.Translate(
            -(bounds.Left + bounds.Width / 2f),
            -(bounds.Top + bounds.Height / 2f));
        canvas.DrawPicture(picture);
    }

    private static SKPaint CreateOutlinePaint(SkiaPointGlyph glyph) =>
        new()
        {
            Color = glyph.OutlineColor.ToSkia(),
            IsAntialias = true,
            StrokeWidth = (float)glyph.OutlineWidth,
            Style = SKPaintStyle.Stroke,
        };
}
