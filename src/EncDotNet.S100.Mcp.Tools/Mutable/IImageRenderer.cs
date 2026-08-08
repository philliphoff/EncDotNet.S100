namespace EncDotNet.S100.Mcp.Tools.Mutable;

/// <summary>
/// Renders the session's current state — loaded datasets, presentation, time,
/// and viewport — to a PNG image. Backs the <c>render_to_image</c> tool.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam that makes headless MCP validation possible: an agent can
/// <c>open_dataset</c> → <c>set_palette</c> → render → assert on the bytes with
/// no window and no live render loop. The desktop viewer implements it by
/// snapshotting its live Mapsui map; the CLI implements it over the same
/// headless Skia pipeline its <c>render</c> command already uses — which is why
/// the CLI path runs in CI where a GUI screenshot cannot.
/// </para>
/// <para>
/// Rendering never mutates session state. The method returns
/// <see langword="null"/> when there is nothing to render (no viewport / zero
/// size); the calling tool maps that to a not-ready error.
/// </para>
/// </remarks>
public interface IImageRenderer
{
    /// <summary>
    /// Renders the current view to PNG-encoded bytes at the requested size.
    /// </summary>
    /// <param name="widthPx">Output width in pixels.</param>
    /// <param name="heightPx">Output height in pixels.</param>
    /// <param name="pixelDensity">
    /// Display pixel-density multiplier (<c>1.0</c> = device-independent pixels;
    /// <c>2.0</c> = HiDPI).
    /// </param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>
    /// PNG bytes, or <see langword="null"/> when there is nothing to render.
    /// </returns>
    Task<byte[]?> RenderToPngAsync(
        int widthPx,
        int heightPx,
        double pixelDensity,
        CancellationToken cancellationToken = default);
}
