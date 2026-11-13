using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>A predicted current station as drawn at the view time.</summary>
/// <param name="StationId">The station and depth bin id, such as <c>PUG1701_33</c>.</param>
/// <param name="Name">The station's name.</param>
/// <param name="Kind">Harmonic or subordinate.</param>
/// <param name="Position">Where the station is.</param>
/// <param name="DepthMeters">The current's depth below the surface, or <c>null</c>.</param>
/// <param name="SpeedKnots">The predicted speed, in knots.</param>
/// <param name="DirectionDegrees">Where the current flows, in degrees true.</param>
/// <param name="IsFlood">Whether it's flooding.</param>
/// <param name="IsSlack">Whether it's within <see cref="TideStationSource.TurningWindow"/> of a slack.</param>
/// <param name="Label">"2.6 kn", or "slack 14:35".</param>
/// <param name="Time">The view time the state is for (UTC).</param>
internal sealed record CurrentStationState(
    string StationId,
    string Name,
    TideStationKind Kind,
    GeoPosition Position,
    double? DepthMeters,
    double SpeedKnots,
    double DirectionDegrees,
    bool IsFlood,
    bool IsSlack,
    string Label,
    DateTime Time);

/// <summary>
/// The <b>Current stations · predicted</b> overlay (tide predictions design
/// D16, D22): NOAA's tidal current stations in view, each with its predicted
/// speed and direction at the view time, under <i>Other chart overlays</i>
/// below Tide stations.
/// </summary>
/// <remarks>
/// Drawn from 1:500,000 and thinned to 40 px like the tide stations: harmonic
/// first, then the shallowest depth bin, then by id. A station S-111 covers at
/// the view time isn't drawn: S-111 wins, as S-104 does for tides. Rotary
/// stations draw the arrow at the predicted direction (D22).
/// </remarks>
internal sealed class CurrentStationSource : IDynamicFeatureSource, IDynamicFeatureDescriber
{
    /// <summary>The source id, and its key in <c>ViewerSettings.DynamicSourceVisibility</c>.</summary>
    public const string SourceId = "current-stations";

    /// <summary>The features' kind, and the renderer key.</summary>
    public const string FeatureKind = "tides.current.predicted";

    /// <summary>The attribute holding a feature's <see cref="CurrentStationState"/>.</summary>
    public const string StateAttribute = "currentStation";

    /// <summary>Within-plane priority on <see cref="S98DisplayPlane.OtherChartOverlays"/>: just below Tide stations.</summary>
    public const int WithinPlanePriority = 105;

    private readonly TideStationListService _list;
    private readonly Func<DateTime> _viewTime;
    private readonly Func<TimeFormat> _format;
    private readonly Func<GeoPosition, DateTime, bool> _coveredByS111;
    private readonly object _gate = new();
    private readonly Dictionary<string, ICurrentPredictor?> _predictors = new(StringComparer.Ordinal);
    private TideStationCatalogue? _predictorsFor;
    private IReadOnlyList<DynamicFeature> _snapshot = [];
    private IReadOnlyList<CurrentStationState> _inView = [];
    private MapViewportSnapshot? _view;
    private bool _isActive;

    /// <summary>Creates the source.</summary>
    /// <param name="list">The station list.</param>
    /// <param name="viewTime">The view time (UTC).</param>
    /// <param name="format">The Local/UTC setting of the slack label.</param>
    /// <param name="coveredByS111">Whether a loaded S-111 covers a position at a time.</param>
    public CurrentStationSource(
        TideStationListService list,
        Func<DateTime> viewTime,
        Func<TimeFormat> format,
        Func<GeoPosition, DateTime, bool> coveredByS111)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(viewTime);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(coveredByS111);
        _list = list;
        _viewTime = viewTime;
        _format = format;
        _coveredByS111 = coveredByS111;
        Metadata = new DynamicSourceMetadata
        {
            DisplayName = Strings.Tides_CurrentsLayer,
            Description = Strings.Tides_CurrentsLayer_Description,
            RendererKey = FeatureKind,
            Plane = S98DisplayPlane.OtherChartOverlays,
            WithinPlanePriority = WithinPlanePriority,
        };
        _list.Changed += (_, _) => Refresh();
    }

    /// <inheritdoc />
    public string Id => SourceId;

    /// <inheritdoc />
    public DynamicSourceMetadata Metadata { get; }

    /// <inheritdoc />
    public IReadOnlyList<DynamicFeature> CurrentFeatures
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    /// <summary>The stations in view at a drawable scale, before thinning and S-111 suppression.</summary>
    public IReadOnlyList<CurrentStationState> StationsInView
    {
        get
        {
            lock (_gate)
            {
                return _inView;
            }
        }
    }

    /// <summary>Whether the overlay is on.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _isActive;
            }
        }

        set
        {
            lock (_gate)
            {
                if (_isActive == value)
                {
                    return;
                }

                _isActive = value;
            }

            if (value)
            {
                _ = LoadThenRefreshAsync();
            }
            else
            {
                Refresh();
            }

            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public event EventHandler<DynamicFeaturesChanged>? Changed;

    /// <summary>Raised when <see cref="IsActive"/> changes.</summary>
    public event EventHandler? ActiveChanged;

    /// <summary>Raised after the features were recomputed.</summary>
    public event EventHandler? Refreshed;

    /// <summary>The station's state carried by a feature, or <c>null</c>.</summary>
    /// <param name="feature">The feature.</param>
    public static CurrentStationState? GetState(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Attributes.TryGetValue(StateAttribute, out var value) ? value as CurrentStationState : null;
    }

    /// <summary>The feature id of a station.</summary>
    /// <param name="stationId">The station id.</param>
    public static string FeatureIdFor(string stationId) => "current:" + stationId;

    /// <summary>Sets the viewport and recomputes.</summary>
    /// <param name="view">The viewport.</param>
    public void UpdateView(MapViewportSnapshot? view)
    {
        lock (_gate)
        {
            _view = view;
        }

        Refresh();
    }

    /// <summary>A station's predictor, or <c>null</c> when it can't be predicted.</summary>
    /// <param name="catalogue">The list.</param>
    /// <param name="stationId">The station.</param>
    public ICurrentPredictor? PredictorFor(TideStationCatalogue catalogue, string stationId)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        lock (_gate)
        {
            if (!ReferenceEquals(catalogue, _predictorsFor))
            {
                _predictors.Clear();
                _predictorsFor = catalogue;
            }

            if (!_predictors.TryGetValue(stationId, out var predictor))
            {
                predictor = catalogue.TryCreateCurrentPrediction(stationId)?.Predictor;
                _predictors[stationId] = predictor;
            }

            return predictor;
        }
    }

    /// <summary>Recomputes the features for the current view, view time and list.</summary>
    public void Refresh()
    {
        MapViewportSnapshot? view;
        bool active;
        lock (_gate)
        {
            view = _view;
            active = _isActive;
        }

        var catalogue = _list.Catalogue;
        IReadOnlyList<CurrentStationState> inView = [];
        IReadOnlyList<DynamicFeature> features = [];
        if (active && catalogue is not null && view is not null && StationThinning.IsDrawable(view))
        {
            var time = _viewTime();
            inView = Compute(catalogue, view, time);
            features = StationThinning.Thin(
                    inView
                        .OrderBy(s => s.Kind == TideStationKind.Harmonic ? 0 : 1)
                        .ThenBy(s => s.DepthMeters ?? double.MaxValue)
                        .ThenBy(s => s.StationId, StringComparer.Ordinal),
                    s => s.Position,
                    view.MercatorResolution * TideStationSource.ThinningPixels)
                .Where(s => !_coveredByS111(s.Position, time))
                .Select(ToFeature)
                .ToArray();
        }

        bool changed;
        lock (_gate)
        {
            changed = _snapshot.Count != 0 || features.Count != 0;
            _snapshot = features;
            _inView = inView;
        }

        if (changed)
        {
            Changed?.Invoke(this, new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset });
        }

        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public DynamicFeatureDescription? Describe(DynamicFeature feature, IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (GetState(feature) is not { } state)
        {
            return null;
        }

        var culture = formatProvider ?? CultureInfo.InvariantCulture;
        return new DynamicFeatureDescription(
            state.Name,
            [
                new(Strings.Tides_Pick_Station, state.StationId),
                new(Strings.Tides_Pick_Kind, TideStationSource.KindText(state.Kind)),
                new(Strings.Tides_Currents_Pick_Current, state.IsSlack
                    ? state.Label
                    : string.Format(culture, Strings.Tides_Currents_SpeedDirectionFormat, state.SpeedKnots, state.DirectionDegrees)),
            ]);
    }

    private IReadOnlyList<CurrentStationState> Compute(TideStationCatalogue catalogue, MapViewportSnapshot view, DateTime time)
    {
        var format = _format();
        var result = new List<CurrentStationState>();
        foreach (var match in catalogue.FindCurrentStationsWithin(StationThinning.Box(view)))
        {
            var station = match.Station;
            if (PredictorFor(catalogue, station.Id) is not { } predictor)
            {
                continue;
            }

            var current = predictor.CurrentAt(time);
            var slack = predictor.FindEvents(time - TideStationSource.TurningWindow, time + TideStationSource.TurningWindow)
                .Where(e => e.IsSlack)
                .Select(e => (CurrentEvent?)e)
                .MinBy(e => Math.Abs((e!.Value.Time - time).Ticks));
            var label = slack is { } s
                ? string.Format(CultureInfo.CurrentCulture, Strings.Tides_Currents_SlackLabelFormat, TimeFormatting.FormatClock(s.Time, format))
                : string.Format(CultureInfo.CurrentCulture, Strings.Tides_Currents_SpeedLabelFormat, current.SpeedKnots);
            result.Add(new CurrentStationState(
                station.Id,
                station.Name,
                station.Kind,
                station.Position,
                station.DepthMeters,
                current.SpeedKnots,
                current.DirectionDegrees,
                current.IsFlood,
                slack is not null,
                label,
                time));
        }

        return result;
    }

    private static DynamicFeature ToFeature(CurrentStationState state) => new()
    {
        Id = FeatureIdFor(state.StationId),
        Kind = FeatureKind,
        GeometryType = GeometryType.Point,
        Coordinates = [state.Position],
        Attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { [StateAttribute] = state },
        LastUpdated = new DateTimeOffset(DateTime.SpecifyKind(state.Time, DateTimeKind.Utc)),
    };

    private async Task LoadThenRefreshAsync()
    {
        await _list.EnsureAvailableAsync().ConfigureAwait(false);
        Refresh();
    }
}
