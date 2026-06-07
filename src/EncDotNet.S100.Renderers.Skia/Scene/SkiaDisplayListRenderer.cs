using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using SkiaSharp;
using Svg.Skia;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// A headless, direct-SkiaSharp renderer for the S-100 Part 9 vector
/// intermediate representation produced by <see cref="VectorSceneBuilder"/>.
/// Rasterises a <see cref="VectorScene"/> against a <see cref="Viewport"/> into
/// a standalone <see cref="SKBitmap"/> — the vector analogue of
/// <see cref="SkiaCoverageRenderer"/>, suitable for a tile-serving web API with
/// no Mapsui / GUI dependency.
/// </summary>
/// <remarks>
/// <para>The renderer supplies the second half of the S-100 projection that the
/// Mapsui backend delegates to its navigator: it projects the
/// <see cref="Viewport"/> geographic bounds to EPSG:3857 and maps that
/// rectangle linearly to pixels. Geometry is transformed world→screen, but
/// stroke widths, symbol sizes, and text sizes are realised in display pixels
/// per the IR unit contract (see <see cref="PaintOp"/>).</para>
/// <para><b>Scope.</b> This renders point, line, solid-area, tiled pattern-area,
/// and text ops. Antimeridian crossing and Web-Mercator pole limits are out
/// of scope.</para>
/// </remarks>
public sealed class SkiaDisplayListRenderer
{
    /// <summary>Background colour cleared before painting. Defaults to transparent.</summary>
    public RgbaColor Background { get; set; } = RgbaColor.Transparent;

    /// <summary>
    /// Whether to apply S-100 Part 9 §11.1 scale-visibility culling using the
    /// viewport's <see cref="Viewport.ScaleDenominator"/>. Defaults to
    /// <see langword="true"/> (an explicit viewport carries a meaningful scale).
    /// Auto-fit / "render the whole dataset" callers set this to
    /// <see langword="false"/>, because the denominator synthesised from a
    /// fitted extent is not the dataset's compilation scale and would otherwise
    /// wrongly cull scale-ranged detail.
    /// </summary>
    public bool HonorScaleVisibility { get; set; } = true;

    /// <summary>
    /// Renders the scene at the requested viewport, returning a new bitmap of
    /// <see cref="Viewport.WidthPixels"/> × <see cref="Viewport.HeightPixels"/>.
    /// The caller owns the returned bitmap.
    /// </summary>
    public SKBitmap Render(VectorScene scene, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(viewport);

        var bitmap = new SKBitmap(
            viewport.WidthPixels, viewport.HeightPixels, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Background.ToSkia());

        var transform = WorldToScreen.Create(viewport);
        double denom = viewport.ScaleDenominator;

        // Per-render cache of decoded pattern tiles, keyed by pattern
        // reference. Real S-101 cells can have many polygons sharing a single
        // pattern (e.g. quality-of-bathymetry overlays) so decoding the PNG
        // once and reusing the SKImage across ops is a meaningful saving.
        Dictionary<string, SKImage?>? patternImages = null;

        try
        {
            foreach (var op in scene.Ops)
            {
                if (HonorScaleVisibility && !ScaleVisibility.IsVisibleAtScale(op, denom))
                    continue;

                switch (op)
                {
                    case AreaPaintOp area:
                        DrawArea(canvas, area, transform);
                        break;
                    case PatternAreaPaintOp pattern:
                        patternImages ??= new Dictionary<string, SKImage?>(StringComparer.Ordinal);
                        DrawPatternArea(canvas, pattern, transform, patternImages);
                        break;
                    case LinePaintOp line:
                        DrawLine(canvas, line, transform);
                        break;
                    case PointPaintOp point:
                        DrawPoint(canvas, point, transform);
                        break;
                    case TextPaintOp text:
                        DrawText(canvas, text, transform);
                        break;
                }
            }

            canvas.Flush();
            return bitmap;
        }
        finally
        {
            if (patternImages is not null)
            {
                foreach (var img in patternImages.Values)
                    img?.Dispose();
            }
        }
    }

    private static void DrawArea(SKCanvas canvas, AreaPaintOp op, WorldToScreen t)
    {
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        AddRing(path, op.WorldShell, t);
        foreach (var hole in op.WorldHoles)
            AddRing(path, hole, t);

        using var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = op.Fill.ToSkia(),
        };
        canvas.DrawPath(path, fill);

        if (op.OutlineWidthPx > 0 && op.OutlineColor.A > 0)
        {
            using var outline = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = op.OutlineColor.ToSkia(),
                StrokeWidth = (float)op.OutlineWidthPx,
            };
            canvas.DrawPath(path, outline);
        }
    }

    /// <summary>
    /// Fills the polygon with the op's tiled pattern. The repeat anchor is the
    /// world (0, 0) point projected to screen space, matching the Mapsui
    /// <c>AnchoredPatternFillStyle</c> contract so the pattern grid is global
    /// (overlapping polygons sharing a pattern align seamlessly across their
    /// boundary, avoiding moiré).
    /// </summary>
    private static void DrawPatternArea(
        SKCanvas canvas,
        PatternAreaPaintOp op,
        WorldToScreen t,
        Dictionary<string, SKImage?> imageCache)
    {
        if (op.WorldShell.Count < 3)
            return;

        if (!imageCache.TryGetValue(op.PatternReference, out var tileImage))
        {
            tileImage = SKImage.FromEncodedData(op.TilePng);
            imageCache[op.PatternReference] = tileImage;
        }
        if (tileImage is null)
            return;

        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        AddRing(path, op.WorldShell, t);
        foreach (var hole in op.WorldHoles)
            AddRing(path, hole, t);

        var (anchorX, anchorY) = t.Project((0, 0));
        var anchorMatrix = SKMatrix.CreateTranslation(anchorX, anchorY);
        using var shader = tileImage.ToShader(
            SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, anchorMatrix);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = shader,
        };

        canvas.Save();
        canvas.ClipPath(path, antialias: true);
        canvas.DrawRect(path.Bounds, paint);
        canvas.Restore();
    }

    private static void DrawLine(SKCanvas canvas, LinePaintOp op, WorldToScreen t)
    {
        if (op.World.Count < 2)
            return;

        using var path = new SKPath();
        var (sx, sy) = t.Project(op.World[0]);
        path.MoveTo(sx, sy);
        for (int i = 1; i < op.World.Count; i++)
        {
            var (px, py) = t.Project(op.World[i]);
            path.LineTo(px, py);
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = op.Color.ToSkia(),
            StrokeWidth = (float)op.WidthPx,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        if (op.DashArrayPx is { Count: > 0 })
        {
            paint.PathEffect = SKPathEffect.CreateDash(op.DashArrayPx.ToArray(), 0f);
        }
        else if (op.DefaultDash)
        {
            float d = (float)Math.Max(op.WidthPx * 3.0, 3.0);
            paint.PathEffect = SKPathEffect.CreateDash([d, d], 0f);
        }

        canvas.DrawPath(path, paint);
        paint.PathEffect?.Dispose();
    }

    private static void DrawPoint(SKCanvas canvas, PointPaintOp op, WorldToScreen t)
    {
        var (cx, cy) = t.Project(op.World);
        cx += (float)op.OffsetXpx;
        cy += (float)op.OffsetYpx;

        if (op.Symbol is { } symbol)
        {
            using var svg = SKSvg.CreateFromSvg(symbol.ProcessedSvg);
            var picture = svg?.Picture;
            if (picture is not null)
            {
                var bounds = picture.CullRect;
                // Svg.Skia already rasterises the SVG's millimetre dimensions to
                // pixels at 96 DPI, so CullRect is in display pixels (e.g. a
                // 3.98 mm symbol → 15 px). The symbol is therefore drawn at its
                // natural pixel size times the global symbol scale — matching the
                // Mapsui ImageStyle convention (SymbolScale applied to the same
                // CullRect). Applying a further mm→px factor here would oversize
                // every symbol by ~3.78×.
                float scale = (float)symbol.Scale;
                float w = bounds.Width * scale;
                float h = bounds.Height * scale;

                // Place the bbox centre on the anchor, then shift by the pivot
                // fraction so the pivot — not the bbox centre — lands on it.
                float pivotShiftX = (float)(symbol.PivotRelativeX * w);
                float pivotShiftY = (float)(symbol.PivotRelativeY * h);

                canvas.Save();
                canvas.Translate(cx + pivotShiftX, cy + pivotShiftY);
                if (op.Rotation is { } rot)
                    canvas.RotateDegrees((float)rot);
                canvas.Scale(scale);
                canvas.Translate(-(bounds.Left + bounds.Width / 2f), -(bounds.Top + bounds.Height / 2f));
                canvas.DrawPicture(picture);
                canvas.Restore();
                return;
            }
        }

        // Fallback: a small filled dot, sized like the legacy SymbolStyle dot.
        float radius = (float)Math.Max(op.FallbackScale * 12.0, 1.0);
        using var dot = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = op.FallbackColor.ToSkia(),
        };
        canvas.DrawCircle(cx, cy, radius, dot);
    }

    private static void DrawText(SKCanvas canvas, TextPaintOp op, WorldToScreen t)
    {
        var (ax, ay) = t.Project(op.World);

        using var font = new SKFont(RendererFonts.Default, (float)op.FontSizePx);
        using var paint = new SKPaint { IsAntialias = true };

        font.MeasureText(op.Text, out var textBounds, paint);
        float textWidth = textBounds.Width;
        float textHeight = textBounds.Height;

        // Resolve the anchor according to alignment (screen +Y = down).
        float x = op.HorizontalAlignment switch
        {
            TextHorizontalAlignment.Start => ax,
            TextHorizontalAlignment.End => ax - textWidth,
            _ => ax - textWidth / 2f,
        };
        float baseline = op.VerticalAlignment switch
        {
            TextVerticalAlignment.Top => ay - textBounds.Top,
            TextVerticalAlignment.Bottom => ay - textBounds.Bottom,
            _ => ay - textBounds.MidY,
        };

        x += (float)op.OffsetXpx;
        baseline += (float)op.OffsetYpx;

        if (op.BackColor is { } back)
        {
            using var bg = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = back.ToSkia() };
            const float pad = 1.5f;
            var rect = new SKRect(
                x + textBounds.Left - pad,
                baseline + textBounds.Top - pad,
                x + textBounds.Left + textWidth + pad,
                baseline + textBounds.Top + textHeight + pad);
            canvas.DrawRect(rect, bg);
        }

        paint.Color = op.ForeColor.ToSkia();
        canvas.DrawText(op.Text, x, baseline, SKTextAlign.Left, font, paint);
    }

    private static void AddRing(SKPath path, IReadOnlyList<(double X, double Y)> ring, WorldToScreen t)
    {
        if (ring.Count < 3)
            return;
        var (sx, sy) = t.Project(ring[0]);
        path.MoveTo(sx, sy);
        for (int i = 1; i < ring.Count; i++)
        {
            var (px, py) = t.Project(ring[i]);
            path.LineTo(px, py);
        }
        path.Close();
    }
}

/// <summary>
/// A linear EPSG:3857-world → screen-pixel affine derived from a
/// <see cref="Viewport"/>. The viewport's geographic bounds are projected to
/// EPSG:3857 and mapped to the pixel rectangle (origin top-left, +Y down).
/// </summary>
internal readonly struct WorldToScreen
{
    private readonly double _minX;
    private readonly double _maxY;
    private readonly double _scaleX;
    private readonly double _scaleY;

    private WorldToScreen(double minX, double maxY, double scaleX, double scaleY)
    {
        _minX = minX;
        _maxY = maxY;
        _scaleX = scaleX;
        _scaleY = scaleY;
    }

    public static WorldToScreen Create(Viewport viewport)
    {
        var (minX, minY) = WebMercator.FromLonLat(viewport.MinLongitude, viewport.MinLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(viewport.MaxLongitude, viewport.MaxLatitude);

        double spanX = maxX - minX;
        double spanY = maxY - minY;
        double scaleX = spanX != 0 ? viewport.WidthPixels / spanX : 0;
        double scaleY = spanY != 0 ? viewport.HeightPixels / spanY : 0;
        return new WorldToScreen(minX, maxY, scaleX, scaleY);
    }

    public (float X, float Y) Project((double X, double Y) world)
    {
        float sx = (float)((world.X - _minX) * _scaleX);
        float sy = (float)((_maxY - world.Y) * _scaleY);
        return (sx, sy);
    }
}
