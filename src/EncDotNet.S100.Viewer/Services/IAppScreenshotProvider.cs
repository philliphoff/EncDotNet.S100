using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Captures a PNG snapshot of the running application window (or a
/// designated <see cref="Control"/>) for the feedback reporter.
/// </summary>
/// <remarks>
/// <para>
/// The capture target is the live <c>MainWindow</c>, which only exists
/// after DI is built. <see cref="Target"/> is therefore late-bound by
/// the window during its construction — mirroring the
/// <see cref="IMapHostAccessor"/> pattern — so services resolved earlier
/// can hold the provider and read the target at capture time.
/// </para>
/// <para>
/// Capturing the whole window yields a true "application" screenshot
/// (chart plus surrounding chrome). When no window target is available
/// the feedback service falls back to the map-only PNG produced by
/// <see cref="IMapHost.RenderCurrentViewToPngAsync"/>.
/// </para>
/// </remarks>
internal interface IAppScreenshotProvider
{
    /// <summary>
    /// The control to capture (typically the main window), or
    /// <see langword="null"/> when not yet attached.
    /// </summary>
    Control? Target { get; set; }

    /// <summary>
    /// Renders <see cref="Target"/> to PNG-encoded bytes, marshalling to
    /// the UI thread as needed.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// PNG bytes of the current window, or <see langword="null"/> when no
    /// target is attached or it has no on-screen size yet.
    /// </returns>
    Task<byte[]?> CapturePngAsync(CancellationToken cancellationToken = default);
}
