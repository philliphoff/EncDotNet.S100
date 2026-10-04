using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
    public static Task<byte[]?> CapturePngAsync(
        Control target,
        CancellationToken cancellationToken = default) =>
        CapturePngAsync(target, 1.0, cancellationToken);

    /// <summary>
    /// Renders <paramref name="target"/> to PNG-encoded bytes on Avalonia's UI
    /// thread at <paramref name="scale"/> device pixels per logical pixel.
    /// </summary>
    /// <param name="target">The control to capture.</param>
    /// <param name="scale">
    /// Device pixels per logical pixel: <c>1</c> renders at 96 dpi, <c>2</c>
    /// matches a Retina / HiDPI display.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// PNG bytes, or <see langword="null"/> when the target has no positive
    /// laid-out size.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="target"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="scale"/> is not a positive, finite number.
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
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Scale must be a positive, finite number.");
        }

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
                () => CaptureOnUiThread(target, scale, cancellationToken))
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
                () => CaptureOnUiThread(target, scale, cancellationToken)),
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
        double scale,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pixelSize = new PixelSize(
            (int)Math.Round(target.Bounds.Width * scale),
            (int)Math.Round(target.Bounds.Height * scale));
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
        {
            return null;
        }

        using var bitmap = new RenderTargetBitmap(pixelSize);

        // RenderTargetBitmap renders synchronously on this thread through the
        // same live layers. Mark it off-screen so the tile renderer composites
        // the live view's cached tiles instead of scheduling tiles at the
        // capture's scale that the live window would then keep blitting.
        using (S100VectorTileRenderer.BeginOffscreenRender())
        {
            if (scale == 1.0)
            {
                bitmap.Render(target);
            }
            else
            {
                // Scale with a transform on a 96-dpi bitmap rather than rendering
                // at 96 × scale dpi: the high-dpi path hides content under
                // BoxShadow effects (e.g. the Library's selected segment label).
                using var context = bitmap.CreateDrawingContext();
                using (context.PushTransform(Matrix.CreateScale(scale, scale)))
                {
                    var size = target.Bounds.Size;
                    context.FillRectangle(
                        new VisualBrush(target)
                        {
                            Stretch = Stretch.None,
                            AlignmentX = AlignmentX.Left,
                            AlignmentY = AlignmentY.Top,
                            SourceRect = new RelativeRect(0, 0, size.Width, size.Height, RelativeUnit.Absolute),
                            DestinationRect = new RelativeRect(0, 0, size.Width, size.Height, RelativeUnit.Absolute),
                        },
                        new Rect(size));
                }
            }
        }

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
