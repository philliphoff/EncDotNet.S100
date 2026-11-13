using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>Makes the Object information card of a picked predicted tide station (D8).</summary>
internal sealed class TideStationCardFactory : IDynamicPickCardFactory
{
    private readonly TideStationListService _list;
    private readonly TideStationSource _source;
    private readonly Func<TideCardContext> _context;

    /// <summary>Creates the factory.</summary>
    /// <param name="list">The station list.</param>
    /// <param name="source">The overlay's source, whose predictors it reuses.</param>
    /// <param name="context">The cards' surroundings, resolved on first pick.</param>
    public TideStationCardFactory(TideStationListService list, TideStationSource source, Func<TideCardContext> context)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        _list = list;
        _source = source;
        _context = context;
    }

    /// <inheritdoc />
    public IDynamicPickCard? CreateCard(DynamicSourceHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (!ReferenceEquals(hit.Source, _source)
            || TideStationSource.GetState(hit.Feature) is not { } state
            || _list.Catalogue is not { } catalogue
            || catalogue.Find(state.StationId) is not { } station
            || _source.PredictorFor(catalogue, station.Id) is not { } predictor)
        {
            return null;
        }

        return new TideStationCardViewModel(catalogue, station, predictor, _context());
    }
}
