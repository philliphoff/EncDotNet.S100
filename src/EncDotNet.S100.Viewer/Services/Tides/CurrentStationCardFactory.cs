using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>Makes the Object information card of a picked predicted current station (D16).</summary>
internal sealed class CurrentStationCardFactory
{
    private readonly TideStationListService _list;
    private readonly CurrentStationSource _source;
    private readonly Func<CurrentCardContext> _context;

    /// <summary>Creates the factory.</summary>
    /// <param name="list">The station list.</param>
    /// <param name="source">The overlay's source, whose predictors it reuses.</param>
    /// <param name="context">The cards' surroundings, resolved on first pick.</param>
    public CurrentStationCardFactory(TideStationListService list, CurrentStationSource source, Func<CurrentCardContext> context)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        _list = list;
        _source = source;
        _context = context;
    }

    /// <summary>
    /// The card of a picked station's <see cref="PredictedStationSection"/>
    /// (data source design note slice 7d), or <see langword="null"/> to show
    /// its rows: another kind, or a station not in the list.
    /// </summary>
    /// <param name="pickSection">The section.</param>
    public object? Present(EncDotNet.S100.Datasets.Pipelines.PickSection pickSection)
    {
        ArgumentNullException.ThrowIfNull(pickSection);
        if (pickSection is not PredictedStationSection { SectionKind: PredictedStationSection.CurrentKind } section
            || _list.Catalogue is not { } catalogue
            || catalogue.FindCurrentStation(section.StationId) is not { } station
            || _source.PredictorFor(catalogue, station.Id) is not { } predictor)
        {
            return null;
        }

        return new CurrentStationCardViewModel(catalogue, station, predictor, _context());
    }
}
