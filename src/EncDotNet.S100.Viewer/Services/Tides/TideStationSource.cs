using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>Whether the predicted tide is rising, falling or turning at the view time.</summary>
internal enum TideTrend
{
    /// <summary>Rising.</summary>
    Rising,

    /// <summary>Falling.</summary>
    Falling,

    /// <summary>Within <see cref="TideStationSource.TurningWindow"/> of a high or low.</summary>
    Turning,
}

/// <summary>A predicted tide station as drawn at the view time.</summary>
/// <param name="StationId">The station id, such as <c>9447130</c>.</param>
/// <param name="Name">The station's name.</param>
/// <param name="Kind">Harmonic or subordinate.</param>
/// <param name="Position">Where the station is.</param>
/// <param name="HeightMeters">The predicted height above <see cref="DatumName"/>, in metres.</param>
/// <param name="RateMetersPerHour">The rate of rise, in metres per hour.</param>
/// <param name="Trend">Rising, falling or turning.</param>
/// <param name="DatumName">The datum the height is on, such as <c>MLLW</c>.</param>
/// <param name="Time">The view time the state is for (UTC).</param>
internal sealed record TideStationState(
    string StationId,
    string Name,
    TideStationKind Kind,
    GeoPosition Position,
    double HeightMeters,
    double RateMetersPerHour,
    TideTrend Trend,
    string DatumName,
    DateTime Time);

/// <summary>
/// The <b>Tide stations · predicted</b> overlay (tide predictions design D4–D6):
/// the NOAA tide stations in view, each with its predicted height and
/// rising, falling or turning state at the view time, as an
/// <see cref="IDynamicFeatureSource"/> so it gets a Layer stack row under
/// <i>Other chart overlays</i>.
/// </summary>
/// <remarks>
/// <para>
/// Stations are published only at 1:500,000 and larger, and thinned so no
/// two kept stations are within 40 px: harmonic stations first, then
/// subordinates, each by id, so the choice is stable while panning (D6). A
/// station S-104 covers at the view time isn't drawn: the S-104 gauge or grid
/// is what depths use (D5).
/// </para>
/// <para>
/// Heights are on the station's MLLW, NOAA's chart datum and the only one a
/// subordinate station is predicted on.
/// </para>
/// </remarks>
internal sealed class TideStationSource : IDynamicFeatureSource, IDynamicFeatureDescriber
{
    /// <summary>The source id, and its key in <c>ViewerSettings.DynamicSourceVisibility</c>.</summary>
    public const string SourceId = "tide-stations";

    /// <summary>The features' kind, and the renderer key.</summary>
    public const string FeatureKind = "tides.station.predicted";

    /// <summary>The attribute holding a feature's <see cref="TideStationState"/>.</summary>
    public const string StateAttribute = "tideStation";

    /// <summary>The within-plane priority on <see cref="S98DisplayPlane.OtherChartOverlays"/>: above NDBC's 100.</summary>
    public const int WithinPlanePriority = 110;

    /// <summary>The thinning distance (D6).</summary>
    public const double ThinningPixels = 40;

    /// <summary>How near a high or low the tide counts as turning (D5).</summary>
    public static readonly TimeSpan TurningWindow = TimeSpan.FromMinutes(20);

    private readonly TideStationListService _list;
    private readonly Func<DateTime> _viewTime;
    private readonly Func<GeoPosition, DateTime, bool> _coveredByS104;
    private readonly object _gate = new();
    private readonly Dictionary<string, ITidePredictor?> _predictors = new(StringComparer.Ordinal);
    private TideStationCatalogue? _predictorsFor;
    private IReadOnlyList<DynamicFeature> _snapshot = [];
    private IReadOnlyList<TideStationState> _inView = [];
    private MapViewportSnapshot? _view;
    private bool _isActive;

    /// <summary>Creates the source.</summary>
    /// <param name="list">The station list.</param>
    /// <param name="viewTime">The view time (UTC).</param>
    /// <param name="coveredByS104">Whether a loaded S-104 covers a position at a time.</param>
    public TideStationSource(
        TideStationListService list,
        Func<DateTime> viewTime,
        Func<GeoPosition, DateTime, bool> coveredByS104)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(viewTime);
        ArgumentNullException.ThrowIfNull(coveredByS104);
        _list = list;
        _viewTime = viewTime;
        _coveredByS104 = coveredByS104;
        Metadata = new DynamicSourceMetadata
        {
            DisplayName = Strings.Tides_StationsLayer,
            Description = Strings.Tides_StationsLayer_Description,
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

    /// <summary>
    /// The stations in view at a drawable scale, before thinning and S-104
    /// suppression: the Legend's count.
    /// </summary>
    public IReadOnlyList<TideStationState> StationsInView
    {
        get
        {
            lock (_gate)
            {
                return _inView;
            }
        }
    }

    /// <summary>Whether the overlay is on (its Layer stack row is ticked).</summary>
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

    /// <summary>Raised after the features were recomputed, on the thread that recomputed them.</summary>
    public event EventHandler? Refreshed;

    /// <summary>The station's state carried by a feature, or <c>null</c>.</summary>
    /// <param name="feature">The feature.</param>
    public static TideStationState? GetState(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Attributes.TryGetValue(StateAttribute, out var value) ? value as TideStationState : null;
    }

    /// <summary>The feature id of a station.</summary>
    /// <param name="stationId">The station id.</param>
    public static string FeatureIdFor(string stationId) => "tide:" + stationId;

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

    /// <summary>
    /// A station's predictor on MLLW, or <c>null</c> when the station can't be
    /// predicted on it.
    /// </summary>
    /// <param name="catalogue">The list.</param>
    /// <param name="stationId">The station.</param>
    public ITidePredictor? PredictorFor(TideStationCatalogue catalogue, string stationId)
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
                predictor = catalogue.TryCreatePrediction(stationId, VerticalDatum.MeanLowerLowWater)?.Predictor;
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
        IReadOnlyList<TideStationState> inView = [];
        IReadOnlyList<DynamicFeature> features = [];
        if (active && catalogue is not null && view is not null && IsDrawable(view))
        {
            var time = _viewTime();
            inView = Compute(catalogue, view, time);
            features = Thin(inView, view.MercatorResolution * ThinningPixels)
                .Where(s => !_coveredByS104(s.Position, time))
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
                new(Strings.Tides_Pick_Kind, KindText(state.Kind)),
                new(
                    Strings.Tides_Pick_PredictedHeight,
                    string.Format(culture, Strings.Tides_HeightAboveDatumFormat, state.HeightMeters, state.DatumName)),
                new(Strings.Tides_Pick_Trend, TrendText(state.Trend)),
            ]);
    }

    /// <summary>The mariner's word for a station kind.</summary>
    /// <param name="kind">The kind.</param>
    public static string KindText(TideStationKind kind) =>
        kind == TideStationKind.Harmonic ? Strings.Tides_Kind_Harmonic : Strings.Tides_Kind_Subordinate;

    /// <summary>The mariner's word for a trend.</summary>
    /// <param name="trend">The trend.</param>
    public static string TrendText(TideTrend trend) => trend switch
    {
        TideTrend.Rising => Strings.Tides_Trend_Rising,
        TideTrend.Falling => Strings.Tides_Trend_Falling,
        _ => Strings.Tides_Trend_Turning,
    };

    /// <summary>The trend of a prediction at a time.</summary>
    /// <param name="predictor">The prediction.</param>
    /// <param name="utc">The time.</param>
    public static TideTrend TrendAt(ITidePredictor predictor, DateTime utc)
    {
        ArgumentNullException.ThrowIfNull(predictor);
        if (predictor.FindExtremes(utc - TurningWindow, utc + TurningWindow).Count > 0)
        {
            return TideTrend.Turning;
        }

        return predictor.RateAt(utc) >= 0 ? TideTrend.Rising : TideTrend.Falling;
    }

    /// <summary>
    /// Thins stations so no two kept ones are within <paramref name="distance"/>
    /// Web Mercator metres: harmonic first, then subordinate, each by id (D6).
    /// </summary>
    /// <param name="stations">The stations.</param>
    /// <param name="distance">The distance, in Web Mercator metres.</param>
    /// <returns>The kept stations.</returns>
    public static IReadOnlyList<TideStationState> Thin(IEnumerable<TideStationState> stations, double distance)
    {
        ArgumentNullException.ThrowIfNull(stations);
        var ordered = stations
            .OrderBy(s => s.Kind == TideStationKind.Harmonic ? 0 : 1)
            .ThenBy(s => s.StationId, StringComparer.Ordinal);
        var kept = new List<(TideStationState State, double X, double Y)>();
        var squared = distance * distance;
        foreach (var station in ordered)
        {
            var (x, y) = Mercator(station.Position);
            var clear = true;
            foreach (var other in kept)
            {
                var dx = other.X - x;
                var dy = other.Y - y;
                if ((dx * dx) + (dy * dy) < squared)
                {
                    clear = false;
                    break;
                }
            }

            if (clear)
            {
                kept.Add((station, x, y));
            }
        }

        return kept.Select(k => k.State).ToArray();
    }

    private static bool IsDrawable(MapViewportSnapshot view)
    {
        var latitude = (view.MinLatitude + view.MaxLatitude) / 2;
        return view.MercatorResolution > 0
            && MapScaleFormatter.ResolutionToScaleDenominator(view.MercatorResolution, latitude) is { } scale
            && scale <= TideStationPortrayal.StationMaxScaleDenominator;
    }

    private IReadOnlyList<TideStationState> Compute(TideStationCatalogue catalogue, MapViewportSnapshot view, DateTime time)
    {
        var padLat = (view.MaxLatitude - view.MinLatitude) * 0.1;
        var padLon = (view.MaxLongitude - view.MinLongitude) * 0.1;
        var box = new BoundingBox(
            southLatitude: Math.Max(-90, view.MinLatitude - padLat),
            westLongitude: view.MinLongitude - padLon,
            northLatitude: Math.Min(90, view.MaxLatitude + padLat),
            eastLongitude: view.MaxLongitude + padLon);
        var result = new List<TideStationState>();
        foreach (var match in catalogue.FindWithin(box))
        {
            var station = match.Station;
            if (PredictorFor(catalogue, station.Id) is not { } predictor)
            {
                continue;
            }

            result.Add(new TideStationState(
                station.Id,
                station.Name,
                station.Kind,
                station.Position,
                predictor.HeightAt(time),
                predictor.RateAt(time),
                TrendAt(predictor, time),
                predictor.DatumName,
                time));
        }

        return result;
    }

    private static DynamicFeature ToFeature(TideStationState state) => new()
    {
        Id = FeatureIdFor(state.StationId),
        Kind = FeatureKind,
        GeometryType = GeometryType.Point,
        Coordinates = [state.Position],
        Attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { [StateAttribute] = state },
        LastUpdated = new DateTimeOffset(DateTime.SpecifyKind(state.Time, DateTimeKind.Utc)),
    };

    private static (double X, double Y) Mercator(GeoPosition position)
    {
        const double radius = 6378137.0;
        var x = radius * position.Longitude * Math.PI / 180;
        var lat = Math.Clamp(position.Latitude, -85.05112878, 85.05112878) * Math.PI / 180;
        var y = radius * Math.Log(Math.Tan((Math.PI / 4) + (lat / 2)));
        return (x, y);
    }

    private async Task LoadThenRefreshAsync()
    {
        await _list.EnsureAvailableAsync().ConfigureAwait(false);
        Refresh();
    }
}
