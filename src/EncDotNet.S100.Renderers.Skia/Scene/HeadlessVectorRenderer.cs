using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// Source-agnostic entry point for headless vector rendering: lowers any
/// S-100 Part 9 display list (point/line/solid-area/pattern-area/text) through
/// the shared <see cref="VectorSceneBuilder"/> and rasterises it with
/// <see cref="SkiaDisplayListRenderer"/>, auto-fitting the viewport to the
/// resolved scene's EPSG:3857 extent unless an explicit viewport is supplied.
/// Works for any vector product (S-101
/// ISO 8211, S-12x / S-201 / S-421 GML, …) because it depends only on the
/// encoding-agnostic <see cref="DrawingInstruction"/> /
/// <see cref="IFeatureGeometryProvider"/> contract — not on Mapsui.
/// </summary>
/// <remarks>
/// Tiled pattern area-fills are supported when an <c>areaFillProvider</c> is
/// supplied (see <see cref="Render"/>); the renderer rasterises each
/// referenced pattern tile via <see cref="SkiaSvgRasterizer.RasterizePatternTile"/>
/// and tiles it across the polygon. Draw order follows the shared core's
/// S-100 Part 9 ordering (solid areas → pattern areas → lines → points →
/// text within a plane) — the same ordering each Mapsui layer applies —
/// but a single headless bitmap does not reproduce the S-101 processor's
/// two-layer area/non-area split (that split exists only to interleave
/// S-102), nor the Mapsui pattern phase's NetTopologySuite priority-clipping
/// against higher-priority patterns and opaque solid fills.
/// </remarks>
public static class HeadlessVectorRenderer
{
    /// <summary>
    /// Renders a display list to a standalone bitmap. By default the viewport is
    /// auto-fitted to the scene extent and scale-visibility culling is disabled
    /// (the fitted scale is not a meaningful compilation scale), so all resolved
    /// ops are drawn; pass an explicit <paramref name="viewport"/> to frame an
    /// exact window and enable scale-visibility culling.
    /// </summary>
    /// <param name="instructions">The Part 9 display list to render.</param>
    /// <param name="geometryProvider">Resolves feature geometry referenced by the instructions.</param>
    /// <param name="palette">Active colour palette for token resolution.</param>
    /// <param name="symbolProvider">Resolves a symbol name to raw SVG content (pre-processing), or null.</param>
    /// <param name="lineStyleProvider">Resolves a line-style name to its catalogue definition, or null.</param>
    /// <param name="symbolScale">Global symbol scale factor.</param>
    /// <param name="textScale">Global text scale factor.</param>
    /// <param name="widthPixels">Output bitmap width.</param>
    /// <param name="heightPixels">Output bitmap height.</param>
    /// <param name="background">Background fill colour.</param>
    /// <param name="areaFillProvider">
    /// Optional resolver for area-fill catalogue entries by name. When supplied
    /// together with <paramref name="symbolProvider"/>, area instructions with
    /// an <c>AreaFillReference</c> are rasterised into tiled pattern fills via
    /// <see cref="SkiaSvgRasterizer.RasterizePatternTile"/>. When omitted,
    /// pattern areas are skipped (matching legacy behaviour).
    /// </param>
    /// <param name="hiddenCategories">
    /// Bitmask of instruction categories to suppress from the output (S-100
    /// Part 9 instruction types — areas, lines, points, text). Defaults to
    /// <see cref="DrawingInstructionCategory.None"/> (render everything).
    /// Hiding categories filters the display list before lowering, so the
    /// remaining instructions keep their priorities, viewing-group state, and
    /// extent. The auto-fitted viewport is computed from the filtered scene,
    /// matching what the user actually sees.
    /// </param>
    /// <param name="basemap">
    /// Optional basemap drawn <em>beneath</em> the dataset (issue #411). When
    /// <see cref="BasemapKind.Offline"/>, the bundled Natural Earth land layer
    /// (<see cref="NaturalEarthBasemap.LandScene"/>) is painted first, against
    /// the same auto-fitted viewport, so it registers exactly with the chart.
    /// Defaults to <see cref="BasemapKind.None"/> (no basemap; output unchanged).
    /// </param>
    /// <param name="viewport">
    /// Optional explicit display window. When <c>null</c> (the default) the
    /// viewport is auto-fitted to the scene extent (padded 10%, aspect-matched)
    /// and scale-visibility culling is disabled. When supplied, that exact
    /// window is rendered <em>and</em> S-100 Part 9 scale-visibility culling is
    /// enabled — because an explicit viewport carries a meaningful
    /// <see cref="Viewport.ScaleDenominator"/> — bringing this path into
    /// alignment with the composite/GUI render paths.
    /// </param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public static SKBitmap Render(
        IReadOnlyList<DrawingInstruction> instructions,
        IFeatureGeometryProvider geometryProvider,
        ColorPalette palette,
        Func<string, string?>? symbolProvider,
        Func<string, LineStyle?>? lineStyleProvider,
        double symbolScale,
        double textScale,
        int widthPixels,
        int heightPixels,
        RgbaColor background,
        Func<string, AreaFill?>? areaFillProvider = null,
        DrawingInstructionCategory hiddenCategories = DrawingInstructionCategory.None,
        BasemapKind basemap = BasemapKind.None,
        Viewport? viewport = null)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(geometryProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);

        if (hiddenCategories != DrawingInstructionCategory.None)
            instructions = FilterInstructions(instructions, hiddenCategories);

        var scene = BuildScene(
            instructions,
            geometryProvider,
            palette,
            symbolProvider,
            lineStyleProvider,
            symbolScale,
            textScale,
            areaFillProvider);

        // An explicit viewport frames an exact window and carries a real scale
        // denominator, so honour scale-visibility; the auto-fit fallback uses a
        // synthetic scale, so culling stays disabled (draw every resolved op).
        bool honorScaleVisibility = viewport is not null;
        var resolvedViewport = viewport ?? FitViewport(scene, widthPixels, heightPixels);

        if (basemap == BasemapKind.Offline)
        {
            // Paint the land basemap under the dataset against the SAME
            // viewport so the two register exactly. The compositor clears the
            // background once, then draws each transparent layer bottom-first.
            var compositeRenderer = new HeadlessCompositeRenderer { Background = background };
            var layers = new CompositeLayer[]
            {
                new VectorCompositeLayer(NaturalEarthBasemap.LandScene, honorScaleVisibility: false),
                new VectorCompositeLayer(scene, honorScaleVisibility),
            };
            return compositeRenderer.Render(resolvedViewport, layers);
        }

        var renderer = new SkiaDisplayListRenderer
        {
            Background = background,
            HonorScaleVisibility = honorScaleVisibility,
        };
        return renderer.Render(scene, resolvedViewport);
    }

    /// <summary>
    /// Lowers a Part 9 display list into a resolved <see cref="VectorScene"/>
    /// (EPSG:3857 world geometry with paint state) without rasterising it. This
    /// is the reusable lowering seam shared by the single-dataset
    /// <see cref="Render"/> path and the multi-layer headless compositor, which
    /// needs the scene to draw it against a <em>shared</em> viewport rather than
    /// an auto-fitted one.
    /// </summary>
    /// <param name="instructions">The Part 9 display list to lower.</param>
    /// <param name="geometryProvider">Resolves feature geometry referenced by the instructions.</param>
    /// <param name="palette">Active colour palette for token resolution.</param>
    /// <param name="symbolProvider">Resolves a symbol name to raw SVG content (pre-processing), or null.</param>
    /// <param name="lineStyleProvider">Resolves a line-style name to its catalogue definition, or null.</param>
    /// <param name="symbolScale">Global symbol scale factor.</param>
    /// <param name="textScale">Global text scale factor.</param>
    /// <param name="areaFillProvider">Optional resolver for area-fill catalogue entries (tiled patterns).</param>
    /// <param name="hiddenCategories">
    /// Drawing-instruction categories (areas, lines, points, text) to omit from
    /// the lowered scene. Defaults to
    /// <see cref="DrawingInstructionCategory.None"/> (lower everything). Used by
    /// the multi-layer compositor to apply a global suppression across layers.
    /// </param>
    /// <returns>The resolved vector scene.</returns>
    public static VectorScene BuildScene(
        IReadOnlyList<DrawingInstruction> instructions,
        IFeatureGeometryProvider geometryProvider,
        ColorPalette palette,
        Func<string, string?>? symbolProvider,
        Func<string, LineStyle?>? lineStyleProvider,
        double symbolScale,
        double textScale,
        Func<string, AreaFill?>? areaFillProvider = null,
        DrawingInstructionCategory hiddenCategories = DrawingInstructionCategory.None)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(geometryProvider);

        if (hiddenCategories != DrawingInstructionCategory.None)
            instructions = FilterInstructions(instructions, hiddenCategories);

        var builder = new VectorSceneBuilder
        {
            ResolveColor = ColorResolver.Create(palette),
            SymbolResolver = symbolProvider is null
                ? null
                : name => VectorSceneBuilder.ResolveSymbolAsset(symbolProvider(name), palette),
            LineStyleProvider = lineStyleProvider,
            PatternResolver = BuildPatternResolver(symbolProvider, areaFillProvider, palette),
            SymbolScale = symbolScale,
            TextScale = textScale,
        };

        return builder.Build(instructions, geometryProvider);
    }

    /// <summary>
    /// Builds a tile resolver that rasterises pattern fills via
    /// <see cref="SkiaSvgRasterizer.RasterizePatternTile"/>. Tiles are cached
    /// per render invocation so multiple polygons sharing the same pattern do
    /// not re-rasterise it. Returns <see langword="null"/> when either of the
    /// upstream providers is unavailable (patterns then remain skipped).
    /// </summary>
    private static Func<string, byte[]?>? BuildPatternResolver(
        Func<string, string?>? symbolProvider,
        Func<string, AreaFill?>? areaFillProvider,
        ColorPalette palette)
    {
        if (symbolProvider is null || areaFillProvider is null)
            return null;

        var cache = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        return fillName =>
        {
            if (cache.TryGetValue(fillName, out var cached))
                return cached;

            byte[]? tile = null;
            try
            {
                var areaFill = areaFillProvider(fillName);
                if (areaFill?.PatternSymbol is { } symbolName)
                {
                    var svgContent = symbolProvider(symbolName);
                    if (!string.IsNullOrEmpty(svgContent))
                    {
                        var processed = SvgProcessor.Process(svgContent, palette);
                        tile = SkiaSvgRasterizer.RasterizePatternTile(processed, areaFill);
                    }
                }
            }
            catch
            {
                // Bad area fill / symbol — drop the pattern silently, matching
                // the Mapsui ProducePatternTile behaviour.
                tile = null;
            }

            cache[fillName] = tile;
            return tile;
        };
    }

    /// <summary>
    /// Produces a blank bitmap of the requested size filled with
    /// <paramref name="background"/>. Used when a pre-render gate (e.g. an
    /// S-411 time-window suppression) means the dataset contributes no
    /// portrayal for the current context.
    /// </summary>
    /// <param name="widthPixels">Output bitmap width.</param>
    /// <param name="heightPixels">Output bitmap height.</param>
    /// <param name="background">Background fill colour.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public static SKBitmap RenderBlank(int widthPixels, int heightPixels, RgbaColor background)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);

        var bitmap = new SKBitmap(
            widthPixels, heightPixels, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(background.ToSkia());
        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// Builds a <see cref="Viewport"/> fitted to a resolved scene's EPSG:3857
    /// extent, padded so the projected aspect ratio matches the requested pixel
    /// rectangle (the renderer scales X and Y independently, so matching the
    /// aspect avoids distortion). The geographic bounds are recovered from the
    /// projected extent via <see cref="WebMercator.ToLonLat"/>.
    /// </summary>
    public static Viewport FitViewport(VectorScene scene, int widthPixels, int heightPixels)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (!TryGetSeamAwareWorldBounds(scene, out double minX, out double minY, out double maxX, out double maxY))
        {
            // No geometry — fall back to a small extent around the origin.
            minX = -1000; minY = -1000; maxX = 1000; maxY = 1000;
        }

        double spanX = maxX - minX;
        double spanY = maxY - minY;

        // Pad 10% (and guard zero-span degenerate extents).
        double padX = spanX > 0 ? spanX * 0.1 : 1000;
        double padY = spanY > 0 ? spanY * 0.1 : 1000;
        minX -= padX; maxX += padX;
        minY -= padY; maxY += padY;
        spanX = maxX - minX;
        spanY = maxY - minY;

        // Expand the smaller dimension so the extent's aspect matches the output.
        double viewAspect = (double)widthPixels / heightPixels;
        double dataAspect = spanX / spanY;
        if (dataAspect > viewAspect)
        {
            double targetSpanY = spanX / viewAspect;
            double grow = (targetSpanY - spanY) / 2.0;
            minY -= grow; maxY += grow;
        }
        else
        {
            double targetSpanX = spanY * viewAspect;
            double grow = (targetSpanX - spanX) / 2.0;
            minX -= grow; maxX += grow;
        }

        // Lossless (unclamped) inverse for the viewport corners so the renderer
        // reproduces these exact world bounds (a pole-overhanging edge clamped
        // to ±85° would drift geometry poleward). See WebMercator.ToLonLat.
        var (minLon, minLat) = WebMercator.ToLonLat(minX, minY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(maxX, maxY, clampLatitude: false);

        // Approximate scale denominator (used only if a caller re-enables
        // culling): mercator metres-per-pixel, corrected to ground metres by
        // cos(midLat), divided by the S-100 standard 0.00028 m/px screen pitch.
        // Clamp the mid-latitude to the display limit so an overhanging edge
        // cannot drive cos(midLat) to ~0 and blow up the denominator.
        double midLat = Math.Clamp((minLat + maxLat) * 0.5, -WebMercator.MaxLatitude, WebMercator.MaxLatitude);
        double midLatRad = midLat * Math.PI / 180.0;
        double groundMetresPerPixel = (maxX - minX) / widthPixels * Math.Cos(midLatRad);
        double denom = groundMetresPerPixel / ScaleVisibility.DenomToResolutionMetres;

        return new Viewport
        {
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            WidthPixels = widthPixels,
            HeightPixels = heightPixels,
            ScaleDenominator = denom > 0 ? denom : 1.0,
        };
    }

    /// <summary>
    /// Computes a <em>seam-aware</em> EPSG:3857 bounding box for the scene: when
    /// the geometry straddles the ±180° antimeridian the returned window is
    /// shifted into a contiguous longitude frame (its <paramref name="maxX"/> may
    /// exceed +½ <see cref="WebMercator.Circumference"/>), so the fitted viewport
    /// frames the data on its true extent instead of collapsing to a near-global
    /// span (issue #413). Delegates the detection to
    /// <see cref="SeamAwareBoundsAccumulator"/>. Returns <see langword="false"/>
    /// when the scene has no geometry to bound.
    /// </summary>
    public static bool TryGetSeamAwareWorldBounds(
        VectorScene scene, out double minX, out double minY, out double maxX, out double maxY)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var accumulator = new SeamAwareBoundsAccumulator();
        accumulator.AddScene(scene);
        return accumulator.TryResolve(out minX, out minY, out maxX, out maxY);
    }

    /// <summary>
    /// Computes the EPSG:3857 bounding box spanning every resolved paint op's
    /// world geometry. Returns <see langword="false"/> when the scene has no
    /// geometry to bound. Exposed so the multi-layer compositor can union each
    /// vector layer's projected extent into a shared viewport.
    /// </summary>
    /// <remarks>
    /// This is the <em>naive</em> min/max — it does not account for the ±180°
    /// antimeridian seam. Use <see cref="TryGetSeamAwareWorldBounds"/> (or feed
    /// the scene to a <see cref="SeamAwareBoundsAccumulator"/>) when fitting an
    /// auto-viewport so datasets crossing the dateline are framed correctly.
    /// </remarks>
    public static bool TryGetWorldBounds(
        VectorScene scene, out double minX, out double minY, out double maxX, out double maxY)
    {
        double loX = double.MaxValue, loY = double.MaxValue;
        double hiX = double.MinValue, hiY = double.MinValue;
        bool any = false;

        void Expand(double x, double y)
        {
            any = true;
            if (x < loX) loX = x;
            if (x > hiX) hiX = x;
            if (y < loY) loY = y;
            if (y > hiY) hiY = y;
        }

        foreach (var op in scene.Ops)
        {
            switch (op)
            {
                case PointPaintOp p:
                    Expand(p.World.X, p.World.Y);
                    break;
                case TextPaintOp t:
                    Expand(t.World.X, t.World.Y);
                    break;
                case LinePaintOp l:
                    foreach (var (x, y) in l.World) Expand(x, y);
                    break;
                case AreaPaintOp a:
                    foreach (var (x, y) in a.WorldShell) Expand(x, y);
                    foreach (var hole in a.WorldHoles)
                        foreach (var (x, y) in hole) Expand(x, y);
                    break;
            }
        }

        minX = loX; minY = loY; maxX = hiX; maxY = hiY;
        return any;
    }

    /// <summary>
    /// Returns a copy of <paramref name="instructions"/> with every entry whose
    /// type is included in <paramref name="hidden"/> removed. The relative
    /// order of the surviving instructions is preserved so downstream
    /// priority / S-100 Part 9 type sorting in
    /// <see cref="VectorSceneBuilder"/> behaves identically.
    /// </summary>
    private static IReadOnlyList<DrawingInstruction> FilterInstructions(
        IReadOnlyList<DrawingInstruction> instructions,
        DrawingInstructionCategory hidden)
    {
        var kept = new List<DrawingInstruction>(instructions.Count);
        foreach (var inst in instructions)
        {
            var category = inst switch
            {
                AreaInstruction => DrawingInstructionCategory.Areas,
                LineInstruction => DrawingInstructionCategory.Lines,
                PointInstruction => DrawingInstructionCategory.Points,
                TextInstruction => DrawingInstructionCategory.Text,
                _ => DrawingInstructionCategory.None,
            };
            if ((hidden & category) == 0)
                kept.Add(inst);
        }
        return kept;
    }
}
