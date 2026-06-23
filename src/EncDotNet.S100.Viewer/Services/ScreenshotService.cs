using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using EncDotNet.S100.Viewer.Diagnostics;

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

        using var __cmd = ViewerObservability.BeginCommand("screenshot");

        try
        {
            var pixelSize = new PixelSize((int)target.Bounds.Width, (int)target.Bounds.Height);
            if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            {
                Console.Error.WriteLine($"[Screenshot] Target has zero size, skipping.");
                return;
            }

            // RenderTargetBitmap.Render re-reads the map's GPU-resident tile
            // textures on the UI thread; left unsynchronised it races the
            // render thread's Metal paint and crashes in Skia (issue #337).
            // Route through the shared capture protocol that forces one
            // fully-drained live frame and holds the gate during the render —
            // the same guard the MCP render_to_image path uses.
            var png = RenderGate.CaptureDrained(
                target.InvalidateVisual,
                () =>
                {
                    using var bitmap = new RenderTargetBitmap(pixelSize);
                    bitmap.Render(target);
                    using var ms = new MemoryStream();
                    bitmap.Save(ms);
                    return ms.ToArray();
                });

            if (png is null)
            {
                Console.Error.WriteLine("[Screenshot] Capture produced no data.");
                return;
            }

            File.WriteAllBytes(outputPath, png);
            Console.WriteLine($"[Screenshot] Saved {pixelSize.Width}x{pixelSize.Height} to {outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Screenshot] Failed: {ex.Message}");
        }
    }
}
