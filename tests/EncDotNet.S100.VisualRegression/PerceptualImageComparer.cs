using SkiaSharp;

namespace EncDotNet.S100.VisualRegression;

/// <summary>
/// Compares two PNG (or any SkiaSharp-decodable) images pixel-by-pixel using a
/// perceptual tolerance. The comparison is symmetric and ignores order: A vs B
/// is equivalent to B vs A.
/// </summary>
/// <remarks>
/// Two thresholds (plus an optional third) control acceptance:
/// <list type="bullet">
///   <item><see cref="MaxChannelDelta"/> — the largest per-channel (R, G, B, A)
///         absolute difference allowed for a single pixel to be considered
///         "the same". Pixels exceeding this threshold are counted as
///         differing.</item>
///   <item><see cref="MaxDifferentPixelFraction"/> — the largest fraction of
///         pixels (in <c>[0, 1]</c>) that may differ before the overall
///         comparison fails.</item>
///   <item><see cref="MaxDifferentInkFraction"/> — optional. The largest number
///         of differing pixels, as a fraction of the <em>expected</em> image's
///         ink (pixels that differ from <see cref="Background"/>). Use it for
///         sparse renders, where a fraction of the whole image can exceed all
///         the ink present and so let every symbol appear or vanish unnoticed.
///         Because the budget is taken from the expected image, a blank
///         baseline gets no budget at all.</item>
/// </list>
/// The defaults (per-channel ≤ 4, fraction ≤ 0.05) tolerate sub-pixel
/// rasterisation jitter and cross-platform font hinting drift (especially
/// label glyphs in S-101, S-124, and S-421 portrayal) without masking real
/// regressions in geometry, colour, or symbology.
/// </remarks>
public sealed class PerceptualImageComparer
{
    /// <summary>Maximum allowed absolute difference per channel for a single pixel. Default: 4.</summary>
    public int MaxChannelDelta { get; init; } = 4;

    /// <summary>Maximum allowed fraction of pixels that may differ. Default: 0.05 (5%).</summary>
    public double MaxDifferentPixelFraction { get; init; } = 0.05;

    /// <summary>
    /// Maximum allowed number of differing pixels as a fraction of the expected
    /// image's non-background pixel count, or <see langword="null"/> (the
    /// default) for no ink-relative limit. Applies in addition to
    /// <see cref="MaxDifferentPixelFraction"/>.
    /// </summary>
    public double? MaxDifferentInkFraction { get; init; }

    /// <summary>
    /// Background colour used to count ink for <see cref="MaxDifferentInkFraction"/>.
    /// Default: opaque white, the render harness background.
    /// </summary>
    public SKColor Background { get; init; } = SKColors.White;

    /// <summary>
    /// Largest per-channel (R, G, B) difference from the background at which a
    /// pixel still counts as background rather than ink.
    /// </summary>
    public const int InkChannelThreshold = 8;

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
        int expectedInk = 0;

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
                if (IsInk(e, Background)) expectedInk++;
            }
        }

        double fraction = total == 0 ? 0 : (double)different / total;
        bool ok = fraction <= MaxDifferentPixelFraction;
        string? reason = ok ? null
            : $"{different} / {total} pixels differ ({fraction:P2}) — limit {MaxDifferentPixelFraction:P2}; max channel delta {maxDelta}.";

        if (ok && MaxDifferentInkFraction is { } inkFraction)
        {
            int inkBudget = (int)Math.Floor(expectedInk * inkFraction);
            if (different > inkBudget)
            {
                ok = false;
                reason = $"{different} pixels differ — limit {inkBudget} ({inkFraction:P0} of the expected image's {expectedInk} ink pixels); max channel delta {maxDelta}.";
            }
        }

        return new ImageComparisonResult(
            AreEqual: ok,
            Reason: reason,
            MaxChannelDelta: maxDelta,
            DifferentPixelCount: different,
            TotalPixelCount: total);
    }

    /// <summary>
    /// Counts the pixels of <paramref name="bitmap"/> that differ from
    /// <paramref name="background"/> by more than <see cref="InkChannelThreshold"/>
    /// in any colour channel — the render's "ink".
    /// </summary>
    public static int CountNonBackgroundPixels(SKBitmap bitmap, SKColor background)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (IsInk(bitmap.GetPixel(x, y), background)) count++;
            }
        }
        return count;
    }

    private static bool IsInk(SKColor c, SKColor background) =>
        Math.Abs(c.Red - background.Red) > InkChannelThreshold
        || Math.Abs(c.Green - background.Green) > InkChannelThreshold
        || Math.Abs(c.Blue - background.Blue) > InkChannelThreshold;
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
