using EncDotNet.S100.Pipelines.Vector;
using SkiaSharp;
using Svg.Skia;

namespace EncDotNet.S100.Renderers.Skia;

/// <summary>
/// Rasterizes processed S-100 SVG symbols into tiled pattern bitmaps using SkiaSharp.
/// </summary>
public static class SkiaSvgRasterizer
{
    /// <summary>
    /// Intended <em>on-screen</em> pixels-per-mm density of a pattern tile, i.e.
    /// the effective size at which a tile is drawn after the renderer applies
    /// <see cref="PatternTileShaderScale"/>. S-100 defines pattern dimensions in
    /// mm for paper charts (~3.78 px/mm at 96 DPI). For interactive display a
    /// lower density is used so patterns repeat more tightly relative to the
    /// on-screen polygon size.
    /// </summary>
    public const double DefaultPixelsPerMm = 1.5;

    /// <summary>
    /// Supersampling factor applied when rasterizing pattern tiles. Tiles are
    /// rendered at <see cref="PatternTileRenderPixelsPerMm"/> (this factor times
    /// <see cref="DefaultPixelsPerMm"/>) and then scaled back down to their
    /// on-screen size by the renderer via <see cref="PatternTileShaderScale"/>.
    /// Rendering above the on-screen density keeps tiles crisp when the shader
    /// is sampled at fractional offsets and on HiDPI/Retina surfaces, where a
    /// tile rasterized at the on-screen density alone would be upsampled and
    /// appear blurry.
    /// </summary>
    public const double PatternTileSupersample = 4.0;

    /// <summary>
    /// Density at which pattern tiles are actually rasterized (the on-screen
    /// density supersampled by <see cref="PatternTileSupersample"/>). This is
    /// the default for <see cref="RasterizePatternTile"/>.
    /// </summary>
    public const double PatternTileRenderPixelsPerMm = DefaultPixelsPerMm * PatternTileSupersample;

    /// <summary>
    /// Scale a renderer must apply to a tile shader so a tile rasterized at
    /// <see cref="PatternTileRenderPixelsPerMm"/> is drawn at its intended
    /// on-screen size (<see cref="DefaultPixelsPerMm"/>). Equal to
    /// <c>1 / <see cref="PatternTileSupersample"/></c>.
    /// </summary>
    public const double PatternTileShaderScale = 1.0 / PatternTileSupersample;

    /// <summary>
    /// Rasterizes a processed SVG pattern into a repeating tile bitmap,
    /// encoded as a PNG byte array.
    /// </summary>
    /// <param name="processedSvg">SVG content with CSS classes already resolved to inline attributes.</param>
    /// <param name="areaFill">Area fill definition containing tiling vectors.</param>
    /// <param name="pixelsPerMm">
    /// Pixels-per-mm density for the output tile. Defaults to
    /// <see cref="PatternTileRenderPixelsPerMm"/> (supersampled); the renderer
    /// is expected to compensate with <see cref="PatternTileShaderScale"/>.
    /// </param>
    /// <returns>PNG-encoded tile bytes, or <c>null</c> if the SVG cannot be rasterized.</returns>
    public static byte[]? RasterizePatternTile(string processedSvg, AreaFill areaFill, double pixelsPerMm = PatternTileRenderPixelsPerMm)
    {
        using var svg = SKSvg.CreateFromSvg(processedSvg);
        if (svg is null) return null;

        var picture = svg.Picture;
        if (picture is null) return null;

        var svgBounds = picture.CullRect;
        if (svgBounds.Width <= 0 || svgBounds.Height <= 0) return null;

        var layout = ComputeTileLayout(areaFill, svgBounds, pixelsPerMm);

        int tileW = Math.Max(1, (int)Math.Round(layout.TileW));
        int tileH = Math.Max(1, (int)Math.Round(layout.TileH));

        // Cap tile size for sanity. Tiles are rasterized supersampled
        // (see PatternTileSupersample), so the cap is correspondingly larger
        // than the on-screen tile size.
        if (tileW > 2048 || tileH > 2048) return null;

        using var bitmap = new SKBitmap(tileW, tileH);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        DrawLattice(canvas, picture, svgBounds, areaFill, layout, pixelsPerMm);

        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Builds a pattern tile as a resolution-independent <see cref="SKPicture"/>
    /// recorded in millimetre units (1 unit = 1 mm) rather than a fixed-resolution
    /// bitmap. Tiling the picture via a picture shader
    /// (<see cref="SKPicture.ToShader(SKShaderTileMode, SKShaderTileMode, SKFilterMode, SKMatrix, SKRect)"/>)
    /// lets Skia re-rasterize the tile to match the canvas transform on every draw,
    /// so the pattern stays crisp at any zoom level and on HiDPI/Retina surfaces —
    /// unlike a pre-rasterized bitmap tile, which is upsampled and appears blurry.
    /// Used by the interactive renderer.
    /// </summary>
    /// <param name="processedSvg">SVG content with CSS classes already resolved to inline attributes.</param>
    /// <param name="areaFill">Area fill definition containing tiling vectors.</param>
    /// <param name="tileRect">
    /// On return, the tile's repeat rectangle in millimetres (origin at 0,0). The
    /// renderer tiles the shader over this rectangle and scales it to the desired
    /// on-screen pixels-per-mm density.
    /// </param>
    /// <returns>The recorded tile picture, or <c>null</c> if the SVG cannot be parsed.</returns>
    public static SKPicture? BuildPatternTilePicture(string processedSvg, AreaFill areaFill, out SKRect tileRect)
    {
        tileRect = SKRect.Empty;

        using var svg = SKSvg.CreateFromSvg(processedSvg);
        if (svg is null) return null;

        var picture = svg.Picture;
        if (picture is null) return null;

        var svgBounds = picture.CullRect;
        if (svgBounds.Width <= 0 || svgBounds.Height <= 0) return null;

        // Record in millimetre units so the tile is independent of output
        // resolution; the renderer applies the on-screen px/mm scale.
        var layout = ComputeTileLayout(areaFill, svgBounds, 1.0);
        var rect = new SKRect(0, 0, layout.TileW, layout.TileH);

        using var recorder = new SKPictureRecorder();
        var recordingCanvas = recorder.BeginRecording(rect);
        DrawLattice(recordingCanvas, picture, svgBounds, areaFill, layout, 1.0);
        var tilePicture = recorder.EndRecording();

        tileRect = rect;
        return tilePicture;
    }

    /// <summary>Conversion from SVG user units (96 DPI) to millimetres.</summary>
    private const double SvgPxPerMm = 96.0 / 25.4;

    /// <summary>
    /// Resolved tile geometry, expressed in the caller's target units (bitmap
    /// pixels when rasterizing, millimetres when recording a picture).
    /// </summary>
    private readonly record struct TileLayout(
        float TileW, float TileH, float CellW, float CellH,
        float Scale, float OffsetX, float OffsetY, float ScaledW, float ScaledH,
        bool HasOffset);

    private static TileLayout ComputeTileLayout(AreaFill areaFill, SKRect svgBounds, double pixelsPerMm)
    {
        // v1 defines horizontal repeat spacing; v2 defines vertical + optional horizontal offset.
        double tileWidthMm = Math.Abs(areaFill.V1X);
        double tileHeightMm = Math.Abs(areaFill.V2Y);
        if (tileWidthMm <= 0) tileWidthMm = svgBounds.Width;
        if (tileHeightMm <= 0) tileHeightMm = svgBounds.Height;

        bool hasOffset = Math.Abs(areaFill.V2X) > 0.01;

        // For parallelogram lattices (v2.x != 0), create a double-height tile
        // with the second row offset by v2.x, producing the correct brick-like pattern.
        double totalHeightMm = hasOffset ? tileHeightMm * 2 : tileHeightMm;

        float cellW = (float)(tileWidthMm * pixelsPerMm);
        float cellH = (float)(tileHeightMm * pixelsPerMm);
        float tileW = cellW;
        float tileH = (float)(totalHeightMm * pixelsPerMm);

        // S-100 SVG symbols specify dimensions in mm. Svg.Skia rasterizes at
        // 96 DPI (96/25.4 ≈ 3.78 px/mm). Scale the SVG from its native DPI to the
        // target density so the intended spacing between symbols (from the tiling
        // vectors) is preserved.
        float scale = (float)(pixelsPerMm / SvgPxPerMm);
        float scaledW = svgBounds.Width * scale;
        float scaledH = svgBounds.Height * scale;
        float offsetX = (cellW - scaledW) / 2;
        float offsetY = (cellH - scaledH) / 2;

        return new TileLayout(tileW, tileH, cellW, cellH, scale, offsetX, offsetY, scaledW, scaledH, hasOffset);
    }

    /// <summary>
    /// Draws the symbol lattice (first row plus, for parallelogram lattices, the
    /// offset second row with wrapping copies) onto <paramref name="canvas"/>.
    /// Shared by the bitmap (<see cref="RasterizePatternTile"/>) and picture
    /// (<see cref="BuildPatternTilePicture"/>) paths so both produce identical
    /// geometry. The symbol picture is replayed via <see cref="SKPicture.Playback"/>
    /// so that, when recording, the lattice is flattened into the tile picture and
    /// holds no reference to the (disposed) source SVG picture.
    /// </summary>
    private static void DrawLattice(SKCanvas canvas, SKPicture picture, SKRect svgBounds,
        AreaFill areaFill, TileLayout layout, double pixelsPerMm)
    {
        float baseTranslateX = layout.OffsetX - svgBounds.Left * layout.Scale;
        float baseTranslateY = layout.OffsetY - svgBounds.Top * layout.Scale;

        // First row. Clip to the cell rectangle so boundary paths don't bleed
        // into the second row's cell space (which would cause crosshatch when
        // the tile repeats and adjacent cells' boundary strokes overlap).
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, layout.CellW, layout.CellH));
        canvas.Translate(baseTranslateX, baseTranslateY);
        canvas.Scale(layout.Scale);
        picture.Playback(canvas);
        canvas.Restore();

        if (!layout.HasOffset)
            return;

        // For parallelogram lattices, draw offset copies for the second row.
        // Because the tile repeats as a simple rectangle, the offset symbol may
        // extend past the tile boundary. Draw wrapping copies so the clipped
        // portions appear correctly when the tile is repeated.
        float offset2X = (float)(areaFill.V2X * pixelsPerMm);
        float baseY = layout.CellH + baseTranslateY;

        foreach (float wrapOffset in new[] { 0f, -layout.TileW, layout.TileW })
        {
            float tx = offset2X + baseTranslateX + wrapOffset;

            // Skip if the entire symbol would be off-tile
            if (tx + layout.ScaledW < 0 || tx > layout.TileW)
                continue;

            canvas.Save();
            canvas.ClipRect(new SKRect(0, layout.CellH, layout.TileW, layout.TileH));
            canvas.Translate(tx, baseY);
            canvas.Scale(layout.Scale);
            picture.Playback(canvas);
            canvas.Restore();
        }
    }
}
