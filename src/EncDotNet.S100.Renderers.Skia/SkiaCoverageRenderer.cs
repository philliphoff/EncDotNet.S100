using System.Diagnostics;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using SkiaSharp;
using S100Diag = EncDotNet.S100.Renderers.Skia.Diagnostics;

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

    /// <inheritdoc/>
    /// <remarks>
    /// Produces one pixel per sampled grid cell; cells with no data are
    /// painted with <see cref="NoDataColor"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The layer has no <see cref="StyledCoverageLayer.ColorScheme"/>.
    /// </exception>
    public SKBitmap Render(StyledCoverageLayer layer, Viewport viewport)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.render.coverage.frame");
        __activity?.SetTag("s100.render.target", "skia");
        var renderStart = Stopwatch.GetTimestamp();

        var sampled = layer.Coverage;
        var colorScheme = layer.ColorScheme
            ?? throw new InvalidOperationException(
                "SkiaCoverageRenderer requires a non-null ColorScheme. " +
                "Catalogues that do not specify a coverage colour fill " +
                "(e.g. S-111 Edition 2.0.0) must not be passed to this " +
                "renderer.");
        var fieldData = sampled.GetField(colorScheme.FieldName);
        var fieldSpan = fieldData.Span;

        int rows = fieldData.Rows;
        int cols = fieldData.Cols;
        S100Diag.Telemetry.CoverageCellsProcessed.Add((long)rows * cols);

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
        // Optional relief shading (a factor per cell), applied to band colours
        // only: no-data fill stays flat.
        float[]? shade = layer.ReliefShade is { } rs && rs.Length == rows * cols ? rs : null;

        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                int index = row * cols + col;
                float value = fieldSpan[index];
                SKColor color = ResolveColor(value, resolvedBands, noDataValue, noDataIsNaN, noDataSkColor, out bool isBand);
                if (isBand && shade is not null)
                    color = Shade(color, shade[index]);
                // Grid row 0 is the southernmost (bottom of image), so flip vertically.
                bitmap.SetPixel(col, rows - 1 - row, color);
            }

        S100Diag.Telemetry.CoverageFrameDuration.Record(
            (Stopwatch.GetTimestamp() - renderStart) * 1000.0 / Stopwatch.Frequency);

        return bitmap;
    }

    private static SKColor ResolveColor(
        float value,
        ReadOnlySpan<ResolvedBand> bands,
        float noDataValue,
        bool noDataIsNaN,
        SKColor noDataColor,
        out bool isBand)
    {
        isBand = false;
        bool isNoData = noDataIsNaN ? float.IsNaN(value) : value == noDataValue;
        if (isNoData)
            return noDataColor;

        for (int i = 0; i < bands.Length; i++)
        {
            ref readonly var band = ref bands[i];
            if (value >= band.MinValue && value < band.MaxValue)
            {
                isBand = true;
                return band.Color;
            }
        }

        return SKColors.Transparent;
    }

    // SKColor is unpremultiplied, so shade the channels directly.
    private static SKColor Shade(SKColor color, float factor) => factor == 1f
        ? color
        : new SKColor(
            ReliefShading.ShadeChannel(color.Red, factor),
            ReliefShading.ShadeChannel(color.Green, factor),
            ReliefShading.ShadeChannel(color.Blue, factor),
            color.Alpha);

    private readonly record struct ResolvedBand(float MinValue, float MaxValue, SKColor Color);
}
