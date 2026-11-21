using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// A relief-shaded coverage raster is drawn mipmapped
/// (<see cref="SmoothRasterRenderer"/>), so zoomed out its fine shading
/// averages instead of mottling; flat band colours keep Mapsui's
/// <see cref="RasterStyle"/>.
/// </summary>
public class SmoothRasterRendererTests
{
    [Fact]
    public void A_shaded_coverage_uses_the_smooth_raster_style_and_a_flat_one_does_not()
    {
        var renderer = new MapsuiCoverageRenderer(new ProjNetCrsTransformFactory());
        var flat = Layer(shade: null);
        var shaded = Layer(shade: [0.8f, 1.2f, 1f, 0.9f]);

        Assert.IsType<RasterStyle>(Style(renderer.Render(flat, Viewport)));
        Assert.IsType<SmoothRasterStyle>(Style(renderer.Render(shaded, Viewport)));
    }

    [Fact]
    public void A_raster_drawn_eight_times_smaller_averages_its_pixels()
    {
        // Black with a 2 × 2 white dot in every 8 × 8 block, drawn at 1/8 size.
        // Each screen pixel's sample point falls in the middle of a dot, so
        // filtering without mipmaps draws it white; averaging the block gives
        // 4/64 white, near black.
        const int size = 64;
        using var dots = Dots(size);
        var feature = new RasterFeature(new MRaster([], new MRect(0, 0, size, size)));
        var style = new SmoothRasterStyle { Image = dots };
        var layer = new MemoryLayer { Features = [feature] };
        var viewport = new Mapsui.Viewport(size / 2.0, size / 2.0, 8, 0, size / 8, size / 8);

        using var surface = SKSurface.Create(new SKImageInfo(size / 8, size / 8, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Red);
        Assert.True(SmoothRasterRenderer.Instance.Draw(surface.Canvas, viewport, layer, feature, style, null!, 0));

        using var image = surface.Snapshot();
        using var pixels = SKBitmap.FromImage(image);
        for (int y = 0; y < pixels.Height; y++)
            for (int x = 0; x < pixels.Width; x++)
            {
                var pixel = pixels.GetPixel(x, y);
                Assert.InRange(pixel.Red, 0, 48);
                Assert.Equal(pixel.Red, pixel.Green);
            }
    }

    [Fact]
    public void The_image_matrix_puts_the_corners_on_the_screen_points_even_when_rotated()
    {
        var matrix = SmoothRasterRenderer.ImageToScreen(
            200, 100, topLeft: (50, 10), topRight: (50, 210), bottomLeft: (-50, 10));

        AssertPoint(matrix.MapPoint(0, 0), 50, 10);
        AssertPoint(matrix.MapPoint(200, 0), 50, 210);
        AssertPoint(matrix.MapPoint(0, 100), -50, 10);
        AssertPoint(matrix.MapPoint(200, 100), -50, 210);
    }

    private static readonly EncDotNet.S100.Pipelines.Viewport Viewport = new()
    {
        MinLatitude = 0,
        MaxLatitude = 1,
        MinLongitude = 0,
        MaxLongitude = 1,
        WidthPixels = 100,
        HeightPixels = 100,
        ScaleDenominator = 50_000,
    };

    private static void AssertPoint(SKPoint point, float x, float y)
    {
        Assert.Equal(x, point.X, 3);
        Assert.Equal(y, point.Y, 3);
    }

    private static IStyle Style(ILayer layer) =>
        Assert.Single(Assert.IsType<MemoryLayer>(layer).Features.OfType<RasterFeature>()).Styles.Single();

    private static SKImage Dots(int size)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                bitmap.SetPixel(x, y, x % 8 is 3 or 4 && y % 8 is 3 or 4 ? SKColors.White : SKColors.Black);
        return SKImage.FromBitmap(bitmap);
    }

    private static StyledCoverageLayer Layer(float[]? shade)
    {
        var grid = new GridMetadata
        {
            NumRows = 2,
            NumColumns = 2,
            OriginLatitude = 0,
            OriginLongitude = 0,
            SpacingLatitudinal = 0.01,
            SpacingLongitudinal = 0.01,
        };
        return new StyledCoverageLayer
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = grid,
                Values = new Dictionary<string, float[]> { ["depth"] = [5f, 25f, 1f, 50f] },
            },
            ColorScheme = new CoverageColorScheme
            {
                FieldName = "depth",
                Bands = [new ColorBand { MinValue = 0f, MaxValue = 100f, Color = "#4080C0" }],
            },
            NoDataValue = float.NaN,
            Georeferencer = new GridGeoreferencer(grid, "EPSG:4326"),
            ReliefShade = shade,
        };
    }
}
