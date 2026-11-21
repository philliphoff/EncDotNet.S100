using EncDotNet.S100.Collections;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services.Depth;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>Tide predictions as the DEPTHS indicator sees them, for a view.</summary>
internal interface ITidePredictionsInView
{
    /// <summary>Raised when what <see cref="For"/> returns may have changed.</summary>
    event EventHandler? Changed;

    /// <summary>The predictions for a view.</summary>
    /// <param name="view">The view, or null when unknown.</param>
    TidePredictionsInView For(GeoBounds? view);
}

/// <summary>
/// Tide predictions as the DEPTHS indicator sees them (tide predictions
/// design D2, D9, D15): whether they're in use, the stations in reach of the
/// view, and whether the station list is stale.
/// </summary>
internal sealed class TidePredictionsInViewSource : ITidePredictionsInView
{
    /// <summary>How far a station's prediction reaches (D19): 10 km.</summary>
    public const double ReachMeters = 10_000;

    private const double MetersPerDegreeLatitude = 111_320;

    private readonly TideStationListService _list;
    private readonly TidePredictionSourceService _predictions;

    /// <summary>Creates the source.</summary>
    /// <param name="list">The station list.</param>
    /// <param name="predictions">The source switch's state.</param>
    public TidePredictionsInViewSource(TideStationListService list, TidePredictionSourceService predictions)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(predictions);
        _list = list;
        _predictions = predictions;
        _list.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _predictions.StateChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public TidePredictionsInView For(GeoBounds? view)
    {
        if (_list.Catalogue is not { } catalogue || view is not { } v)
        {
            // Read the cached list in the background for the flyout's offer (D2).
            _ = _list.EnsureLoadedAsync();
            return TidePredictionsInView.None;
        }

        var padLat = ReachMeters / MetersPerDegreeLatitude;
        var padLon = padLat / Math.Max(0.1, Math.Cos(((v.South + v.North) / 2) * Math.PI / 180));
        var box = new BoundingBox(
            southLatitude: Math.Max(-90, v.South - padLat),
            westLongitude: v.West - padLon,
            northLatitude: Math.Min(90, v.North + padLat),
            eastLongitude: v.East + padLon);
        var centre = new GeoPosition((v.South + v.North) / 2, (v.West + v.East) / 2);
        var stations = catalogue.FindWithin(box, centre)
            .Where(m => catalogue.TryCreatePrediction(m.Station.Id, VerticalDatum.MeanLowerLowWater) is not null)
            .Select(m => $"{m.Station.Name} {m.Station.Id}")
            .ToArray();
        return new TidePredictionsInView(
            _predictions.State == TidePredictionSourceState.InUse,
            stations,
            (int)VerticalDatum.MeanLowerLowWater,
            _list.IsStale,
            catalogue.Retrieved);
    }
}
