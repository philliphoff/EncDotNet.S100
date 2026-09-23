using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// A rotated composite viewport (issue #578) turns the chart clockwise about
/// the image centre while its labels stay upright, and still composites the
/// layers bottom-most first.
/// </summary>
public sealed class RotatedCompositeTests
{
    private static readonly RgbaColor Red = new(255, 0, 0, 255);
    private static readonly RgbaColor Blue = new(0, 0, 255, 255);
    private static readonly RgbaColor Black = new(0, 0, 0, 255);

    // 200 × 100 px around (0, 0) at about 0.01° per pixel on both axes.
    private static Viewport View(double rotation) => new()
    {
        MinLongitude = -1,
        MaxLongitude = 1,
        MinLatitude = -0.5,
        MaxLatitude = 0.5,
        WidthPixels = 200,
        HeightPixels = 100,
        ScaleDenominator = 4_000_000,
        RotationDegrees = rotation,
    };

    [Fact]
    public void NorthUp_DrawsTheSquareNorthOfCentre()
    {
        using var bitmap = Render(View(0), Layer(Square(0.3, 0, Red)));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(100, 20));
        Assert.Equal(SKColors.White, bitmap.GetPixel(130, 50));
    }

    [Theory]
    [InlineData(90, 130, 50)]   // north to the right
    [InlineData(180, 100, 80)]  // north down
    [InlineData(-90, 70, 50)]   // north to the left
    public void Rotated_TurnsTheChartClockwise(double rotation, int x, int y)
    {
        using var bitmap = Render(View(rotation), Layer(Square(0.3, 0, Red)));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(x, y));
        Assert.Equal(SKColors.White, bitmap.GetPixel(100, 20));
    }

    [Fact]
    public void Rotated_FillsTheCornersTheNorthUpFrameDoesNotCover()
    {
        // A fill over the whole north-up frame plus its surroundings: rotated
        // 45°, the output's corners show area outside the north-up frame, which
        // must be drawn too.
        var fill = new AreaPaintOp
        {
            FeatureReference = "sea",
            WorldShell = Ring(-3, -3, 3, 3),
            Fill = Blue,
        };

        using var bitmap = Render(View(45), Layer(fill));

        Assert.Equal(SKColors.Blue, bitmap.GetPixel(1, 1));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(198, 98));
    }

    [Fact]
    public void Rotated_KeepsTextUpright_AtTheRotatedAnchor()
    {
        var label = new TextPaintOp
        {
            FeatureReference = "label",
            World = WebMercator.FromLonLat(0, 0.3),
            Text = "WWWWWW",
            FontSizePx = 14,
            ForeColor = Black,
        };

        using var bitmap = Render(View(90), Layer(label));

        var ink = InkBounds(bitmap);
        Assert.True(ink.Width > 2 * ink.Height, $"expected wide, upright text; ink was {ink}");
        Assert.InRange(ink.MidX, 125, 135);
        Assert.InRange(ink.MidY, 45, 55);
    }

    [Fact]
    public void Rotated_AHigherLayerCoversALowerLayersText()
    {
        var label = new TextPaintOp
        {
            FeatureReference = "label",
            World = (0, 0),
            Text = "WWWWWW",
            FontSizePx = 14,
            ForeColor = Black,
        };
        var cover = new AreaPaintOp
        {
            FeatureReference = "cover",
            WorldShell = Ring(-0.5, -0.3, 0.5, 0.3),
            Fill = Blue,
        };

        using var bitmap = Render(View(30), Layer(label), Layer(cover));

        Assert.Equal(SKRectI.Empty, InkBounds(bitmap, SKColors.Black));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(100, 50));
    }

    private static SKBitmap Render(Viewport viewport, params CompositeLayer[] layers)
        => new HeadlessCompositeRenderer().Render(viewport, layers);

    private static VectorCompositeLayer Layer(params PaintOp[] ops)
        => new(new VectorScene(ops), honorScaleVisibility: false);

    // A 0.1° square centred on (lat, lon).
    private static AreaPaintOp Square(double lat, double lon, RgbaColor fill) => new()
    {
        FeatureReference = "square",
        WorldShell = Ring(lon - 0.05, lat - 0.1, lon + 0.05, lat + 0.1),
        Fill = fill,
    };

    private static IReadOnlyList<(double X, double Y)> Ring(double west, double south, double east, double north) =>
    [
        WebMercator.FromLonLat(west, south),
        WebMercator.FromLonLat(east, south),
        WebMercator.FromLonLat(east, north),
        WebMercator.FromLonLat(west, north),
        WebMercator.FromLonLat(west, south),
    ];

    // The bounds of the pixels that are not the white background (or, given a
    // colour, of the pixels close to it).
    private static SKRectI InkBounds(SKBitmap bitmap, SKColor? color = null)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                bool ink = color is { } c
                    ? Math.Abs(pixel.Red - c.Red) + Math.Abs(pixel.Green - c.Green) + Math.Abs(pixel.Blue - c.Blue) < 60
                    : pixel != SKColors.White;
                if (!ink)
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 ? SKRectI.Empty : new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }
}
