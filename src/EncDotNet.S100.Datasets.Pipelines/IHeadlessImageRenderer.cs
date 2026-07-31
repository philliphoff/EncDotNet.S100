using EncDotNet.S100.Pipelines;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Capability implemented by dataset processors that can rasterise their
/// portrayal to a standalone <see cref="SKBitmap"/> through the headless,
/// backend-agnostic Skia core (vector products via
/// <c>HeadlessVectorRenderer</c>; coverage products via the direct Skia
/// coverage / arrow renderers) — bypassing Mapsui entirely.
/// </summary>
/// <remarks>
/// <para>
/// This is a separate capability interface rather than a member of
/// <see cref="IDatasetProcessor"/> because headless rendering is not universal:
/// some processor shapes (e.g. S-104 / S-111 fixed-station datasets, S-57) do
/// not yet have a Mapsui-free render path. Callers should feature-test with
/// <c>processor is IHeadlessImageRenderer</c> and surface a clear "headless
/// rendering not supported" message when the cast fails.
/// </para>
/// <para>
/// Implementations that <em>do</em> implement this interface but encounter an
/// unsupported dataset <em>shape</em> at render time (e.g. a fixed-station
/// coverage variant) throw <see cref="System.NotSupportedException"/> with a
/// descriptive message.
/// </para>
/// </remarks>
public interface IHeadlessImageRenderer
{
    /// <summary>
    /// Renders the dataset to a standalone <see cref="SKBitmap"/> of the
    /// requested pixel dimensions, bypassing Mapsui.
    /// </summary>
    /// <param name="widthPixels">Output bitmap width in pixels.</param>
    /// <param name="heightPixels">Output bitmap height in pixels.</param>
    /// <param name="context">
    /// Optional spec-specific render context (palette, symbol/text scale,
    /// ECDIS display, selected time step). When <c>null</c> the processor
    /// renders with its defaults.
    /// </param>
    /// <param name="background">
    /// Optional background fill; defaults to opaque white when <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    /// <exception cref="System.NotSupportedException">
    /// Thrown when this processor implements the capability in general but the
    /// specific dataset shape cannot be rendered headlessly.
    /// </exception>
    Task<SKBitmap> RenderHeadlessAsync(
        int widthPixels,
        int heightPixels,
        RenderContext? context = null,
        RgbaColor? background = null,
        CancellationToken cancellationToken = default);
}
