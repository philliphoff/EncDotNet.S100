using SkiaSharp;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Renderers.Skia;

/// <summary>
/// Renders a <see cref="StyledCoverageLayer"/> to an <see cref="SKBitmap"/>
/// using SkiaSharp. Each grid cell maps to one pixel in the output bitmap.
/// </summary>
public class SkiaCoverageRenderer : ICoverageRenderer<SKBitmap>
{
    /// <summary>
    /// Colour used for no-data cells. Defaults to transparent.
    /// </summary>
    public RgbaColor NoDataColor { get; set; } = RgbaColor.Transparent;

    public SKBitmap Render(StyledCoverageLayer layer, Viewport viewport)
    {
        var sampled = layer.Coverage;
        var colorScheme = layer.ColorScheme;
        var fieldData = sampled.GetField(colorScheme.FieldName);

        int rows = fieldData.GetLength(0);
        int cols = fieldData.GetLength(1);

        var bitmap = new SKBitmap(cols, rows, SKColorType.Rgba8888, SKAlphaType.Premul);

        // Pre-resolve band colors from hex strings to SKColor
        var resolvedBands = new ResolvedBand[colorScheme.Bands.Count];
        for (int i = 0; i < colorScheme.Bands.Count; i++)
        {
            var band = colorScheme.Bands[i];
            resolvedBands[i] = new ResolvedBand(
                band.MinValue,
                band.MaxValue,
                RgbaColor.FromHex(band.Color).ToSkia());
        }

        var noDataSkColor = NoDataColor.ToSkia();
        float noDataValue = layer.NoDataValue;
        bool noDataIsNaN = float.IsNaN(noDataValue);

        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            float value = fieldData[row, col];
            var color = ResolveColor(value, resolvedBands, noDataValue, noDataIsNaN, noDataSkColor);
            bitmap.SetPixel(col, row, color);
        }

        return bitmap;
    }

    private static SKColor ResolveColor(
        float value,
        ReadOnlySpan<ResolvedBand> bands,
        float noDataValue,
        bool noDataIsNaN,
        SKColor noDataColor)
    {
        bool isNoData = noDataIsNaN ? float.IsNaN(value) : value == noDataValue;
        if (isNoData)
            return noDataColor;

        for (int i = 0; i < bands.Length; i++)
        {
            ref readonly var band = ref bands[i];
            if (value >= band.MinValue && value < band.MaxValue)
                return band.Color;
        }

        return SKColors.Transparent;
    }

    private readonly record struct ResolvedBand(float MinValue, float MaxValue, SKColor Color);
}
