using SkiaSharp;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Skia;

namespace EncDotNet.S100.Pipelines.Tests;

public class SkiaCoverageRendererTests
{
    private static readonly Viewport DefaultViewport = new()
    {
        MinLatitude = 0, MaxLatitude = 1,
        MinLongitude = 0, MaxLongitude = 1,
        WidthPixels = 100, HeightPixels = 100,
    };

    [Fact]
    public void Render_BitmapDimensions_MatchGrid()
    {
        var layer = MakeStyledLayer(
            rows: 3, cols: 5,
            fill: 7f,
            noDataValue: float.NaN);

        var renderer = new SkiaCoverageRenderer();
        using var bitmap = renderer.Render(layer, DefaultViewport);

        Assert.Equal(5, bitmap.Width);
        Assert.Equal(3, bitmap.Height);
    }

    [Fact]
    public void Render_DepthValues_MapToCorrectBandColors()
    {
        // 2x2 grid: shallow (1m), medium (5m), deep (25m), very deep (50m)
        var depths = new float[,] { { 1f, 5f }, { 25f, 50f } };
        var layer = MakeStyledLayer(depths, noDataValue: -9999f);

        var renderer = new SkiaCoverageRenderer();
        using var bitmap = renderer.Render(layer, DefaultViewport);

        // Band: [0, 3) → #ADE3FF, [3, 10) → #6BC5FF, [10, 30) → #2196F3, [30, 100) → #0D47A1
        // Grid row 0 (south) maps to bitmap row 1 (bottom), grid row 1 (north) maps to bitmap row 0 (top)
        AssertPixelHex(bitmap, col: 0, row: 1, "#ADE3FF"); // 1m → shallow (grid row 0)
        AssertPixelHex(bitmap, col: 1, row: 1, "#6BC5FF"); // 5m → medium (grid row 0)
        AssertPixelHex(bitmap, col: 0, row: 0, "#2196F3"); // 25m → deep (grid row 1)
        AssertPixelHex(bitmap, col: 1, row: 0, "#0D47A1"); // 50m → very deep (grid row 1)
    }

    [Fact]
    public void Render_NaNNoData_ProducesTransparentPixels()
    {
        var depths = new float[,] { { 5f, float.NaN } };
        var layer = MakeStyledLayer(depths, noDataValue: float.NaN);

        var renderer = new SkiaCoverageRenderer();
        using var bitmap = renderer.Render(layer, DefaultViewport);

        // Real value → colored (non-zero alpha)
        Assert.NotEqual(0, bitmap.GetPixel(0, 0).Alpha);
        // NaN → transparent (zero alpha)
        Assert.Equal(0, bitmap.GetPixel(1, 0).Alpha);
    }

    [Fact]
    public void Render_SentinelNoData_ProducesTransparentPixels()
    {
        const float noData = -9999f;
        var depths = new float[,] { { 5f, noData } };
        var layer = MakeStyledLayer(depths, noDataValue: noData);

        var renderer = new SkiaCoverageRenderer();
        using var bitmap = renderer.Render(layer, DefaultViewport);

        Assert.NotEqual(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(0, bitmap.GetPixel(1, 0).Alpha);
    }

    [Fact]
    public void Render_CustomNoDataColor_Applied()
    {
        var depths = new float[,] { { float.NaN } };
        var layer = MakeStyledLayer(depths, noDataValue: float.NaN);

        var renderer = new SkiaCoverageRenderer
        {
            NoDataColor = new RgbaColor(147, 174, 187, 255) // IHO NODTA grey
        };
        using var bitmap = renderer.Render(layer, DefaultViewport);

        var pixel = bitmap.GetPixel(0, 0);
        Assert.Equal(147, pixel.Red);
        Assert.Equal(174, pixel.Green);
        Assert.Equal(187, pixel.Blue);
    }

    [Fact]
    public void Render_OutOfRangeValue_ProducesTransparent()
    {
        // 999 is outside all defined bands (max band upper = 100)
        var depths = new float[,] { { 999f } };
        var layer = MakeStyledLayer(depths, noDataValue: float.NaN);

        var renderer = new SkiaCoverageRenderer();
        using var bitmap = renderer.Render(layer, DefaultViewport);

        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }

    #region Helpers

    private static readonly CoverageColorScheme TestColorScheme = new()
    {
        FieldName = "depth",
        Bands =
        [
            new ColorBand { MinValue = 0f, MaxValue = 3f, Color = "#ADE3FF" },
            new ColorBand { MinValue = 3f, MaxValue = 10f, Color = "#6BC5FF" },
            new ColorBand { MinValue = 10f, MaxValue = 30f, Color = "#2196F3" },
            new ColorBand { MinValue = 30f, MaxValue = 100f, Color = "#0D47A1" },
        ]
    };

    private static StyledCoverageLayer MakeStyledLayer(
        float[,] depths,
        float noDataValue) =>
        new()
        {
            Coverage = new SampledCoverage
            {
                Region = GridRegion.Full,
                Metadata = new GridMetadata
                {
                    NumRows = depths.GetLength(0),
                    NumColumns = depths.GetLength(1),
                    OriginLatitude = 0,
                    OriginLongitude = 0,
                    SpacingLatitudinal = 0.01,
                    SpacingLongitudinal = 0.01,
                },
                Values = new Dictionary<string, float[,]> { ["depth"] = depths },
            },
            ColorScheme = TestColorScheme,
            NoDataValue = noDataValue,
        };

    private static StyledCoverageLayer MakeStyledLayer(
        int rows, int cols,
        float fill,
        float noDataValue)
    {
        var depths = new float[rows, cols];
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
            depths[r, c] = fill;

        return MakeStyledLayer(depths, noDataValue);
    }

    private static void AssertPixelHex(SKBitmap bitmap, int col, int row, string expectedHex)
    {
        var expected = RgbaColor.FromHex(expectedHex).ToSkia();
        var actual = bitmap.GetPixel(col, row);
        Assert.Equal(expected, actual);
    }

    #endregion
}
