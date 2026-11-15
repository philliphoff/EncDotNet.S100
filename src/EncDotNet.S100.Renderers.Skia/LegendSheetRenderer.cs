using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia;

/// <summary>Colours and sizes of a legend sheet (<see cref="LegendSheetRenderer"/>).</summary>
public sealed record LegendSheetStyle
{
    /// <summary>The sheet's background; transparent for none.</summary>
    public SKColor Background { get; init; } = SKColors.White;

    /// <summary>The colour of titles and row labels.</summary>
    public SKColor Text { get; init; } = new(0x20, 0x20, 0x20);

    /// <summary>The colour of captions and group titles.</summary>
    public SKColor SecondaryText { get; init; } = new(0x60, 0x60, 0x60);

    /// <summary>The tile each glyph sits on, as the chart's background sits under the map (legends D10); transparent for none.</summary>
    public SKColor GlyphTile { get; init; } = SKColors.Transparent;

    /// <summary>The width of each row's glyph, in logical pixels.</summary>
    public int GlyphWidth { get; init; } = DefaultGlyphWidth;

    /// <summary>The height of each row's glyph, in logical pixels.</summary>
    public int GlyphHeight { get; init; } = DefaultGlyphHeight;

    /// <summary>Device pixels per logical pixel: 2 for a sheet twice as many pixels across.</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>The default <see cref="GlyphWidth"/>: a line sample's width.</summary>
    public const int DefaultGlyphWidth = 28;

    /// <summary>The default <see cref="GlyphHeight"/>.</summary>
    public const int DefaultGlyphHeight = 14;
}

/// <summary>
/// Draws product legends (<see cref="ProductLegend"/>) as one image: each
/// product's title, its caption, its group titles when it has more than one
/// group, and its rows, each a glyph (<see cref="LegendGlyphRenderer"/>) and
/// its label. GIS clients show it as a layer's legend graphic.
/// </summary>
public static class LegendSheetRenderer
{
    private const float Padding = 8;
    private const float GlyphGap = 6;
    private const float RowGap = 4;
    private const float SectionGap = 10;
    private const float TitleSize = 13;
    private const float CaptionSize = 11;
    private const float LabelSize = 12;
    private const float TileInset = 1;

    /// <summary>
    /// The size of the sheet <see cref="Render"/> draws, in device pixels,
    /// without drawing it.
    /// </summary>
    /// <param name="legends">The product legends, top first.</param>
    /// <param name="style">The colours and sizes.</param>
    public static (int Width, int Height) Measure(IReadOnlyList<ProductLegend> legends, LegendSheetStyle style)
    {
        ArgumentNullException.ThrowIfNull(legends);
        ArgumentNullException.ThrowIfNull(style);
        var (width, height) = Layout(legends, style, canvas: null);
        return (Device(width, style.Scale), Device(height, style.Scale));
    }

    /// <summary>
    /// Draws the legends to a new bitmap of <see cref="Measure"/>'s size. With
    /// no rows, the sheet is a small blank image. The caller owns the bitmap.
    /// </summary>
    /// <param name="legends">The product legends, top first.</param>
    /// <param name="style">The colours and sizes.</param>
    public static SKBitmap Render(IReadOnlyList<ProductLegend> legends, LegendSheetStyle style)
    {
        ArgumentNullException.ThrowIfNull(legends);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(style.Scale);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(style.GlyphWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(style.GlyphHeight);

        var (width, height) = Measure(legends, style);
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(style.Background);
        canvas.Scale(style.Scale);
        Layout(legends, style, canvas);
        canvas.Flush();
        return bitmap;
    }

    private static int Device(float logical, float scale) => Math.Max(1, (int)Math.Ceiling(logical * scale));

    /// <summary>Lays the sheet out, drawing it when <paramref name="canvas"/> is given; returns its logical size.</summary>
    private static (float Width, float Height) Layout(IReadOnlyList<ProductLegend> legends, LegendSheetStyle style, SKCanvas? canvas)
    {
        var typeface = RendererFonts.Default;
        using var title = new SKFont(typeface, TitleSize) { Embolden = true, Subpixel = true };
        using var caption = new SKFont(typeface, CaptionSize) { Subpixel = true };
        using var groupTitle = new SKFont(typeface, CaptionSize) { Embolden = true, Subpixel = true };
        using var label = new SKFont(typeface, LabelSize) { Subpixel = true };
        using var ink = new SKPaint { IsAntialias = true, Color = style.Text };
        using var secondary = new SKPaint { IsAntialias = true, Color = style.SecondaryText };
        using var tile = new SKPaint { IsAntialias = true, Color = style.GlyphTile };

        float glyphWidth = style.GlyphWidth, glyphHeight = style.GlyphHeight;
        float right = Padding, y = Padding;
        bool first = true;
        foreach (var legend in legends)
        {
            if (!legend.Rows.Any())
                continue;
            if (!first)
                y += SectionGap;
            first = false;

            Line(TitleOf(legend), title, ink, Padding);
            if (!string.IsNullOrWhiteSpace(legend.Caption))
                Line(legend.Caption, caption, secondary, Padding);

            foreach (var group in legend.Groups)
            {
                if (group.Rows.Count == 0)
                    continue;
                if (legend.Groups.Count > 1)
                    Line(group.Title, groupTitle, secondary, Padding);

                foreach (var row in group.Rows)
                {
                    float rowHeight = Math.Max(glyphHeight, LineHeight(label));
                    float glyphTop = y + ((rowHeight - glyphHeight) / 2);
                    if (canvas is not null)
                    {
                        if (style.GlyphTile.Alpha > 0)
                        {
                            canvas.DrawRect(
                                new SKRect(Padding - TileInset, glyphTop - TileInset, Padding + glyphWidth + TileInset, glyphTop + glyphHeight + TileInset),
                                tile);
                        }

                        canvas.Save();
                        canvas.Translate(Padding, glyphTop);
                        canvas.ClipRect(new SKRect(0, 0, glyphWidth, glyphHeight));
                        LegendGlyphRenderer.Draw(canvas, row, glyphWidth, glyphHeight);
                        canvas.Restore();
                    }

                    float textX = Padding + glyphWidth + GlyphGap;
                    float baseline = y + (rowHeight / 2) - ((label.Metrics.Ascent + label.Metrics.Descent) / 2);
                    canvas?.DrawText(row.Label, textX, baseline, SKTextAlign.Left, label, ink);
                    right = Math.Max(right, textX + label.MeasureText(row.Label));
                    y += rowHeight + RowGap;
                }
            }
        }

        if (first)
            return (1, 1); // nothing to show
        return (right + Padding, y - RowGap + Padding);

        void Line(string text, SKFont font, SKPaint paint, float x)
        {
            float height = LineHeight(font);
            canvas?.DrawText(text, x, y - font.Metrics.Ascent, SKTextAlign.Left, font, paint);
            right = Math.Max(right, x + font.MeasureText(text));
            y += height + RowGap;
        }
    }

    private static float LineHeight(SKFont font) => font.Metrics.Descent - font.Metrics.Ascent;

    /// <summary>"Bathymetric surface (S-102)", or the spec alone when it is the title.</summary>
    private static string TitleOf(ProductLegend legend) =>
        string.IsNullOrEmpty(legend.Spec) || string.Equals(legend.Title, legend.Spec, StringComparison.OrdinalIgnoreCase)
            ? legend.Title
            : $"{legend.Title} ({legend.Spec})";
}
