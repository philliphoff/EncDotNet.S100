using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Maps;

/// <summary>
/// Applies immutable map-wide presentation state to the datasets owned by a
/// map host or session.
/// </summary>
/// <remarks>
/// <para>
/// The contract is renderer- and UI-framework-neutral. Implementations retain
/// ownership of their processors, rendered layers, and refresh lifecycle;
/// passing a <see cref="MapPresentationState"/> does not transfer ownership.
/// </para>
/// <para>
/// A long-lived map host or session normally owns one controller for the same
/// lifetime as its loaded dataset collection. Implementations may coalesce
/// overlapping calls, but the most recently supplied state must remain
/// authoritative.
/// </para>
/// </remarks>
public interface IMapPresentationController
{
    /// <summary>
    /// Asynchronously applies <paramref name="presentation"/> to every loaded
    /// dataset.
    /// </summary>
    /// <param name="presentation">
    /// The immutable map-wide presentation snapshot to apply.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the requested application.
    /// </param>
    /// <returns>A task that completes when the presentation has been applied.</returns>
    Task SetPresentationAsync(
        MapPresentationState presentation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="presentation"/> when only how depths are drawn
    /// changed, its <see cref="MapPresentationState.DepthAdjustment"/> or its
    /// <see cref="MapPresentationState.SafetyContourHalo"/>, re-rendering just
    /// the datasets that adjust depths. The default re-applies the whole
    /// presentation.
    /// </summary>
    /// <param name="presentation">The presentation carrying the new adjustment.</param>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    /// <returns>
    /// <see langword="true"/> when the map now shows this adjustment, or
    /// <see langword="false"/> when a newer one superseded it.
    /// </returns>
    async Task<bool> SetDepthAdjustmentAsync(
        MapPresentationState presentation,
        CancellationToken cancellationToken = default)
    {
        await SetPresentationAsync(presentation, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
