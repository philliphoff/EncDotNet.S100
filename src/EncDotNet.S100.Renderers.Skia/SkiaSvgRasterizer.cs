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
    /// Default pixels-per-mm density used when rasterizing SVG pattern tiles.
    /// S-100 defines pattern dimensions in mm for paper charts (~3.78 px/mm at 96 DPI).
    /// For interactive display a lower density is used so patterns repeat more tightly
    /// relative to the on-screen polygon size.
    /// </summary>
    public const double DefaultPixelsPerMm = 1.5;

    /// <summary>
    /// Rasterizes a processed SVG pattern into a repeating tile bitmap,
    /// encoded as a PNG byte array.
    /// </summary>
    /// <param name="processedSvg">SVG content with CSS classes already resolved to inline attributes.</param>
    /// <param name="areaFill">Area fill definition containing tiling vectors.</param>
    /// <param name="pixelsPerMm">Pixels-per-mm density for the output tile.</param>
    /// <returns>PNG-encoded tile bytes, or <c>null</c> if the SVG cannot be rasterized.</returns>
    public static byte[]? RasterizePatternTile(string processedSvg, AreaFill areaFill, double pixelsPerMm = DefaultPixelsPerMm)
    {
        using var svg = SKSvg.CreateFromSvg(processedSvg);
        if (svg is null) return null;

        var picture = svg.Picture;
        if (picture is null) return null;

        var svgBounds = picture.CullRect;
        if (svgBounds.Width <= 0 || svgBounds.Height <= 0) return null;

        // Determine tile dimensions from the tiling vectors.
        // v1 defines horizontal repeat spacing; v2 defines vertical + optional horizontal offset.
        double tileWidthMm = Math.Abs(areaFill.V1X);
        double tileHeightMm = Math.Abs(areaFill.V2Y);
        if (tileWidthMm <= 0) tileWidthMm = svgBounds.Width;
        if (tileHeightMm <= 0) tileHeightMm = svgBounds.Height;

        bool hasOffset = Math.Abs(areaFill.V2X) > 0.01;

        // For parallelogram lattices (v2.x != 0), create a double-height tile
        // with the second row offset by v2.x, producing the correct brick-like pattern.
        double totalHeightMm = hasOffset ? tileHeightMm * 2 : tileHeightMm;

        int tileW = Math.Max(1, (int)Math.Round(tileWidthMm * pixelsPerMm));
        int tileH = Math.Max(1, (int)Math.Round(totalHeightMm * pixelsPerMm));

        // Cap tile size for sanity
        if (tileW > 512 || tileH > 512) return null;

        using var bitmap = new SKBitmap(tileW, tileH);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Scale the SVG to its natural mm size within the tile.
        // S-100 SVG symbols specify dimensions in mm. Svg.Skia rasterizes at
        // 96 DPI, converting mm to pixels at 96/25.4 ≈ 3.78 px/mm. We render
        // the tile at the caller's pixelsPerMm density, so scale the SVG down
        // from its native DPI to the target density. This preserves the intended
        // spacing between symbols defined by the tiling vectors.
        const double SvgPxPerMm = 96.0 / 25.4;
        float svgW = svgBounds.Width;
        float svgH = svgBounds.Height;
        float cellW = (float)(tileWidthMm * pixelsPerMm);
        float cellH = (float)(tileHeightMm * pixelsPerMm);
        float scale = (float)(pixelsPerMm / SvgPxPerMm);

        // Center the SVG in the cell
        float scaledW = svgW * scale;
        float scaledH = svgH * scale;
        float offsetX = (cellW - scaledW) / 2;
        float offsetY = (cellH - scaledH) / 2;

        // Draw the SVG at position (0,0) for the first row
        canvas.Save();
        canvas.Translate(offsetX - svgBounds.Left * scale, offsetY - svgBounds.Top * scale);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Restore();

        // For parallelogram lattices, draw offset copies for the second row.
        // Because the tile repeats as a simple rectangle, the offset symbol may
        // extend past the tile boundary. Draw wrapping copies so the clipped
        // portions appear correctly when the tile is repeated.
        if (hasOffset)
        {
            float offset2X = (float)(areaFill.V2X * pixelsPerMm);
            float baseY = cellH + offsetY - svgBounds.Top * scale;
            float baseTranslateX = offsetX - svgBounds.Left * scale;

            foreach (float wrapOffset in new[] { 0f, -tileW, tileW })
            {
                float tx = offset2X + baseTranslateX + wrapOffset;

                // Skip if the entire symbol would be off-canvas
                if (tx + scaledW < 0 || tx > tileW)
                    continue;

                canvas.Save();
                canvas.Translate(tx, baseY);
                canvas.Scale(scale);
                canvas.DrawPicture(picture);
                canvas.Restore();
            }
        }

        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
