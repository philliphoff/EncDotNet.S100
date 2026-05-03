using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Captures a PNG snapshot of an Avalonia <see cref="Control"/> (typically
/// the map control) to a file. Used to fulfil the <c>--screenshot</c>
/// command-line option.
/// </summary>
internal sealed class ScreenshotService
{
    /// <summary>
    /// Renders <paramref name="target"/> to a PNG at <paramref name="outputPath"/>.
    /// Logs a message on success and on failure, but never throws.
    /// </summary>
    public void Capture(Control target, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(outputPath);

        try
        {
            var pixelSize = new PixelSize((int)target.Bounds.Width, (int)target.Bounds.Height);
            if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            {
                Console.Error.WriteLine($"[Screenshot] Target has zero size, skipping.");
                return;
            }

            using var bitmap = new RenderTargetBitmap(pixelSize);
            bitmap.Render(target);
            bitmap.Save(outputPath);

            Console.WriteLine($"[Screenshot] Saved {pixelSize.Width}x{pixelSize.Height} to {outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Screenshot] Failed: {ex.Message}");
        }
    }
}
