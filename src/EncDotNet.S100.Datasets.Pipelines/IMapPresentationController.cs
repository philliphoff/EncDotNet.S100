namespace EncDotNet.S100.Datasets.Pipelines;

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
}
