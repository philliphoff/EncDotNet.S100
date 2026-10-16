using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// The mark drawn inside a water-level gauge symbol.
/// </summary>
public enum WaterLevelGaugeMark
{
    /// <summary>Trend unknown: a "?" in the square.</summary>
    Unknown,

    /// <summary>Water level falling: a chevron pointing down.</summary>
    Falling,

    /// <summary>Water level rising: a chevron pointing up.</summary>
    Rising,

    /// <summary>Water level steady: a horizontal dash.</summary>
    Steady,

    /// <summary>No value at this time step: an em dash, and no value label.</summary>
    NoData,
}

/// <summary>
/// Paints the SoundCharts water-level station symbol: a rounded square with a
/// trend mark, and the height as a monospace label to its right. Shared by the
/// viewer's Mapsui style renderer and the headless composite so both draw the
/// same symbol.
/// </summary>
/// <remarks>
/// S-104 Ed 2.0.0 §9 defines no portrayal, so this symbol is SoundCharts' own.
/// It uses one ink colour per palette, and colour does not encode the trend or
/// the height. All sizes are device-independent pixels at symbol scale 1.
/// </remarks>
public static class WaterLevelGaugePainter
{
    /// <summary>Side of the square, in pixels.</summary>
    public const float SquareSize = 16f;

    /// <summary>Corner radius of the square, in pixels.</summary>
    public const float CornerRadius = 3f;

    /// <summary>Stroke width of the square, in pixels.</summary>
    public const float SquareStrokeWidth = 1.6f;

    /// <summary>Stroke width of the trend mark, in pixels.</summary>
    public const float MarkStrokeWidth = 1.9f;

    /// <summary>Size of the value label, in pixels.</summary>
    public const float LabelFontSize = 11f;

    /// <summary>Distance from the symbol centre to the label's left edge, in pixels.</summary>
    public const float LabelOffsetX = 13f;

    /// <summary>Distance from the symbol centre down to the label baseline, in pixels.</summary>
    public const float LabelBaselineY = 4f;

    /// <summary>Width of the halo stroked under labels, in pixels.</summary>
    public const float HaloWidth = 3f;

    private const float UnknownFontSize = 10f;

    /// <summary>
    /// The monospace semibold face used for value labels (the app's mono
    /// stack), falling back to the renderer's default face.
    /// </summary>
    public static SKTypeface LabelTypeface { get; } = MatchTypeface(
        new SKFontStyle(SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
        "JetBrains Mono", "Menlo", "Consolas", "DejaVu Sans Mono", "Courier New");

    private static readonly SKTypeface MarkTypeface = MatchTypeface(
        SKFontStyle.Bold, "Inter", "Helvetica Neue", "Segoe UI", "Helvetica", "Arial", "DejaVu Sans");

    /// <summary>
    /// Draws one gauge centred on (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    /// <param name="canvas">The canvas to draw on, in screen pixels.</param>
    /// <param name="x">Symbol centre, screen X.</param>
    /// <param name="y">Symbol centre, screen Y.</param>
    /// <param name="mark">The trend mark to draw.</param>
    /// <param name="label">The value label, or <see langword="null"/> to draw the square alone.</param>
    /// <param name="ink">Stroke, mark and label colour.</param>
    /// <param name="fill">Square fill colour.</param>
    /// <param name="halo">Label halo colour.</param>
    /// <param name="scale">Symbol scale; 1 draws the sizes above.</param>
    public static void Draw(
        SKCanvas canvas,
        float x,
        float y,
        WaterLevelGaugeMark mark,
        string? label,
        SKColor ink,
        SKColor fill,
        SKColor halo,
        float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        canvas.Save();
        try
        {
            canvas.Translate(x, y);
            canvas.Scale(scale);

            const float half = SquareSize / 2f;
            var square = new SKRect(-half, -half, half, half);
            using (var fillPaint = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill })
                canvas.DrawRoundRect(square, CornerRadius, CornerRadius, fillPaint);
            using (var stroke = Stroke(ink, SquareStrokeWidth))
                canvas.DrawRoundRect(square, CornerRadius, CornerRadius, stroke);

            DrawMark(canvas, mark, ink);

            if (label is not null && mark != WaterLevelGaugeMark.NoData)
                DrawHaloText(canvas, label, LabelOffsetX, LabelBaselineY, LabelTypeface, LabelFontSize, ink, halo, SKTextAlign.Left);
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>
    /// Draws <paramref name="text"/> with a halo stroked under the fill, the way
    /// SVG's <c>paint-order="stroke"</c> does.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="text">The text.</param>
    /// <param name="x">Anchor X, interpreted by <paramref name="align"/>.</param>
    /// <param name="baseline">Baseline Y.</param>
    /// <param name="typeface">The face.</param>
    /// <param name="size">Font size in pixels.</param>
    /// <param name="ink">Text colour.</param>
    /// <param name="halo">Halo colour.</param>
    /// <param name="align">Horizontal alignment about <paramref name="x"/>.</param>
    public static void DrawHaloText(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        SKTypeface typeface,
        float size,
        SKColor ink,
        SKColor halo,
        SKTextAlign align)
    {
        using var font = new SKFont(typeface, size) { Subpixel = true, Edging = SKFontEdging.Antialias };
        using var haloPaint = new SKPaint
        {
            Color = halo,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = HaloWidth,
            StrokeJoin = SKStrokeJoin.Round,
        };
        using var inkPaint = new SKPaint { Color = ink, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawText(text, x, baseline, align, font, haloPaint);
        canvas.DrawText(text, x, baseline, align, font, inkPaint);
    }

    private static void DrawMark(SKCanvas canvas, WaterLevelGaugeMark mark, SKColor ink)
    {
        switch (mark)
        {
            case WaterLevelGaugeMark.Rising:
                DrawPolyline(canvas, ink, (-4f, 2f), (0f, -2.5f), (4f, 2f));
                break;
            case WaterLevelGaugeMark.Falling:
                DrawPolyline(canvas, ink, (-4f, -2f), (0f, 2.5f), (4f, -2f));
                break;
            case WaterLevelGaugeMark.Steady:
                DrawPolyline(canvas, ink, (-4f, 0f), (4f, 0f));
                break;
            case WaterLevelGaugeMark.Unknown:
                DrawCentredText(canvas, "?", ink);
                break;
            case WaterLevelGaugeMark.NoData:
                DrawCentredText(canvas, "—", ink);
                break;
        }
    }

    private static void DrawPolyline(SKCanvas canvas, SKColor ink, params (float X, float Y)[] points)
    {
        using var path = new SKPath();
        path.MoveTo(points[0].X, points[0].Y);
        for (int i = 1; i < points.Length; i++)
            path.LineTo(points[i].X, points[i].Y);
        using var paint = Stroke(ink, MarkStrokeWidth);
        canvas.DrawPath(path, paint);
    }

    private static void DrawCentredText(SKCanvas canvas, string text, SKColor ink)
    {
        using var font = new SKFont(MarkTypeface, UnknownFontSize) { Subpixel = true, Edging = SKFontEdging.Antialias };
        font.MeasureText(text, out var bounds);
        using var paint = new SKPaint { Color = ink, IsAntialias = true, Style = SKPaintStyle.Fill };
        // Centre the glyph's ink box, not its advance box, so "?" sits mid-square.
        canvas.DrawText(text, -bounds.MidX, -bounds.MidY, SKTextAlign.Left, font, paint);
    }

    private static SKPaint Stroke(SKColor color, float width) =>
        new()
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

    /// <summary>
    /// The first installed family of <paramref name="families"/> in
    /// <paramref name="style"/>, or the renderer's default face.
    /// </summary>
    internal static SKTypeface MatchTypeface(SKFontStyle style, params string[] families)
    {
        foreach (var family in families)
        {
            var face = SKFontManager.Default.MatchFamily(family, style);
            if (RendererFonts.IsUsable(face))
                return face!;
        }
        return RendererFonts.Default;
    }
}
