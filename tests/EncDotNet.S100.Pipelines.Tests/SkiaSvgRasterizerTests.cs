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
