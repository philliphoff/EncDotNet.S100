using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mapsui.UI.Avalonia;

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
    /// <exception cref="InvalidOperationException">
    /// The target contains a Mapsui control that does not derive from
    /// <see cref="CaptureSynchronizedMapControl"/>.
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

        var requiresSynchronization = await InvokeOnUiThreadAsync(
            () => RequiresCaptureSynchronization(target))
            .ConfigureAwait(false);
        if (!requiresSynchronization)
        {
            return await InvokeOnUiThreadAsync(
                () => CaptureOnUiThread(target, cancellationToken))
                .ConfigureAwait(false);
        }

        // The capture re-renders the control tree on the UI thread
        // (RenderTargetBitmap.Render below), which re-enters the map control's
        // live-paint markers on that same UI thread. Those markers acquire the
        // capture gate themselves, so they already serialize this capture against
        // a concurrent compositor paint — do NOT also hold the gate on a worker
        // thread here, or the UI-thread marker would deadlock waiting on a holder
        // that is itself awaiting this UI-thread render (acquireGate: false).
        return await CaptureCoordinator.CaptureDrainedAsync(
            () => InvokeOnUiThreadAsync(target.InvalidateVisual),
            () => InvokeOnUiThreadAsync(
                () => CaptureOnUiThread(target, cancellationToken)),
            cancellationToken,
            acquireGate: false).ConfigureAwait(false);
    }

    internal static bool RequiresCaptureSynchronization(Control target)
    {
        var mapControls = target.GetVisualDescendants().OfType<MapControl>().ToList();
        if (target is MapControl mapControl)
        {
            mapControls.Add(mapControl);
        }

        if (mapControls.Any(control => control is not CaptureSynchronizedMapControl))
        {
            throw new InvalidOperationException(
                "Mapsui controls must derive from CaptureSynchronizedMapControl " +
                "before their control tree can be captured.");
        }

        return mapControls.Count > 0;
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
