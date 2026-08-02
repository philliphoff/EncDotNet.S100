using Avalonia.Controls;
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
    public async Task<byte[]?> CapturePngAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = Target;
        return target is null
            ? null
            : await AvaloniaControlCapture.CapturePngAsync(
                target,
                cancellationToken).ConfigureAwait(false);
    }
}
