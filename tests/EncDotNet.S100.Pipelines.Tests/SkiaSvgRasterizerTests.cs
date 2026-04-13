using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

public class SkiaSvgRasterizerTests
{
    // Minimal valid SVG: 10x10 red square
    private const string TestSvg =
        """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" height="10" fill="red"/></svg>""";

    [Fact]
    public void RasterizePatternTile_SimpleGrid_ProducesSingleHeightTile()
    {
        var fill = new AreaFill
        {
            Name = "test",
            V1X = 20,
            V1Y = 0,
            V2X = 0, // no offset → simple grid
            V2Y = 20,
        };

        var png = SkiaSvgRasterizer.RasterizePatternTile(TestSvg, fill, pixelsPerMm: 2);
        Assert.NotNull(png);

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(40, bitmap.Width);  // 20mm * 2px/mm
        Assert.Equal(40, bitmap.Height); // single height
    }

    [Fact]
    public void RasterizePatternTile_ParallelogramLattice_ProducesDoubleHeightTile()
    {
        var fill = new AreaFill
        {
            Name = "test",
            V1X = 20,
            V1Y = 0,
            V2X = 10, // half-width offset → parallelogram
            V2Y = 20,
        };

        var png = SkiaSvgRasterizer.RasterizePatternTile(TestSvg, fill, pixelsPerMm: 2);
        Assert.NotNull(png);

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(40, bitmap.Width);  // 20mm * 2px/mm
        Assert.Equal(80, bitmap.Height); // double height for offset row
    }

    [Fact]
    public void RasterizePatternTile_SymbolSmallerThanTile_HasTransparentGaps()
    {
        // The SVG is 10x10 CSS px (≈ 2.65mm at 96 DPI). The tile cell is
        // 20mm × 20mm. The symbol should occupy a fraction of the cell, leaving
        // transparent gaps around the edges.
        var fill = new AreaFill
        {
            Name = "test",
            V1X = 20,
            V1Y = 0,
            V2X = 0,
            V2Y = 20,
        };

        var png = SkiaSvgRasterizer.RasterizePatternTile(TestSvg, fill, pixelsPerMm: 2);
        Assert.NotNull(png);

        using var bitmap = SKBitmap.Decode(png);

        // The corners of the tile should be transparent because the symbol
        // is centered and smaller than the cell.
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, 0).Alpha);
        Assert.Equal(0, bitmap.GetPixel(0, bitmap.Height - 1).Alpha);
        Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).Alpha);

        // The center of the tile should have non-transparent pixels.
        var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.True(center.Alpha > 0, "Expected a non-transparent pixel at the tile center.");
    }

    [Fact]
    public void RasterizePatternTile_ParallelogramLattice_OffsetRowWrapsAcrossTileBoundary()
    {
        // Use a large offset (15mm out of 20mm tile width) so the symbol
        // extends past the right edge of the tile. The wrapping copy should
        // cause pixels to appear on the left side of the offset row.
        var fill = new AreaFill
        {
            Name = "test",
            V1X = 20,
            V1Y = 0,
            V2X = 15, // 75% of tile width offset
            V2Y = 20,
        };

        var png = SkiaSvgRasterizer.RasterizePatternTile(TestSvg, fill, pixelsPerMm: 2);
        Assert.NotNull(png);

        using var bitmap = SKBitmap.Decode(png);

        // The offset row is the bottom half (y >= 40).
        // With the offset of 15mm (30px) and the symbol scaled to fit the
        // 40x40 cell, the wrapping copy should place non-transparent pixels
        // near the left edge of the bottom half.
        bool hasPixelInLeftOfOffsetRow = false;
        int cellH = 40; // tileHeightMm * pixelsPerMm

        for (int y = cellH; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width / 4; x++) // left quarter
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    hasPixelInLeftOfOffsetRow = true;
                    break;
                }
            }

            if (hasPixelInLeftOfOffsetRow) break;
        }

        Assert.True(hasPixelInLeftOfOffsetRow,
            "Expected the wrapping copy to produce non-transparent pixels near the left edge of the offset row.");
    }
}
