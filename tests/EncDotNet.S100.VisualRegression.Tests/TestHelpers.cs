using SkiaSharp;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Helpers shared by all spec rendering tests.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Repository-relative path to the committed test datasets directory.
    /// Resolved by walking up from the executing assembly directory until we
    /// find <c>tests/datasets</c>.
    /// </summary>
    public static string DatasetsRoot { get; } = ResolveDatasetsRoot();

    private static string ResolveDatasetsRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "tests", "datasets");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        return Path.Combine(AppContext.BaseDirectory, "tests", "datasets");
    }

    /// <summary>
    /// Counts the pixels that differ from <paramref name="background"/> (default
    /// opaque white) — a cheap content floor so a snapshot test cannot approve
    /// an empty render.
    /// </summary>
    public static int CountNonBackgroundPixels(SKBitmap bitmap, SKColor? background = null) =>
        PerceptualImageComparer.CountNonBackgroundPixels(bitmap, background ?? SKColors.White);

    /// <summary>Encodes an <see cref="SKBitmap"/> as a PNG byte buffer.</summary>
    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Verify an <see cref="SKBitmap"/> as a PNG snapshot. The bitmap is
    /// disposed by this method.
    /// </summary>
    public static SettingsTask VerifyBitmap(SKBitmap bitmap)
    {
        try
        {
            var bytes = EncodePng(bitmap);
            return Verifier.Verify(bytes, "png");
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>
    /// Verify an <see cref="SKBitmap"/> as a PNG snapshot with a custom maximum
    /// allowed fraction of differing pixels. Use for tests whose baseline is
    /// known to drift slightly across platforms/GPUs (e.g. font hinting or
    /// anti-aliasing differences on win-arm64). The bitmap is disposed by this
    /// method.
    /// </summary>
    /// <param name="bitmap">The rendered bitmap to verify.</param>
    /// <param name="maxDifferentPixelFraction">
    /// Maximum allowed fraction of pixels (0–1) that may exceed the per-channel
    /// delta before the comparison fails.
    /// </param>
    public static SettingsTask VerifyBitmap(SKBitmap bitmap, double maxDifferentPixelFraction)
    {
        try
        {
            var bytes = EncodePng(bitmap);
            var comparer = new PerceptualImageComparer
            {
                MaxDifferentPixelFraction = maxDifferentPixelFraction,
            };
            return Verifier.Verify(bytes, "png")
                .UsePerceptualImageComparer(comparer);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>
    /// Verify a <em>sparse</em> render — a few symbols or lines on a white
    /// chart — as a PNG snapshot. The default 5 % differing-pixel limit is
    /// 18 000 px on a 600x600 image, more than such a render's entire ink, so a
    /// sparse snapshot could lose or gain every symbol and still pass. This
    /// instead:
    /// <list type="bullet">
    ///   <item>asserts the render carries at least
    ///         <paramref name="minimumInkPixels"/> non-background pixels, so a
    ///         blank render can never be approved as a baseline; and</item>
    ///   <item>limits the differing pixels to
    ///         <paramref name="maxDifferentInkFraction"/> of the verified
    ///         image's ink (see
    ///         <see cref="PerceptualImageComparer.MaxDifferentInkFraction"/>).</item>
    /// </list>
    /// The bitmap is disposed by this method.
    /// </summary>
    /// <param name="bitmap">The rendered bitmap to verify.</param>
    /// <param name="minimumInkPixels">Smallest acceptable non-background pixel count.</param>
    /// <param name="maxDifferentInkFraction">
    /// Maximum differing pixels as a fraction of the verified image's ink. Size
    /// it from measured cross-platform drift: geometry is stable to within a
    /// few pixels, but label text shifts by a pixel or two between macOS and
    /// Linux, so every glyph pixel of a labelled render can differ.
    /// </param>
    public static SettingsTask VerifySparseBitmap(SKBitmap bitmap, int minimumInkPixels, double maxDifferentInkFraction)
    {
        try
        {
            var ink = CountNonBackgroundPixels(bitmap);
            Assert.True(
                ink >= minimumInkPixels,
                $"Render has only {ink} non-background pixels (expected at least {minimumInkPixels}); the portrayal drew (almost) nothing.");

            var bytes = EncodePng(bitmap);
            var comparer = new PerceptualImageComparer
            {
                MaxDifferentInkFraction = maxDifferentInkFraction,
            };
            return Verifier.Verify(bytes, "png")
                .UsePerceptualImageComparer(comparer);
        }
        finally
        {
            bitmap.Dispose();
        }
    }
}
