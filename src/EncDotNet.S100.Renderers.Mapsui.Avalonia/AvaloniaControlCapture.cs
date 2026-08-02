using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia;

/// <summary>
/// Captures an Avalonia control as PNG bytes while coordinating with live
/// Mapsui painting.
/// </summary>
public static class AvaloniaControlCapture
{
    /// <summary>
    /// Renders <paramref name="target"/> to PNG-encoded bytes on Avalonia's UI
    /// thread.
    /// </summary>
    /// <param name="target">The control to capture.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// PNG bytes, or <see langword="null"/> when the target has no positive
    /// laid-out size.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="target"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> is canceled.
    /// </exception>
    public static async Task<byte[]?> CapturePngAsync(
        Control target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        var hasLayout = await InvokeOnUiThreadAsync(
            () => target.Bounds.Width > 0 && target.Bounds.Height > 0)
            .ConfigureAwait(false);
        if (!hasLayout)
        {
            return null;
        }

        return await CaptureCoordinator.CaptureDrainedAsync(
            () => InvokeOnUiThreadAsync(target.InvalidateVisual),
            () => InvokeOnUiThreadAsync(
                () => CaptureOnUiThread(target, cancellationToken)),
            cancellationToken).ConfigureAwait(false);
    }

    private static byte[]? CaptureOnUiThread(
        Control target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pixelSize = new PixelSize(
            (int)Math.Round(target.Bounds.Width),
            (int)Math.Round(target.Bounds.Height));
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
        {
            return null;
        }

        using var bitmap = new RenderTargetBitmap(pixelSize);
        bitmap.Render(target);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    private static Task InvokeOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    private static Task<T> InvokeOnUiThreadAsync<T>(Func<T> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return Task.FromResult(action());
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
