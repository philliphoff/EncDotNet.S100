using SkiaSharp;

namespace EncDotNet.S100.Testing.Rendering;

/// <summary>
/// Produces a "diff image" highlighting pixels where two images disagree, for
/// human / agent inspection when a baseline comparison fails.
/// </summary>
/// <remarks>
/// The diff image is the same dimensions as the inputs. Matching pixels are
/// rendered as a desaturated, dimmed copy of the actual image so the user can
/// see context; mismatching pixels are overlaid in red with full opacity.
/// </remarks>
public static class DiffImageWriter
{
    /// <summary>
    /// Writes a diff PNG to <paramref name="diffPath"/>. The two input images
    /// must have the same dimensions; otherwise the diff is a solid-red
    /// dimensionally-largest rectangle (still useful for catching the
    /// dimension mismatch visually).
    /// </summary>
    /// <param name="expected">Baseline PNG bytes.</param>
    /// <param name="actual">Received PNG bytes.</param>
    /// <param name="diffPath">Absolute path where the diff PNG should be written.</param>
    /// <param name="channelDeltaThreshold">Per-channel difference above which a pixel is highlighted. Default: 4.</param>
    public static void Write(byte[] expected, byte[] actual, string diffPath, int channelDeltaThreshold = 4)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentException.ThrowIfNullOrEmpty(diffPath);

        using var expectedBmp = SKBitmap.Decode(expected);
        using var actualBmp = SKBitmap.Decode(actual);
        Write(expectedBmp, actualBmp, diffPath, channelDeltaThreshold);
    }

    /// <summary>
    /// Writes a diff PNG to <paramref name="diffPath"/> from two pre-decoded bitmaps.
    /// </summary>
    public static void Write(SKBitmap expected, SKBitmap actual, string diffPath, int channelDeltaThreshold = 4)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentException.ThrowIfNullOrEmpty(diffPath);

        int width = Math.Max(expected.Width, actual.Width);
        int height = Math.Max(expected.Height, actual.Height);

        using var diff = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var highlight = new SKColor(255, 0, 0, 255);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool inE = x < expected.Width && y < expected.Height;
                bool inA = x < actual.Width && y < actual.Height;
                if (!inE || !inA)
                {
                    diff.SetPixel(x, y, highlight);
                    continue;
                }

                var e = expected.GetPixel(x, y);
                var a = actual.GetPixel(x, y);
                int delta = Math.Max(Math.Max(Math.Abs(e.Red - a.Red), Math.Abs(e.Green - a.Green)),
                                     Math.Max(Math.Abs(e.Blue - a.Blue), Math.Abs(e.Alpha - a.Alpha)));
                if (delta > channelDeltaThreshold)
                {
                    diff.SetPixel(x, y, highlight);
                }
                else
                {
                    // Dimmed greyscale of the actual pixel for context.
                    int luma = (a.Red * 30 + a.Green * 59 + a.Blue * 11) / 100;
                    int dim = (luma + 255) / 2; // shift toward white so red highlights pop
                    diff.SetPixel(x, y, new SKColor((byte)dim, (byte)dim, (byte)dim, 255));
                }
            }
        }

        var dir = Path.GetDirectoryName(diffPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var image = SKImage.FromBitmap(diff);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(diffPath);
        data.SaveTo(fs);
    }
}
