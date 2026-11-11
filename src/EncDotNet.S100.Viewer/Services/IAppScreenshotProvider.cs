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
/// <see cref="ICapabilityAccessor{TCapability}"/> pattern — so services resolved earlier
/// can hold the provider and read the target at capture time.
/// </para>
/// <para>
/// Capturing the whole window yields a true "application" screenshot
/// (chart plus surrounding chrome). When no window target is available
/// the feedback service falls back to the map-only PNG produced by
/// <see cref="EncDotNet.S100.Maps.IImageRenderer.RenderToPngAsync"/>.
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

    /// <summary>
    /// Renders <see cref="Target"/> to PNG-encoded bytes at
    /// <paramref name="scale"/> device pixels per logical pixel (2 for a
    /// Retina-sharp image), marshalling to the UI thread as needed.
    /// </summary>
    /// <param name="scale">Device pixels per logical pixel; must be positive.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// PNG bytes of the current window, or <see langword="null"/> when no
    /// target is attached or it has no on-screen size yet.
    /// </returns>
    Task<byte[]?> CapturePngAsync(double scale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders one <paramref name="region"/> of <see cref="Target"/> (a dock or
    /// the map) to PNG-encoded bytes at <paramref name="scale"/> device pixels
    /// per logical pixel, marshalling to the UI thread as needed.
    /// </summary>
    /// <param name="scale">Device pixels per logical pixel; must be positive.</param>
    /// <param name="region">The part of the window to capture.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// PNG bytes of the region, or <see langword="null"/> when no target is
    /// attached or the region is not showing.
    /// </returns>
    Task<byte[]?> CapturePngAsync(double scale, AppScreenshotRegion region, CancellationToken cancellationToken = default);
}

/// <summary>A part of the main window that <see cref="IAppScreenshotProvider"/> can capture on its own.</summary>
internal enum AppScreenshotRegion
{
    /// <summary>The whole window.</summary>
    Window,

    /// <summary>The map, with its overlays (Time HUD, chips, scale bar).</summary>
    Map,

    /// <summary>The left dock, or the full-content panel (Settings) while it is open.</summary>
    LeftDock,

    /// <summary>The right dock.</summary>
    RightDock,

    /// <summary>The bottom dock (Timeline).</summary>
    BottomDock,
}
