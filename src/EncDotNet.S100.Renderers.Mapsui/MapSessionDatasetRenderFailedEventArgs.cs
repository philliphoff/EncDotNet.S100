using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Describes a <see cref="MapsuiMapSession"/> dataset render that failed while
/// the session was refreshing other datasets and therefore swallowed the error
/// to keep them rendering.
/// </summary>
/// <remarks>
/// This is raised only for failures the session absorbs during a coalesced
/// refresh (<see cref="MapSessionRenderKind.TimeRefresh"/> or
/// <see cref="MapSessionRenderKind.PresentationRefresh"/>). A single
/// <see cref="MapsuiMapSession.RenderAsync"/> surfaces its error by throwing to
/// the awaiting caller instead.
/// </remarks>
public sealed class MapSessionDatasetRenderFailedEventArgs : MapSessionDatasetRenderEventArgs
{
    /// <summary>Creates the event arguments.</summary>
    /// <param name="datasetId">The dataset identity that failed to render.</param>
    /// <param name="kind">The refresh operation during which the failure occurred.</param>
    /// <param name="exception">The captured render failure.</param>
    public MapSessionDatasetRenderFailedEventArgs(
        MapDatasetId datasetId,
        MapSessionRenderKind kind,
        Exception exception)
        : base(datasetId, kind)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Exception = exception;
    }

    /// <summary>The captured render failure.</summary>
    public Exception Exception { get; }
}
