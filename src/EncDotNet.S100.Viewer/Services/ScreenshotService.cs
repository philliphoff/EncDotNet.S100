using Avalonia.Controls;
using EncDotNet.S100.Renderers.Mapsui.Avalonia;
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
    public async Task CaptureAsync(
        Control target,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(outputPath);

        using var __cmd = ViewerObservability.BeginCommand("screenshot");

        try
        {
            var png = await AvaloniaControlCapture.CapturePngAsync(
                target,
                cancellationToken);

            if (png is null)
            {
                Console.Error.WriteLine("[Screenshot] Capture produced no data.");
                return;
            }

            await File.WriteAllBytesAsync(outputPath, png, cancellationToken);
            Console.WriteLine(
                $"[Screenshot] Saved {(int)target.Bounds.Width}x{(int)target.Bounds.Height} to {outputPath}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("[Screenshot] Capture canceled.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Screenshot] Failed: {ex.Message}");
        }
    }
}
