using Avalonia.Controls;
using Avalonia.Threading;
using EncDotNet.S100.Renderers.Mapsui.Avalonia;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IAppScreenshotProvider"/>: renders the attached
/// <see cref="Control"/> to a PNG using Avalonia's
/// <see cref="RenderTargetBitmap"/>. All rendering is marshalled to the
/// UI thread.
/// </summary>
internal sealed class AppScreenshotProvider : IAppScreenshotProvider
{
    /// <inheritdoc />
    public Control? Target { get; set; }

    /// <inheritdoc />
    public Task<byte[]?> CapturePngAsync(CancellationToken cancellationToken = default) =>
        CapturePngAsync(1.0, cancellationToken);

    /// <inheritdoc />
    public Task<byte[]?> CapturePngAsync(double scale, CancellationToken cancellationToken = default) =>
        CapturePngAsync(scale, AppScreenshotRegion.Window, cancellationToken);

    /// <inheritdoc />
    public async Task<byte[]?> CapturePngAsync(
        double scale, AppScreenshotRegion region, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = Target;
        if (target is null)
            return null;
        if (region != AppScreenshotRegion.Window)
        {
            target = await Dispatcher.UIThread.InvokeAsync(() => FindRegion(Target, region)).GetTask().ConfigureAwait(false);
            if (target is null)
                return null;
        }

        return await AvaloniaControlCapture.CapturePngAsync(
            target,
            scale,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the control showing <paramref name="region"/> by its name in the
    /// main window, or null when it is not showing.
    /// </summary>
    private static Control? FindRegion(Control? window, AppScreenshotRegion region)
    {
        if (window is null)
            return null;
        Control? Showing(string name) =>
            window.FindControl<Control>(name) is { IsEffectivelyVisible: true } control ? control : null;
        return region switch
        {
            AppScreenshotRegion.Map => Showing("MapArea"),
            AppScreenshotRegion.LeftDock => Showing("ContentPanelHost") ?? Showing("LeftDockPanel"),
            AppScreenshotRegion.RightDock => Showing("RightDockPanel"),
            AppScreenshotRegion.BottomDock => Showing("TimelinePanel"),
            _ => window,
        };
    }
}
