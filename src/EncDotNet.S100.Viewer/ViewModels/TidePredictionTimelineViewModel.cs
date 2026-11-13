using System.Globalization;
using Avalonia;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Tides;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>A high or low on the Tide predictions lane, at a 0–1 position.</summary>
/// <param name="Position">Where on the lane, 0–1.</param>
/// <param name="IsHigh">High or low.</param>
/// <param name="Label">"H 3.4" or "L −0.2".</param>
/// <param name="Time">When (UTC).</param>
/// <param name="HeightMeters">The height above the station's datum, in metres.</param>
internal sealed record TideLaneMark(double Position, bool IsHigh, string Label, DateTime Time, double HeightMeters);

/// <summary>
/// The Timeline's <b>Tide predictions</b> lane (tide predictions design Q6,
/// D14): one lane with open ends that fills whatever range is shown, with the
/// curve and the highs and lows of the station nearest the map centre. It
/// never sets the axis.
/// </summary>
internal sealed class TidePredictionTimelineViewModel : ViewModelBase
{
    private const int Samples = 160;

    private readonly Func<TimelineAxisMap?> _axis;
    private readonly Func<bool> _shown;
    private readonly TideStationListService _list;
    private readonly TideStationSource _predictors;
    private readonly IMapViewportNotifier? _viewport;
    private string? _stationId;

    /// <summary>Creates the lane.</summary>
    /// <param name="axis">The Timeline's axis, or null while it has none.</param>
    /// <param name="shown">Whether predictions are on as a source or as an overlay.</param>
    /// <param name="list">The station list.</param>
    /// <param name="predictors">The station overlay's source, whose predictors it shares.</param>
    /// <param name="viewport">The map viewport, for the station nearest its centre; null for none.</param>
    public TidePredictionTimelineViewModel(
        Func<TimelineAxisMap?> axis,
        Func<bool> shown,
        TideStationListService list,
        TideStationSource predictors,
        IMapViewportNotifier? viewport)
    {
        ArgumentNullException.ThrowIfNull(axis);
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(predictors);
        _axis = axis;
        _shown = shown;
        _list = list;
        _predictors = predictors;
        _viewport = viewport;
    }

    /// <summary>Whether the lane shows: predictions are on and a station is near the map centre.</summary>
    public bool HasLane { get; private set; }

    /// <summary>The station the curve is for: "Seattle 9447130 · 4.2 km from centre".</summary>
    public string StationText { get; private set; } = string.Empty;

    /// <summary>The curve, as 0–1 positions along and up the lane.</summary>
    public IReadOnlyList<Point> Curve { get; private set; } = [];

    /// <summary>The highs and lows on the lane.</summary>
    public IReadOnlyList<TideLaneMark> Marks { get; private set; } = [];

    /// <summary>The lane's tooltip.</summary>
    public string Tooltip => Strings.TimelinePanel_TideLaneTooltip;

    /// <summary>The station id the lane follows, or null.</summary>
    public string? StationId => _stationId;

    /// <summary>Recomputes the lane for the current axis, map centre and list.</summary>
    public void Refresh()
    {
        var axis = _axis();
        var catalogue = _list.Catalogue;
        var centre = _viewport?.Current is { } v
            ? new GeoPosition((v.MinLatitude + v.MaxLatitude) / 2, (v.MinLongitude + v.MaxLongitude) / 2)
            : (GeoPosition?)null;
        ITidePredictor? predictor = null;
        string text = string.Empty;
        _stationId = null;
        if (_shown() && axis is not null && catalogue is not null && centre is { } c)
        {
            foreach (var match in catalogue.FindNearest(c, maxCount: 5, maxDistanceMeters: 100_000))
            {
                if (_predictors.PredictorFor(catalogue, match.Station.Id) is { } p)
                {
                    predictor = p;
                    _stationId = match.Station.Id;
                    text = string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.TimelinePanel_TideLaneStationFormat,
                        match.Station.Name,
                        match.Station.Id,
                        match.DistanceMeters / 1000);
                    break;
                }
            }
        }

        if (predictor is null || axis is null)
        {
            Set(false, string.Empty, [], []);
            return;
        }

        // Sample evenly along the lane (through collapsed gaps too), then fit to its height.
        var times = new DateTime[Samples];
        var heights = new double[Samples];
        for (var i = 0; i < Samples; i++)
        {
            times[i] = axis.ToTime((double)i / (Samples - 1));
            heights[i] = predictor.HeightAt(times[i]);
        }

        var extremes = predictor.FindExtremes(axis.Start, axis.End);
        var min = Math.Min(heights.Min(), extremes.Count > 0 ? extremes.Min(e => e.HeightMeters) : double.MaxValue);
        var max = Math.Max(heights.Max(), extremes.Count > 0 ? extremes.Max(e => e.HeightMeters) : double.MinValue);
        var range = max - min < 0.1 ? 0.1 : max - min;
        var curve = new Point[Samples];
        for (var i = 0; i < Samples; i++)
        {
            curve[i] = new Point((double)i / (Samples - 1), (heights[i] - min) / range);
        }

        var marks = extremes
            .Select(e => new TideLaneMark(
                axis.ToPosition(e.Time),
                e.Kind == TideExtremeKind.High,
                string.Format(
                    CultureInfo.CurrentCulture,
                    e.Kind == TideExtremeKind.High ? Strings.TimelinePanel_TideLaneHighFormat : Strings.TimelinePanel_TideLaneLowFormat,
                    e.HeightMeters),
                e.Time,
                e.HeightMeters))
            .ToArray();
        Set(true, text, curve, marks);
    }

    private void Set(bool hasLane, string text, IReadOnlyList<Point> curve, IReadOnlyList<TideLaneMark> marks)
    {
        HasLane = hasLane;
        StationText = text;
        Curve = curve;
        Marks = marks;
        OnPropertyChanged(nameof(HasLane));
        OnPropertyChanged(nameof(StationText));
        OnPropertyChanged(nameof(Curve));
        OnPropertyChanged(nameof(Marks));
        OnPropertyChanged(nameof(StationId));
    }
}
