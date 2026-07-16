using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

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

        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var target = Target;
            if (target is null)
                return null;

            var width = (int)Math.Round(target.Bounds.Width);
            var height = (int)Math.Round(target.Bounds.Height);
            if (width <= 0 || height <= 0)
                return null;

            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
            bitmap.Render(target);

            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return stream.ToArray();
        });
    }
}
