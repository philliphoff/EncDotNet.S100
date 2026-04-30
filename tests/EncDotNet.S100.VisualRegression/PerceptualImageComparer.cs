using SkiaSharp;

namespace EncDotNet.S100.VisualRegression;

/// <summary>
/// Compares two PNG (or any SkiaSharp-decodable) images pixel-by-pixel using a
/// perceptual tolerance. The comparison is symmetric and ignores order: A vs B
/// is equivalent to B vs A.
/// </summary>
/// <remarks>
/// Two thresholds control acceptance:
/// <list type="bullet">
///   <item><see cref="MaxChannelDelta"/> — the largest per-channel (R, G, B, A)
///         absolute difference allowed for a single pixel to be considered
///         "the same". Pixels exceeding this threshold are counted as
///         differing.</item>
///   <item><see cref="MaxDifferentPixelFraction"/> — the largest fraction of
///         pixels (in <c>[0, 1]</c>) that may differ before the overall
///         comparison fails.</item>
/// </list>
/// The defaults (per-channel ≤ 4, fraction ≤ 0.001) tolerate sub-pixel
/// rasterisation jitter without masking real regressions.
/// </remarks>
public sealed class PerceptualImageComparer
{
    /// <summary>Maximum allowed absolute difference per channel for a single pixel. Default: 4.</summary>
    public int MaxChannelDelta { get; init; } = 4;

    /// <summary>Maximum allowed fraction of pixels that may differ. Default: 0.001 (0.1%).</summary>
    public double MaxDifferentPixelFraction { get; init; } = 0.001;

    /// <summary>Default comparer.</summary>
    public static PerceptualImageComparer Default { get; } = new();

    /// <summary>
    /// Compares two PNG byte buffers and returns the result.
    /// </summary>
    public ImageComparisonResult Compare(byte[] expected, byte[] actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        using var expectedBmp = SKBitmap.Decode(expected);
        using var actualBmp = SKBitmap.Decode(actual);
        return Compare(expectedBmp, actualBmp);
    }

    /// <summary>
    /// Compares two bitmaps and returns the result.
    /// </summary>
    public ImageComparisonResult Compare(SKBitmap expected, SKBitmap actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return new ImageComparisonResult(
                AreEqual: false,
                Reason: $"Dimensions differ: expected {expected.Width}x{expected.Height}, got {actual.Width}x{actual.Height}",
                MaxChannelDelta: 255,
                DifferentPixelCount: Math.Max(expected.Width * expected.Height, actual.Width * actual.Height),
                TotalPixelCount: Math.Max(expected.Width * expected.Height, actual.Width * actual.Height));
        }

        int width = expected.Width;
        int height = expected.Height;
        int total = width * height;
        int different = 0;
        int maxDelta = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var e = expected.GetPixel(x, y);
                var a = actual.GetPixel(x, y);
                int dr = Math.Abs(e.Red - a.Red);
                int dg = Math.Abs(e.Green - a.Green);
                int db = Math.Abs(e.Blue - a.Blue);
                int da = Math.Abs(e.Alpha - a.Alpha);
                int pixelDelta = Math.Max(Math.Max(dr, dg), Math.Max(db, da));
                if (pixelDelta > maxDelta) maxDelta = pixelDelta;
                if (pixelDelta > MaxChannelDelta) different++;
            }
        }

        double fraction = total == 0 ? 0 : (double)different / total;
        bool ok = fraction <= MaxDifferentPixelFraction;
        string? reason = ok ? null
            : $"{different} / {total} pixels differ ({fraction:P2}) — limit {MaxDifferentPixelFraction:P2}; max channel delta {maxDelta}.";

        return new ImageComparisonResult(
            AreEqual: ok,
            Reason: reason,
            MaxChannelDelta: maxDelta,
            DifferentPixelCount: different,
            TotalPixelCount: total);
    }
}

/// <summary>Result of a perceptual image comparison.</summary>
/// <param name="AreEqual">True if the comparison passed both thresholds.</param>
/// <param name="Reason">Human-readable explanation when <paramref name="AreEqual"/> is false.</param>
/// <param name="MaxChannelDelta">The largest per-channel delta seen across all pixels.</param>
/// <param name="DifferentPixelCount">Number of pixels exceeding the per-channel threshold.</param>
/// <param name="TotalPixelCount">Total pixel count.</param>
public sealed record ImageComparisonResult(
    bool AreEqual,
    string? Reason,
    int MaxChannelDelta,
    int DifferentPixelCount,
    int TotalPixelCount);
