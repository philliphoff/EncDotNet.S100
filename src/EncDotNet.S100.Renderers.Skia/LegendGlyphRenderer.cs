using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia;

/// <summary>
/// Draws the glyph of a legend row (<see cref="LegendEntry"/>) at legend
/// size: a colour swatch, an outlined area, a point symbol, or a short sample
/// of a line style with its symbols. The viewer's Layer stack shows these
/// next to each row's label.
/// </summary>
/// <remarks>
/// <para>
/// Glyphs are sized in logical pixels (see <see cref="SizeOf"/>): 14×10 for a
/// swatch or area, 14×14 for a symbol, 28×10 for a line. A line sample is
/// drawn at <see cref="MillimetresPerPixel"/>, smaller than on the chart, so
/// a front shows two or three pips; its symbols are the line style's own,
/// placed and turned as the chart renderer places them, and centred along
/// the sample.
/// </para>
/// </remarks>
public static class LegendGlyphRenderer
{
    /// <summary>
    /// The scale line samples, outlines and symbols along lines are drawn at:
    /// millimetres on the S-100 display surface per logical pixel. The chart
    /// draws at 0.32 mm per pixel, so the legend's are 2.5 times smaller.
    /// </summary>
    public const double MillimetresPerPixel = 0.8;

    /// <summary>The thinnest pen a legend glyph draws, in logical pixels, so a fine line stays visible.</summary>
    public const float MinimumPenWidth = 1f;

    /// <summary>The glyph size of <paramref name="entry"/>, in logical pixels.</summary>
    /// <param name="entry">The legend row.</param>
    public static (int Width, int Height) SizeOf(LegendEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry switch
        {
            SymbolLegendEntry => (14, 14),
            LineLegendEntry => (28, 10),
            _ => (14, 10),
        };
    }

    /// <summary>
    /// Renders the glyph of <paramref name="entry"/> to a new bitmap on a
    /// transparent background, <see cref="SizeOf"/> times
    /// <paramref name="deviceScale"/> pixels. The caller owns the bitmap.
    /// </summary>
    /// <param name="entry">The legend row.</param>
    /// <param name="deviceScale">Device pixels per logical pixel, e.g. 2 on a Retina display.</param>
    public static SKBitmap Render(LegendEntry entry, float deviceScale = 1f)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviceScale);

        var (width, height) = SizeOf(entry);
        var bitmap = new SKBitmap(
            (int)Math.Ceiling(width * deviceScale),
            (int)Math.Ceiling(height * deviceScale),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(deviceScale);
        Draw(canvas, entry, width, height);
        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// Draws the glyph of <paramref name="entry"/> into the rectangle from the
    /// canvas origin to (<paramref name="width"/>, <paramref name="height"/>),
    /// in the canvas's units.
    /// </summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="entry">The legend row.</param>
    /// <param name="width">The glyph width.</param>
    /// <param name="height">The glyph height.</param>
    public static void Draw(SKCanvas canvas, LegendEntry entry, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(entry);

        switch (entry)
        {
            case SwatchLegendEntry swatch:
                DrawSwatch(canvas, swatch, width, height);
                break;
            case SymbolLegendEntry symbol:
                DrawSymbol(canvas, symbol.Svg, width, height);
                break;
            case LineLegendEntry line:
                DrawLine(canvas, line.Line, width, height);
                break;
        }
    }

    private static void DrawSwatch(SKCanvas canvas, SwatchLegendEntry swatch, float width, float height)
    {
        var rect = new SKRect(0.5f, 0.5f, width - 0.5f, height - 0.5f);
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Parse(swatch.Color) };
        canvas.DrawRoundRect(rect, 2, 2, fill);

        if (swatch.Outline is not { } outline || outline.Width <= 0)
            return;

        float pen = PenWidth(outline.Width);
        float inset = pen / 2;
        var outlineRect = new SKRect(inset, inset, width - inset, height - inset);
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = pen,
            Color = Parse(outline.Color),
            PathEffect = DashEffect(outline),
        };
        canvas.DrawRoundRect(outlineRect, 2, 2, stroke);
    }

    private static void DrawSymbol(SKCanvas canvas, string svg, float width, float height)
    {
        var picture = SkiaDisplayListRenderer.GetSymbolPicture(svg);
        if (picture is null)
            return;

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        const float padding = 0.5f;
        float scale = Math.Min((width - 2 * padding) / bounds.Width, (height - 2 * padding) / bounds.Height);
        canvas.Save();
        canvas.Translate(width / 2f, height / 2f);
        canvas.Scale(scale);
        canvas.Translate(-bounds.MidX, -bounds.MidY);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private static void DrawLine(SKCanvas canvas, LegendLineSample line, float width, float height)
    {
        const float x0 = 1f;
        float x1 = width - 1f;
        float length = x1 - x0;
        float y = height / 2f;
        float interval = (float)(line.IntervalLength / MillimetresPerPixel);

        float symbolScale = (float)(1 / MillimetresPerPixel / (96 / 25.4));

        // Where the pattern's origin falls: the symbols are centred along the
        // sample, kept whole inside it, so the first starts the visible run,
        // not the style's start.
        float origin = x0;
        if (line.Symbols.Count > 0 && interval > 0)
        {
            float inset = Math.Min(line.Symbols.Max(s => HalfWidth(s, symbolScale)), length / 2f);
            float room = length - 2 * inset;
            float first = (float)(line.Symbols.Min(s => s.Position) / MillimetresPerPixel);
            float last = 0;
            for (int k = 0; ; k++)
            {
                bool any = false;
                foreach (var symbol in line.Symbols)
                {
                    float at = (float)(symbol.Position / MillimetresPerPixel) - first + k * interval;
                    if (at <= room)
                    {
                        last = Math.Max(last, at);
                        any = true;
                    }
                }

                if (!any)
                    break;
            }

            origin = x0 + inset + (room - last) / 2f - first;
        }

        if (line.Width > 0)
        {
            using var pen = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = PenWidth(line.Width),
                StrokeCap = SKStrokeCap.Butt,
                Color = Parse(line.Color),
            };

            if (line.Dashes.Count == 0 || interval <= 0)
            {
                canvas.DrawLine(x0, y, x1, y, pen);
            }
            else
            {
                // Each dash of each interval that reaches the sample, clipped to it.
                int firstInterval = (int)Math.Floor((x0 - origin) / interval) - 1;
                for (int k = firstInterval; origin + k * interval < x1; k++)
                {
                    foreach (var (start, dash) in line.Dashes)
                    {
                        float a = origin + k * interval + (float)(start / MillimetresPerPixel);
                        float b = a + (float)(dash / MillimetresPerPixel);
                        a = Math.Max(a, x0);
                        b = Math.Min(b, x1);
                        if (b > a)
                            canvas.DrawLine(a, y, b, y, pen);
                    }
                }
            }
        }

        if (line.Symbols.Count == 0 || interval <= 0)
            return;

        for (int k = 0; ; k++)
        {
            bool any = false;
            foreach (var symbol in line.Symbols)
            {
                float x = origin + (float)(symbol.Position / MillimetresPerPixel) + k * interval;
                if (x > x1 + 0.01f)
                    continue;
                any = true;
                if (x >= x0 - 0.01f)
                    DrawLineSymbol(canvas, symbol, x, y, symbolScale);
            }

            if (!any)
                break;
        }
    }

    // As the chart renderer draws a line's symbols: pivot on the line, turned
    // by the symbol's rotation relative to it (here, a line running to +x).
    private static void DrawLineSymbol(SKCanvas canvas, LegendLineSymbol symbol, float x, float y, float scale)
    {
        var picture = SkiaDisplayListRenderer.GetSymbolPicture(symbol.Svg);
        if (picture is null)
            return;

        var bounds = picture.CullRect;
        float pivotX = bounds.Left + bounds.Width / 2f - (float)(symbol.PivotRelativeX * bounds.Width);
        float pivotY = bounds.Top + bounds.Height / 2f - (float)(symbol.PivotRelativeY * bounds.Height);

        canvas.Save();
        canvas.Translate(x, y);
        canvas.RotateDegrees((float)symbol.Rotation);
        canvas.Scale(scale);
        canvas.Translate(-pivotX, -pivotY);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    // How far a symbol reaches along the line from its pivot, at the sample's scale.
    private static float HalfWidth(LegendLineSymbol symbol, float scale)
    {
        var picture = SkiaDisplayListRenderer.GetSymbolPicture(symbol.Svg);
        if (picture is null)
            return 0;
        var bounds = picture.CullRect;
        return (float)(Math.Max(bounds.Width, bounds.Height) / 2 * scale);
    }

    private static float PenWidth(double millimetres) =>
        Math.Max((float)(millimetres / MillimetresPerPixel), MinimumPenWidth);

    // The dashes of an outline as a Skia dash effect: on/off lengths over one
    // interval, starting at the first dash.
    private static SKPathEffect? DashEffect(LegendLineSample line)
    {
        if (line.Dashes.Count == 0 || line.IntervalLength <= 0)
            return null;

        var intervals = new float[line.Dashes.Count * 2];
        for (int i = 0; i < line.Dashes.Count; i++)
        {
            var (start, length) = line.Dashes[i];
            double next = i + 1 < line.Dashes.Count
                ? line.Dashes[i + 1].Start
                : line.IntervalLength + line.Dashes[0].Start;
            intervals[2 * i] = (float)Math.Max(length / MillimetresPerPixel, 0.5);
            intervals[2 * i + 1] = (float)Math.Max((next - start - length) / MillimetresPerPixel, 0.5);
        }

        return SKPathEffect.CreateDash(intervals, 0);
    }

    private static SKColor Parse(string color) =>
        SKColor.TryParse(color, out var parsed) ? parsed : SKColors.Transparent;
}
