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

        // Scale the SVG to fit within one tile cell
        float svgW = svgBounds.Width;
        float svgH = svgBounds.Height;
        float cellW = (float)(tileWidthMm * pixelsPerMm);
        float cellH = (float)(tileHeightMm * pixelsPerMm);
        float scaleX = cellW / svgW;
        float scaleY = cellH / svgH;
        float scale = Math.Min(scaleX, scaleY);

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

        // For parallelogram lattices, draw a second copy offset for the second row
        if (hasOffset)
        {
            float offset2X = (float)(areaFill.V2X * pixelsPerMm);
            canvas.Save();
            canvas.Translate(offset2X + offsetX - svgBounds.Left * scale, cellH + offsetY - svgBounds.Top * scale);
            canvas.Scale(scale);
            canvas.DrawPicture(picture);
            canvas.Restore();
        }

        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
