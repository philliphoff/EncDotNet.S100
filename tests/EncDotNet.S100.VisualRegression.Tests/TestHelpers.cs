using EncDotNet.S100.VisualRegression;
using SkiaSharp;
using VerifyTests;

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
}
