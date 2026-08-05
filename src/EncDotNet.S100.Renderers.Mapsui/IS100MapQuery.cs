namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Reusable geographic query surface over a <see cref="MapsuiMapSession"/>'s
/// currently-shown datasets.
/// </summary>
public interface IS100MapQuery
{
    /// <summary>
    /// Picks the vector features and coverage samples at a geographic point,
    /// ranked topmost-first by the session's S-98 paint stack (then, within a
    /// dataset, by geometry specificity and distance).
    /// </summary>
    /// <remarks>
    /// Only datasets that are active, visible, and currently rendered (in-time)
    /// participate. Each vector hit is resolved to full
    /// <see cref="Datasets.Pipelines.FeatureInfo"/>; coverage datasets with no
    /// vector hit contribute a coverage sample at the session's current time.
    /// Zoom/scale filtering is not applied — the query is purely geographic.
    /// </remarks>
    /// <param name="query">The geographic pick request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ranked picks, topmost-first (possibly empty).</returns>
    Task<IReadOnlyList<S100Pick>> PickAsync(
        GeographicPickQuery query,
        CancellationToken cancellationToken = default);
}
