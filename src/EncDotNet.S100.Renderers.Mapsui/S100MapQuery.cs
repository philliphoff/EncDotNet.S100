namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Default <see cref="IS100MapQuery"/> — delegates to its owning
/// <see cref="MapsuiDatasetLayerSession"/>, which holds the S-98 stack, per-dataset
/// state, and processor leases the pick ranks and resolves against.
/// </summary>
internal sealed class S100MapQuery : IS100MapQuery
{
    private readonly MapsuiDatasetLayerSession _session;

    internal S100MapQuery(MapsuiDatasetLayerSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<S100Pick>> PickAsync(
        GeographicPickQuery query,
        CancellationToken cancellationToken = default) =>
        _session.PickAsync(query, cancellationToken);
}
