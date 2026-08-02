namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Renders the current map state to an offscreen image.
/// </summary>
internal interface IMapSnapshotRenderer
{
    /// <summary>Captures the current map view as PNG-encoded bytes.</summary>
    /// <remarks>
    /// Implementations are safe to call from any thread and marshal to the UI
    /// thread when required.
    /// </remarks>
    Task<byte[]?> RenderCurrentViewToPngAsync(
        int widthPx,
        int heightPx,
        double pixelDensity,
        CancellationToken cancellationToken = default);
}
