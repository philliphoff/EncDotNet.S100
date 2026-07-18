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
public sealed class SkiaDisplayListRenderer : IVectorSceneRenderer<SKCanvas>
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
    /// Whether to apply the antimeridian seam-wrap in <see cref="WorldToScreen"/>
    /// (wrapping each op's world-X into the viewport's shifted longitude window
    /// when <c>MaxLongitude &gt; 180</c> or <c>MinLongitude &lt; −180</c>).
    /// Defaults to <see langword="true"/> so the headless single-viewport
    /// auto-fit path (issue #413) can gather geometry across the ±180° seam.
    /// <para>
    /// The <b>tiled</b> subsystem sets this to <see langword="false"/>: it
    /// rasterises each tile from a narrow per-tile viewport over geometry that is
    /// already positioned in a <i>continuous</i> EPSG:3857 X frame (longitudes
    /// may exceed +180° without wrapping). Under a per-tile window whose bounds
    /// both lie east of +180°, the seam-wrap would teleport the far vertices of
    /// large polygons that extend west of the tile back across the world,
    /// smearing them across the tile. Disabling the wrap keeps continuous
    /// geometry continuous; off-tile vertices simply project outside the tile
    /// and are clipped.
    /// </para>
    /// </summary>
    public bool EnableSeamWrap { get; set; } = true;

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
    /// Process-wide cache of rasterised symbol <i>sprites</i> for the live
    /// overlay's symbol atlas (#332 lever c2), keyed by
    /// (processed SVG, quantised symbol scale, quantised device scale). Replaying
    /// a vector <see cref="SKPicture"/> per point op per frame is the overlay hot
    /// path's steepest cost on dense "All" cells (~4 µs/op); blitting a
    /// once-rasterised <see cref="SKImage"/> instead removes the per-frame replay.
    /// <para>
    /// The sprite is rasterised at <c>symbolScale × deviceScale</c> device pixels
    /// so the 1:1 blit through the canvas's HiDPI matrix is crisp; a different
    /// device scale (e.g. moving the window to another monitor) keys a fresh
    /// sprite. <see cref="SKImage"/> is immutable and its CPU pixels are safe to
    /// share across threads, so — like <see cref="s_symbolPictureCache"/> — this
    /// is a never-evicted <see cref="ConcurrentDictionary{TKey,TValue}"/> bounded
    /// by the small number of distinct symbols in a cell. A <see langword="null"/>
    /// value caches "do not atlas this symbol" (unparseable, or larger than
    /// <see cref="MaxSpriteDimensionPx"/>) so the miss is not re-probed each frame.
    /// </para>
    /// </summary>
    private static readonly ConcurrentDictionary<(string Svg, int ScaleMilli, int DeviceMilli), SymbolSprite?> s_symbolSpriteCache = new();

    /// <summary>
    /// Sampling for the atlas blit. The sprite is rasterised at device resolution
    /// and drawn 1:1 in device space, so linear sampling only matters for the
    /// fractional sub-pixel anchor placement; it matches the antialiased vector
    /// edge to within a pixel.
    /// </summary>
    private static readonly SKSamplingOptions s_spriteSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    /// <summary>
    /// Maximum width/height (device px) of a cached symbol sprite. Symbols larger
    /// than this fall back to the vector <c>DrawPicture</c> path rather than
    /// rasterising a large image; normal point symbols are a few tens of pixels.
    /// </summary>
    private const int MaxSpriteDimensionPx = 1024;

    /// <summary>A once-rasterised symbol sprite plus the picture bounds it covers.</summary>
    private sealed class SymbolSprite
    {
        /// <summary>The rasterised symbol, in device pixels at the keyed scale.</summary>
        public required SKImage Image { get; init; }

        /// <summary>The symbol picture's cull rect (display px), for pivot/placement math.</summary>
        public required SKRect Bounds { get; init; }
    }

    /// <summary>
    /// Process-wide cache of "does this typeface contain every glyph in this
    /// string" results, keyed by (face, text). The glyph-coverage probe
    /// (<see cref="SKTypeface.ContainsGlyphs(string)"/>) allocates a
    /// <c>ushort[text.Length]</c> and runs a full codepoint→glyph mapping pass,
    /// so calling it per text op per frame would heap-allocate and double the
    /// glyph mapping on the live overlay's hot path (the dominant all-ASCII
    /// soundings/labels case). Caching by text makes stable frames re-scan
    /// nothing. Entries are never evicted; the natural bound on distinct label
    /// strings (feature names and sounding values in a cell) keeps it small. A
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> because tiles and the
    /// overlay may probe coverage from different threads.
    /// </summary>
    private static readonly ConcurrentDictionary<(SKTypeface Face, string Text), bool> s_primaryCoverageCache = new();

    /// <summary>
    /// Process-wide cache of fallback typefaces resolved via
    /// <see cref="SKFontManager.MatchCharacter(int)"/>, keyed by Unicode
    /// codepoint. <c>MatchCharacter</c> enumerates the platform font set and is
    /// expensive, so this must outlive any single frame: held for the app
    /// lifetime (entries never evicted, faces never disposed), mirroring
    /// <see cref="s_symbolPictureCache"/>. A <see langword="null"/> value caches
    /// "no platform fallback exists" so the miss is not re-probed every frame.
    /// </summary>
    private static readonly ConcurrentDictionary<int, SKTypeface?> s_fallbackFaceCache = new();

    /// <summary>
    /// Process-wide cache of fallback <see cref="SKFont"/>s keyed by their
    /// typeface and pixel size, so a non-ASCII label does not allocate and
    /// destroy a native font handle every frame. App-lifetime, like
    /// <see cref="s_fallbackFaceCache"/>.
    /// </summary>
    private static readonly ConcurrentDictionary<(SKTypeface Face, float SizePx), SKFont> s_fallbackFontCache = new();

    /// <summary>
    /// Returns whether <paramref name="face"/> can render every glyph in
    /// <paramref name="text"/>, caching the result per (face, text) so the
    /// allocating full-string probe runs at most once per distinct string. See
    /// <see cref="s_primaryCoverageCache"/>.
    /// </summary>
    private static bool PrimaryRendersAll(SKTypeface face, string text) =>
        s_primaryCoverageCache.GetOrAdd((face, text), static key => key.Face.ContainsGlyphs(key.Text));

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
    internal static SKPicture? GetSymbolPicture(string processedSvg)
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
    /// Draws <paramref name="scene"/> onto <paramref name="surface"/> for the
    /// pluggable <see cref="IVectorSceneRenderer{TSurface}"/> seam. Delegates to
    /// <see cref="RenderOnto(SKCanvas, VectorScene, Viewport)"/>; the canvas is
    /// neither cleared nor flushed.
    /// </summary>
    /// <param name="surface">The destination canvas.</param>
    /// <param name="scene">The display list to draw, in Part 9 draw order.</param>
    /// <param name="viewport">The viewport whose projection places the ops.</param>
    void IVectorSceneRenderer<SKCanvas>.Render(SKCanvas surface, VectorScene scene, Viewport viewport)
        => RenderOnto(surface, scene, viewport);

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
        => RenderOnto(canvas, scene, viewport, new OverlayDrawOptions { PointCullBounds = pointCullBounds });

    /// <summary>
    /// As <see cref="RenderOnto(SKCanvas, VectorScene, Viewport, SKRect?)"/>, but
    /// driven by <paramref name="options"/> so the tiled subsystem's live label
    /// plane can: suppress decluttered text
    /// (<see cref="OverlayDrawOptions.SuppressedText"/>), keep label glyphs
    /// <b>upright</b> under a rotated viewport by rotating each text
    /// <i>anchor</i> about the screen centre while drawing glyphs axis-aligned
    /// (<see cref="OverlayDrawOptions.TextAnchorRotationDegrees"/>), and draw the
    /// point and text passes separately
    /// (<see cref="OverlayDrawOptions.DrawPoints"/> /
    /// <see cref="OverlayDrawOptions.DrawText"/>). The defaults reproduce the
    /// plain overlay behaviour (draw everything, no suppression, no rotation).
    /// </summary>
    /// <param name="canvas">The destination canvas. Not cleared or flushed.</param>
    /// <param name="scene">The display list to draw.</param>
    /// <param name="viewport">The live viewport whose projection places the ops.</param>
    /// <param name="options">Overlay draw controls; see <see cref="OverlayDrawOptions"/>.</param>
    public void RenderOnto(SKCanvas canvas, VectorScene scene, Viewport viewport, OverlayDrawOptions options)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(options);

        var transform = WorldToScreen.Create(viewport, EnableSeamWrap);
        double denom = viewport.ScaleDenominator;

        var cullBounds = options.PointCullBounds ?? new SKRect(
            -PointCullMarginPx,
            -PointCullMarginPx,
            viewport.WidthPixels + PointCullMarginPx,
            viewport.HeightPixels + PointCullMarginPx);

        var suppressed = options.SuppressedText;
        double textRotationDeg = options.TextAnchorRotationDegrees;
        float centerX = options.ScreenCenterX;
        float centerY = options.ScreenCenterY;

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

        // Per-render reusable line resources; see LineDrawScratch. Lazily
        // created because text/point-only overlay passes draw no lines.
        LineDrawScratch? lineScratch = null;

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
                        lineScratch ??= new LineDrawScratch();
                        DrawLine(canvas, line, transform, cullBounds, lineScratch);
                        break;
                    case PointPaintOp point when options.DrawPoints:
                        DrawPoint(canvas, point, transform, cullBounds,
                            options.UseSymbolAtlas, options.DeviceScale);
                        break;
                    case TextPaintOp text when options.DrawText:
                        if (suppressed is not null && suppressed.Contains(text))
                            break;
                        textScratch ??= new TextDrawScratch();
                        DrawText(canvas, text, transform, cullBounds, textScratch,
                            textRotationDeg, centerX, centerY);
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
            lineScratch?.Dispose();
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

    private static void DrawLine(
        SKCanvas canvas, LinePaintOp op, WorldToScreen t, SKRect cullBounds, LineDrawScratch scratch)
    {
        int count = op.World.Count;
        if (count < 2)
            return;

        // Project every vertex once, into the scratch buffer, tracking the
        // polyline's screen-space bounding box so an off-view line can be culled
        // before the (native) DrawPath — matching DrawPoint's cull discipline.
        var points = scratch.RentPoints(count);
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            var (px, py) = t.Project(op.World[i]);
            points[i] = new SKPoint(px, py);
            if (px < minX) minX = px;
            if (px > maxX) maxX = px;
            if (py < minY) minY = py;
            if (py > maxY) maxY = py;
        }

        float width = (float)op.WidthPx;

        // Pad the bbox by half the stroke width so a wide line whose centreline
        // sits just outside the cull rectangle (but whose stroke would still
        // paint inside it) is not wrongly culled. Round joins/caps stay within
        // half the stroke width of the geometry.
        float pad = width * 0.5f;
        if (maxX + pad < cullBounds.Left || minX - pad > cullBounds.Right ||
            maxY + pad < cullBounds.Top || minY - pad > cullBounds.Bottom)
        {
            return;
        }

        var path = scratch.Path;
        path.Rewind();
        path.AddPoly(points.AsSpan(0, count), close: false);

        var paint = scratch.Paint;
        paint.Color = op.Color.ToSkia();
        paint.StrokeWidth = width;

        if (op.DashArrayPx is { Count: > 0 } dashArray)
        {
            int n = dashArray.Count;
            Span<float> intervals = n <= 16 ? stackalloc float[n] : new float[n];
            for (int i = 0; i < n; i++)
                intervals[i] = dashArray[i];
            paint.PathEffect = scratch.DashFor(intervals, 0f);
        }
        else if (op.DefaultDash)
        {
            float d = (float)Math.Max(op.WidthPx * 3.0, 3.0);
            Span<float> intervals = [d, d];
            paint.PathEffect = scratch.DashFor(intervals, 0f);
        }
        else
        {
            paint.PathEffect = null;
        }

        canvas.DrawPath(path, paint);
    }

    private static void DrawPoint(SKCanvas canvas, PointPaintOp op, WorldToScreen t, SKRect cullBounds,
        bool useAtlas = false, float deviceScale = 1f)
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

                // #332 lever c2 — atlas the common upright case as a cached sprite
                // blit. The sprite is rasterised at scale×deviceScale device px,
                // so placing it in a logical-size dest rect blits 1:1 through the
                // HiDPI matrix — identical pixels to replaying the picture, minus
                // the per-frame vector replay. Per-op-rotated symbols (oriented
                // lights/secondary symbols) keep the vector path for exact parity.
                if (useAtlas && op.Rotation is null)
                {
                    var sprite = GetSymbolSprite(symbol.ProcessedSvg, scale, deviceScale, picture, bounds);
                    if (sprite is not null)
                    {
                        float destLeft = cx + (bounds.Left - pivotPicX) * scale;
                        float destTop = cy + (bounds.Top - pivotPicY) * scale;
                        var dest = new SKRect(
                            destLeft, destTop,
                            destLeft + bounds.Width * scale,
                            destTop + bounds.Height * scale);
                        canvas.DrawImage(sprite.Image, dest, s_spriteSampling);
                        return;
                    }
                }

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

    /// <summary>
    /// Returns the cached device-resolution sprite for an upright symbol at the
    /// given symbol/device scale, rasterising it once on first use. Returns
    /// <see langword="null"/> (cached) when the symbol should not be atlased
    /// (degenerate bounds, or larger than <see cref="MaxSpriteDimensionPx"/>),
    /// signalling the caller to use the vector path.
    /// </summary>
    private static SymbolSprite? GetSymbolSprite(
        string processedSvg, float scale, float deviceScale, SKPicture picture, SKRect bounds)
    {
        var key = (processedSvg,
            (int)MathF.Round(scale * 1000f),
            (int)MathF.Round(deviceScale * 1000f));

        return s_symbolSpriteCache.GetOrAdd(key, _ =>
        {
            float r = scale * deviceScale;
            if (!(r > 0) || bounds.Width <= 0 || bounds.Height <= 0)
                return null;

            int widthPx = (int)MathF.Ceiling(bounds.Width * r);
            int heightPx = (int)MathF.Ceiling(bounds.Height * r);
            if (widthPx < 1 || heightPx < 1 ||
                widthPx > MaxSpriteDimensionPx || heightPx > MaxSpriteDimensionPx)
            {
                return null;
            }

            var info = new SKImageInfo(widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface is null)
                return null;

            var c = surface.Canvas;
            c.Clear(SKColors.Transparent);
            c.Scale(r);
            c.Translate(-bounds.Left, -bounds.Top);
            c.DrawPicture(picture);
            c.Flush();

            return new SymbolSprite { Image = surface.Snapshot(), Bounds = bounds };
        });
    }

    private static void DrawText(
        SKCanvas canvas, TextPaintOp op, WorldToScreen t, SKRect cullBounds, TextDrawScratch scratch,
        double anchorRotationDeg, float centerX, float centerY)
    {
        var (ax, ay) = t.Project(op.World);

        // Upright-under-rotation: rotate the label *anchor* about the screen
        // centre to match where the rotated base/point passes place the feature,
        // but draw the glyphs axis-aligned (this method never rotates the
        // canvas), so the text stays horizontal. North-up (deg == 0) is a no-op.
        (ax, ay) = RotateAbout(ax, ay, centerX, centerY, anchorRotationDeg);

        if (!cullBounds.Contains(ax + (float)op.OffsetXpx, ay + (float)op.OffsetYpx))
            return;

        var font = scratch.FontFor((float)op.FontSizePx);
        var paint = scratch.Paint;

        var layout = LayoutText(op, ax, ay, font, paint);

        if (op.BackColor is { } back)
        {
            paint.Color = back.ToSkia();
            canvas.DrawRect(layout.Background, paint);
        }

        paint.Color = op.ForeColor.ToSkia();
        DrawRunsWithFallback(canvas, op.Text, layout.X, layout.Baseline, (float)op.FontSizePx, font, paint, scratch);
    }

    /// <summary>
    /// Rotates the screen point (<paramref name="x"/>, <paramref name="y"/>)
    /// about (<paramref name="cx"/>, <paramref name="cy"/>) by
    /// <paramref name="degrees"/>, in the same sense as
    /// <see cref="SKCanvas.RotateDegrees(float, float, float)"/> (screen +Y down).
    /// A zero angle returns the point unchanged. Used to keep label anchors
    /// aligned with a rotated viewport while glyphs stay upright.
    /// </summary>
    internal static (float X, float Y) RotateAbout(float x, float y, float cx, float cy, double degrees)
    {
        if (degrees == 0)
            return (x, y);

        double rad = degrees * Math.PI / 180.0;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);
        double dx = x - cx;
        double dy = y - cy;
        return ((float)(cx + dx * cos - dy * sin), (float)(cy + dx * sin + dy * cos));
    }

    /// <summary>
    /// Computes the draw origin and the ink/background rectangle for a label,
    /// given an already-rotated screen anchor (<paramref name="ax"/>,
    /// <paramref name="ay"/>). Shared by <see cref="DrawText"/> (which draws at
    /// the returned origin) and <see cref="LabelDeclutterer"/> (which uses the
    /// returned rectangle as the label's collision footprint), so both agree on
    /// exactly where a label lands. The rectangle includes the same small pad as
    /// the optional background box.
    /// </summary>
    internal static (float X, float Baseline, SKRect Background) LayoutText(
        TextPaintOp op, float ax, float ay, SKFont font, SKPaint paint)
    {
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

        const float pad = 1.5f;
        var background = new SKRect(
            x + textBounds.Left - pad,
            baseline + textBounds.Top - pad,
            x + textBounds.Left + textWidth + pad,
            baseline + textBounds.Top + textHeight + pad);

        return (x, baseline, background);
    }

    /// <summary>
    /// Draws <paramref name="text"/> with per-run font fallback. When the primary
    /// face has every glyph (the common ASCII case for soundings and most
    /// labels) the whole string is drawn in one call. Otherwise the string is
    /// split into runs of consecutive codepoints the primary face can render and
    /// runs it cannot; each missing run is drawn with a fallback face resolved
    /// via <see cref="SKFontManager.MatchCharacter(int)"/>, advancing the pen by
    /// each run's measured width. This avoids <c>.notdef</c> "tofu" boxes for
    /// codepoints absent from the primary face.
    /// </summary>
    private static void DrawRunsWithFallback(
        SKCanvas canvas, string text, float x, float baseline, float sizePx,
        SKFont primary, SKPaint paint, TextDrawScratch scratch)
    {
        if (string.IsNullOrEmpty(text) || primary.Typeface is null || PrimaryRendersAll(primary.Typeface, text))
        {
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, primary, paint);
            return;
        }

        var primaryFace = primary.Typeface;
        var runs = SegmentRuns(text, cp => primaryFace.ContainsGlyph(cp) ? null : scratch.FallbackFor(cp));

        float penX = x;
        foreach (var (start, length, face) in runs)
        {
            var runFont = face is SKTypeface fallback ? scratch.FallbackFontFor(fallback, sizePx) : primary;
            string run = text.Substring(start, length);
            canvas.DrawText(run, penX, baseline, SKTextAlign.Left, runFont, paint);
            penX += runFont.MeasureText(run, paint);
        }
    }

    /// <summary>
    /// Splits <paramref name="text"/> into maximal runs of consecutive
    /// codepoints served by the same face, where <paramref name="resolveFace"/>
    /// returns the face to use for a Unicode codepoint (<see langword="null"/>
    /// meaning "use the primary face" — either it has the glyph or no fallback
    /// exists). Runs are compared by reference identity so a shared fallback face
    /// is drawn in one call. Pure and codepoint-aware (handles surrogate pairs),
    /// exposed for deterministic testing of the run segmentation independent of
    /// the platform font manager.
    /// </summary>
    internal static List<(int Start, int Length, object? Face)> SegmentRuns(
        string text, Func<int, object?> resolveFace)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(resolveFace);

        var runs = new List<(int, int, object?)>();
        if (text.Length == 0)
            return runs;

        int runStart = 0;
        object? runFace = resolveFace(char.ConvertToUtf32(text, 0));
        int i = char.IsSurrogatePair(text, 0) ? 2 : 1;

        while (i < text.Length)
        {
            object? face = resolveFace(char.ConvertToUtf32(text, i));
            if (!ReferenceEquals(face, runFace))
            {
                runs.Add((runStart, i - runStart, runFace));
                runStart = i;
                runFace = face;
            }
            i += char.IsSurrogatePair(text, i) ? 2 : 1;
        }

        runs.Add((runStart, text.Length - runStart, runFace));
        return runs;
    }

    /// <summary>
    /// Computes the on-screen bounding rectangle of a point symbol (or fallback
    /// dot), centred on its already-rotated screen anchor. Used by
    /// <see cref="LabelDeclutterer"/> so labels avoid colliding with symbols
    /// (S-100 Part 9 draw order keeps symbols on top of yielding text). The box
    /// is centred on the anchor; the small pivot asymmetry is immaterial for a
    /// collision obstacle.
    /// </summary>
    internal static SKRect PointScreenBounds(PointPaintOp op, float cx, float cy)
    {
        float halfW, halfH;
        if (op.Symbol is { } symbol && GetSymbolPicture(symbol.ProcessedSvg) is { } picture)
        {
            var bounds = picture.CullRect;
            float scale = (float)symbol.Scale;
            halfW = Math.Max(bounds.Width * scale / 2f, 1f);
            halfH = Math.Max(bounds.Height * scale / 2f, 1f);
        }
        else
        {
            float radius = (float)Math.Max(op.FallbackScale * 12.0, 1.0);
            halfW = halfH = radius;
        }

        return new SKRect(cx - halfW, cy - halfH, cx + halfW, cy + halfH);
    }

    /// <summary>
    /// Per-render scratch for text drawing: a single reusable
    /// <see cref="SKPaint"/> (its colour is reset per op) and a cache of
    /// <see cref="SKFont"/> keyed by pixel size, plus per-codepoint fallback
    /// faces (and their fonts) resolved on demand for missing-glyph runs. Avoids
    /// allocating native font and paint handles per text op, which matters for
    /// the live overlay that redraws thousands of soundings/labels every frame.
    /// Not thread-safe; one instance per
    /// <see cref="RenderOnto(SKCanvas, VectorScene, Viewport, OverlayDrawOptions)"/>
    /// call. The paint defaults to <see cref="SKPaintStyle.Fill"/>, which is
    /// correct for both the glyph fill and the optional label background rect.
    /// </summary>
    internal sealed class TextDrawScratch : IDisposable
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

        /// <summary>
        /// Returns a typeface that can render <paramref name="codepoint"/> when
        /// the primary face cannot, or <see langword="null"/> when the platform
        /// font manager has no match. Resolved via the app-lifetime
        /// <see cref="s_fallbackFaceCache"/> (the platform lookup is expensive
        /// and must not re-run per frame).
        /// </summary>
        public SKTypeface? FallbackFor(int codepoint) =>
            s_fallbackFaceCache.GetOrAdd(codepoint, static cp =>
            {
                try
                {
                    return SKFontManager.Default.MatchCharacter(cp);
                }
                catch
                {
                    return null;
                }
            });

        /// <summary>Returns a cached font for a fallback <paramref name="face"/> at <paramref name="sizePx"/> from the app-lifetime <see cref="s_fallbackFontCache"/>.</summary>
        public SKFont FallbackFontFor(SKTypeface face, float sizePx) =>
            s_fallbackFontCache.GetOrAdd((face, sizePx), static key => new SKFont(key.Face, key.SizePx));

        public void Dispose()
        {
            Paint.Dispose();
            foreach (var font in _fonts.Values)
                font.Dispose();
            _fonts.Clear();
        }
    }

    /// <summary>
    /// Per-render reusable resources for <see cref="DrawLine"/>. A full S-57
    /// exchange set emits thousands of line ops per tile; allocating a fresh
    /// <see cref="SKPath"/>, <see cref="SKPaint"/>, and dash
    /// <see cref="SKPathEffect"/> per op (the previous implementation) churned
    /// native handles and dominated pan/zoom CPU (finalizer/GC pressure plus the
    /// per-op interop). This scratch reuses one path (reset via
    /// <see cref="SKPath.Rewind"/>), one stroke paint (only colour/width/effect
    /// mutated per op), a growable projection buffer fed to
    /// <see cref="SKPath.AddPoly(ReadOnlySpan{SKPoint}, bool)"/> in a single
    /// interop call, and caches dash effects by pattern so shared dashes are
    /// created once.
    /// </summary>
    /// <remarks>
    /// Instances are per-<c>RenderOnto</c> locals (like
    /// <see cref="TextDrawScratch"/>), never shared statics or instance fields:
    /// the tiled subsystem's overlay renderer is a shared instance, and tile
    /// base-plane workers each rasterise on their own thread, so per-invocation
    /// scoping keeps the mutable scratch confined to a single thread.
    /// </remarks>
    private sealed class LineDrawScratch : IDisposable
    {
        private readonly Dictionary<DashKey, SKPathEffect> _dashEffects = new();
        private SKPoint[] _points = new SKPoint[64];

        /// <summary>The reusable stroke path; call <see cref="SKPath.Rewind"/> before reuse.</summary>
        public SKPath Path { get; } = new();

        /// <summary>
        /// The reusable stroke paint. Round cap/join and antialiasing are fixed;
        /// only <see cref="SKPaint.Color"/>, <see cref="SKPaint.StrokeWidth"/>,
        /// and <see cref="SKPaint.PathEffect"/> are mutated per op.
        /// </summary>
        public SKPaint Paint { get; } = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        /// <summary>Returns a projection buffer with capacity for at least <paramref name="count"/> points.</summary>
        public SKPoint[] RentPoints(int count)
        {
            if (_points.Length < count)
            {
                int size = _points.Length;
                while (size < count)
                    size *= 2;
                _points = new SKPoint[size];
            }
            return _points;
        }

        /// <summary>
        /// Returns a cached dash effect for <paramref name="intervals"/> and
        /// <paramref name="phase"/>, creating it on first use. The returned
        /// effect is owned by the scratch and must not be disposed by the caller.
        /// </summary>
        public SKPathEffect DashFor(ReadOnlySpan<float> intervals, float phase)
        {
            var key = new DashKey(intervals, phase);
            if (!_dashEffects.TryGetValue(key, out var effect))
            {
                effect = SKPathEffect.CreateDash(intervals.ToArray(), phase);
                _dashEffects[key] = effect;
            }
            return effect;
        }

        public void Dispose()
        {
            Path.Dispose();
            Paint.Dispose();
            foreach (var effect in _dashEffects.Values)
                effect.Dispose();
            _dashEffects.Clear();
        }

        /// <summary>Value-equal key over a dash interval pattern and phase, so distinct op instances sharing a pattern reuse one effect.</summary>
        private readonly struct DashKey : IEquatable<DashKey>
        {
            private readonly float[] _intervals;
            private readonly float _phase;
            private readonly int _hash;

            public DashKey(ReadOnlySpan<float> intervals, float phase)
            {
                _intervals = intervals.ToArray();
                _phase = phase;
                var hash = new HashCode();
                foreach (var value in intervals)
                    hash.Add(value);
                hash.Add(phase);
                _hash = hash.ToHashCode();
            }

            public bool Equals(DashKey other)
            {
                if (_phase != other._phase || _intervals.Length != other._intervals.Length)
                    return false;
                for (int i = 0; i < _intervals.Length; i++)
                {
                    if (_intervals[i] != other._intervals[i])
                        return false;
                }
                return true;
            }

            public override bool Equals(object? obj) => obj is DashKey other && Equals(other);

            public override int GetHashCode() => _hash;
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

