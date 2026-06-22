using System.Collections.Concurrent;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
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
    /// Process-wide cache of parsed symbol pictures keyed by the resolved SVG
    /// content (<see cref="ResolvedSymbol.ProcessedSvg"/>). Parsing an SVG into
    /// an <see cref="SKPicture"/> via <see cref="SKSvg.CreateFromSvg(string)"/>
    /// is expensive, and the tiled subsystem's live overlay redraws every point
    /// symbol and sounding glyph on <i>every</i> frame (see
    /// <c>S100VectorTileRenderer.DrawOverlay</c>). The set of distinct symbol
    /// SVGs is small and bounded (symbol catalogue × palette), so caching the
    /// parsed picture across frames and tiles eliminates per-op re-parsing.
    /// </summary>
    /// <remarks>
    /// The cached value is the owning <see cref="SKSvg"/>, not the bare
    /// <see cref="SKPicture"/>: an <see cref="SKSvg"/> owns and disposes its
    /// <see cref="SKSvg.Picture"/>, so keeping a strong reference to the
    /// <see cref="SKSvg"/> keeps the picture's native resources alive (a GC'd
    /// <see cref="SKSvg"/> would finalise the picture out from under us). Entries
    /// are never evicted; the natural bound on distinct symbols keeps the cache
    /// small. <see cref="SKPicture"/> playback (<c>DrawPicture</c>) is
    /// thread-safe, and the cache is a <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// because tiles rasterise on background threads while the overlay draws on
    /// the render thread.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, SKSvg?> s_symbolPictureCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Half-extent, in display pixels, by which the point/text cull rectangle is
    /// grown beyond the viewport so a symbol or label whose <i>anchor</i> sits
    /// just off-screen but whose body is partly visible is still drawn. Sized to
    /// comfortably exceed the largest compound symbol / point-anchored label.
    /// Exposed so callers that supply an explicit cull rectangle (e.g. the live
    /// overlay under a rotated viewport) inflate by the same margin.
    /// </summary>
    public const float PointCullMarginPx = 256f;

    /// <summary>
    /// Returns the parsed picture for <paramref name="processedSvg"/>, parsing
    /// and caching it on first use. Returns <see langword="null"/> when the SVG
    /// cannot be parsed.
    /// </summary>
    private static SKPicture? GetSymbolPicture(string processedSvg)
    {
        var svg = s_symbolPictureCache.GetOrAdd(processedSvg, static content =>
        {
            try
            {
                return SKSvg.CreateFromSvg(content);
            }
            catch
            {
                return null;
            }
        });
        return svg?.Picture;
    }

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

        RenderOnto(canvas, scene, viewport);
        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// Draws <paramref name="scene"/> onto an existing <paramref name="canvas"/>
    /// using <paramref name="viewport"/>'s world→screen projection, without
    /// allocating or clearing a backing bitmap and without flushing the canvas.
    /// This lets a caller composite a display list directly onto a foreground
    /// surface — e.g. the tiled subsystem's live screen-space symbol/text
    /// overlay, which must be drawn at constant on-screen size (the tiled base
    /// plane is rasterised at a discrete band resolution and then scaled, so any
    /// op baked into it scales with zoom; ops drawn here against the live
    /// viewport do not).
    /// </summary>
    /// <param name="canvas">The destination canvas. Not cleared or flushed.</param>
    /// <param name="scene">The display list to draw.</param>
    /// <param name="viewport">The live viewport whose projection places the ops.</param>
    public void RenderOnto(SKCanvas canvas, VectorScene scene, Viewport viewport)
        => RenderOnto(canvas, scene, viewport, pointCullBounds: null);

    /// <summary>
    /// As <see cref="RenderOnto(SKCanvas, VectorScene, Viewport)"/>, but culls
    /// point and point-anchored text ops whose projected anchor falls outside
    /// <paramref name="pointCullBounds"/> (in viewport pixel space) before any
    /// per-op work — avoiding the cost of parsing a symbol SVG or measuring a
    /// label that cannot be visible. When <paramref name="pointCullBounds"/> is
    /// <see langword="null"/>, the cull rectangle is the viewport inflated by
    /// <see cref="PointCullMarginPx"/>. A caller that rotates the canvas (the
    /// live overlay under a rotated viewport) must pass an explicit rectangle
    /// expanded to the rotated viewport's bounding box, since this method draws
    /// in pre-rotation pixel space.
    /// </summary>
    /// <param name="canvas">The destination canvas. Not cleared or flushed.</param>
    /// <param name="scene">The display list to draw.</param>
    /// <param name="viewport">The live viewport whose projection places the ops.</param>
    /// <param name="pointCullBounds">
    /// Pixel-space rectangle outside which point/text ops are skipped, or
    /// <see langword="null"/> to derive it from the viewport plus the symbol
    /// margin.
    /// </param>
    public void RenderOnto(SKCanvas canvas, VectorScene scene, Viewport viewport, SKRect? pointCullBounds)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(viewport);

        var transform = WorldToScreen.Create(viewport);
        double denom = viewport.ScaleDenominator;

        var cullBounds = pointCullBounds ?? new SKRect(
            -PointCullMarginPx,
            -PointCullMarginPx,
            viewport.WidthPixels + PointCullMarginPx,
            viewport.HeightPixels + PointCullMarginPx);

        // Per-render cache of decoded pattern tiles, keyed by pattern
        // reference. Real S-101 cells can have many polygons sharing a single
        // pattern (e.g. quality-of-bathymetry overlays) so decoding the PNG
        // once and reusing the SKImage across ops is a meaningful saving.
        Dictionary<string, SKImage?>? patternImages = null;

        // Per-render reusable text resources. The live overlay redraws every
        // sounding/label per frame; allocating an SKFont + SKPaint per text op
        // (S-100 "All" scenes have thousands) churns native handles. A single
        // scratch reuses one paint and caches fonts by pixel size (soundings
        // share a size), disposed once when the render completes.
        TextDrawScratch? textScratch = null;

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
                        DrawPoint(canvas, point, transform, cullBounds);
                        break;
                    case TextPaintOp text:
                        textScratch ??= new TextDrawScratch();
                        DrawText(canvas, text, transform, cullBounds, textScratch);
                        break;
                }
            }
        }
        finally
        {
            if (patternImages is not null)
            {
                foreach (var img in patternImages.Values)
                    img?.Dispose();
            }
            textScratch?.Dispose();
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

        // Tiles are rasterized supersampled (PatternTileRenderPixelsPerMm); scale
        // the shader back down by PatternTileShaderScale so they draw at their
        // intended on-screen size. Downsampling a high-resolution tile keeps the
        // pattern crisp instead of blurring an upsampled low-resolution one.
        const float tileScale = (float)SkiaSvgRasterizer.PatternTileShaderScale;
        var (anchorX, anchorY) = t.Project((0, 0));
        var localMatrix = SKMatrix.Concat(
            SKMatrix.CreateTranslation(anchorX, anchorY),
            SKMatrix.CreateScale(tileScale, tileScale));
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        using var shader = tileImage.ToShader(
            SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling, localMatrix);

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

    private static void DrawPoint(SKCanvas canvas, PointPaintOp op, WorldToScreen t, SKRect cullBounds)
    {
        var (cx, cy) = t.Project(op.World);
        cx += (float)op.OffsetXpx;
        cy += (float)op.OffsetYpx;

        if (!cullBounds.Contains(cx, cy))
            return;

        if (op.Symbol is { } symbol)
        {
            var picture = GetSymbolPicture(symbol.ProcessedSvg);
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

                // The symbol's pivot point (S-100 Part 9 §11.5) must coincide
                // with the feature anchor, and any rotation/scale must be about
                // that pivot — not the bounding-box centre. Working entirely in
                // picture coordinates, the pivot is the bbox centre shifted by
                // the pivot fraction (PivotRelative = (centre − pivot) / size):
                //   pivot = bboxCentre − PivotRelative × bounds
                // Composing translate(anchor) → rotate → scale → translate(−pivot)
                // rotates and scales the glyph about its pivot while keeping the
                // pivot pinned to the anchor for *every* rotation. (Pre-rotation
                // pivot shifts in screen space only land correctly at 0°, which
                // left oriented secondary symbols — e.g. a buoy's light flare or
                // an offset colour symbol — drifting off the anchor; see #335.)
                float pivotPicX = bounds.Left + bounds.Width / 2f
                    - (float)(symbol.PivotRelativeX * bounds.Width);
                float pivotPicY = bounds.Top + bounds.Height / 2f
                    - (float)(symbol.PivotRelativeY * bounds.Height);

                canvas.Save();
                canvas.Translate(cx, cy);
                if (op.Rotation is { } rot)
                    canvas.RotateDegrees((float)rot);
                canvas.Scale(scale);
                canvas.Translate(-pivotPicX, -pivotPicY);
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

    private static void DrawText(SKCanvas canvas, TextPaintOp op, WorldToScreen t, SKRect cullBounds, TextDrawScratch scratch)
    {
        var (ax, ay) = t.Project(op.World);

        if (!cullBounds.Contains(ax + (float)op.OffsetXpx, ay + (float)op.OffsetYpx))
            return;

        var font = scratch.FontFor((float)op.FontSizePx);
        var paint = scratch.Paint;
        paint.Color = SKColors.Black;

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
            paint.Color = back.ToSkia();
            const float pad = 1.5f;
            var rect = new SKRect(
                x + textBounds.Left - pad,
                baseline + textBounds.Top - pad,
                x + textBounds.Left + textWidth + pad,
                baseline + textBounds.Top + textHeight + pad);
            canvas.DrawRect(rect, paint);
        }

        paint.Color = op.ForeColor.ToSkia();
        canvas.DrawText(op.Text, x, baseline, SKTextAlign.Left, font, paint);
    }

    /// <summary>
    /// Per-render scratch for text drawing: a single reusable
    /// <see cref="SKPaint"/> (its colour is reset per op) and a cache of
    /// <see cref="SKFont"/> keyed by pixel size. Avoids allocating native font
    /// and paint handles per text op, which matters for the live overlay that
    /// redraws thousands of soundings/labels every frame. Not thread-safe; one
    /// instance per <see cref="RenderOnto(SKCanvas, VectorScene, Viewport, SKRect?)"/>
    /// call. The paint defaults to <see cref="SKPaintStyle.Fill"/>, which is
    /// correct for both the glyph fill and the optional label background rect.
    /// </summary>
    private sealed class TextDrawScratch : IDisposable
    {
        private readonly Dictionary<float, SKFont> _fonts = new();

        /// <summary>The shared antialiased fill paint; set its colour per op.</summary>
        public SKPaint Paint { get; } = new() { IsAntialias = true };

        /// <summary>Returns a cached font for <paramref name="sizePx"/>, creating it on first use.</summary>
        public SKFont FontFor(float sizePx)
        {
            if (!_fonts.TryGetValue(sizePx, out var font))
            {
                font = new SKFont(RendererFonts.Default, sizePx);
                _fonts[sizePx] = font;
            }
            return font;
        }

        public void Dispose()
        {
            Paint.Dispose();
            foreach (var font in _fonts.Values)
                font.Dispose();
            _fonts.Clear();
        }
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
