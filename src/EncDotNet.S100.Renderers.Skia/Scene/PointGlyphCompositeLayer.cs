using EncDotNet.S100.Pipelines;
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
}

/// <summary>
/// Paints projected point glyphs into a shared headless composite viewport.
/// </summary>
public sealed class PointGlyphCompositeLayer : CompositeLayer
{
    private const float PrimitiveSizePixels = 32f;
    private readonly IReadOnlyList<SkiaPointGlyph> _glyphs;

    /// <summary>
    /// Creates a point-glyph composite layer.
    /// </summary>
    /// <param name="glyphs">Glyphs to paint, in draw order.</param>
    public PointGlyphCompositeLayer(IReadOnlyList<SkiaPointGlyph> glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        _glyphs = glyphs;
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
            foreach (var glyph in _glyphs)
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
