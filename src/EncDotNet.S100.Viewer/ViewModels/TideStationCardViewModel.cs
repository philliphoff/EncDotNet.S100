using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.Services.Tides;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>One high or low on a tide station card.</summary>
/// <param name="Kind">"High" or "Low".</param>
/// <param name="When">"Today 16:20".</param>
/// <param name="Height">"−0.2 m".</param>
/// <param name="IsPast">Whether it's the last one before the view time, drawn greyed.</param>
internal sealed record TideExtremeRow(string Kind, string When, string Height, bool IsPast)
{
    /// <summary>The row's opacity: the past one greyed.</summary>
    public double Opacity => IsPast ? 0.45 : 1.0;
}

/// <summary>What a tide station card needs besides the station.</summary>
/// <param name="Chart">The station chart context: time format, theme, now-marker.</param>
/// <param name="ViewTime">The view time.</param>
/// <param name="Now">The clock, which "Today" is relative to.</param>
/// <param name="WaterLevel">The loaded water level, for the S-104 that covers a station.</param>
/// <param name="UsePredictions">Whether tide predictions are a water-level source (D1).</param>
/// <param name="TurnOnPredictions">Turns them on, from the card's "Not used for depths" row.</param>
/// <param name="Post">Runs an action on the UI thread.</param>
internal sealed record TideCardContext(
    StationChartContext Chart,
    IViewTime ViewTime,
    TimeProvider Now,
    IWaterLevelCatalog? WaterLevel,
    Func<bool> UsePredictions,
    Action TurnOnPredictions,
    Action<Action> Post);

/// <summary>
/// The Object information card of a predicted tide station (tide predictions
/// design Q3, D8): the predicted height now, the S-104 fixed-station chart
/// dashed over 12 hours each side of the view time, the last high or low and
/// the next four, the reference station of a subordinate station, the datum,
/// whether depths use it, and the fixed "not a forecast" note.
/// </summary>
internal sealed class TideStationCardViewModel : StationTimeSeriesViewModel, Services.DynamicSources.IDynamicPickCard
{
    /// <summary>How far each side of the view time the curve runs (D8).</summary>
    public static readonly TimeSpan HalfWindow = TimeSpan.FromHours(12);

    /// <summary>The curve's step.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(10);

    private readonly TideStationEntry _station;
    private readonly TideStationEntry? _reference;
    private readonly TideStationCatalogue _catalogue;
    private readonly ITidePredictor _predictor;
    private readonly TideCardContext _context;
    private readonly SolidColorPaint _predictedPaint;
    private readonly SolidColorPaint _s104Paint;
    private readonly SolidColorPaint _datumPaint;
    private readonly SolidColorPaint _axisLabelsPaint;
    private readonly SolidColorPaint _axisNamePaint;
    private readonly SolidColorPaint _separatorsPaint;
    private readonly ObservableCollection<DateTimePoint> _predicted = [];
    private readonly ObservableCollection<DateTimePoint> _s104 = [];
    private readonly IWaterLevelSeries? _s104Series;
    private DateTime _time;

    /// <summary>Creates the card.</summary>
    /// <param name="catalogue">The station list.</param>
    /// <param name="station">The station.</param>
    /// <param name="predictor">The station's prediction on MLLW.</param>
    /// <param name="context">The card's surroundings.</param>
    public TideStationCardViewModel(
        TideStationCatalogue catalogue,
        TideStationEntry station,
        ITidePredictor predictor,
        TideCardContext context)
        : base(EmptySnapshot(station), context?.Chart ?? throw new ArgumentNullException(nameof(context)))
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(predictor);
        _catalogue = catalogue;
        _station = station;
        _predictor = predictor;
        _context = context;
        _reference = station.ReferenceStationId is { } referenceId ? catalogue.Find(referenceId) : null;

        var theme = CurrentChartTheme;
        _predictedPaint = new SolidColorPaint(theme.SeriesPrimary, 2f) { PathEffect = new DashEffect([6, 4]) };
        _s104Paint = new SolidColorPaint(theme.AxisName, 1.6f);
        _datumPaint = new SolidColorPaint(theme.Separator, 1f) { PathEffect = new DashEffect([3, 3]) };
        _axisLabelsPaint = new SolidColorPaint(theme.AxisLabel);
        _axisNamePaint = new SolidColorPaint(theme.AxisName);
        _separatorsPaint = new SolidColorPaint(theme.Separator);

        var position = station.Position;
        _s104Series = context.WaterLevel?.WaterLevel?.Locate(position).FirstOrDefault(s => !s.Source.IsPredicted);

        var series = new List<ISeries>(2);
        if (_s104Series is not null)
        {
            series.Add(Line(_s104, Strings.Tides_Card_S104Series, _s104Paint));
        }

        series.Add(Line(_predicted, Strings.Tides_Card_PredictedSeries, _predictedPaint));
        HeightSeries = [.. series];
        HeightAxis = new Axis
        {
            // The caption above the chart names the datum; no axis name.
            LabelsPaint = _axisLabelsPaint,
            NamePaint = _axisNamePaint,
            SeparatorsPaint = _separatorsPaint,
        };
        HeightAxisArray = [HeightAxis];

        var sections = new List<RectangularSection>(Sections)
        {
            new() { Yi = 0, Yj = 0, Xi = double.NaN, Xj = double.NaN, Fill = null, Stroke = _datumPaint },
        };
        if (MhhwMeters is { } mhhw)
        {
            sections.Add(new RectangularSection { Yi = mhhw, Yj = mhhw, Xi = double.NaN, Xj = double.NaN, Fill = null, Stroke = _datumPaint });
        }

        CardSections = sections;
        UseTidePredictionsCommand = new RelayCommand(() =>
        {
            _context.TurnOnPredictions();
            RaiseAll();
        });

        _context.ViewTime.Changed += OnViewTimeChanged;
        _context.Chart.TimeFormat.TimeFormatChanged += OnTimeFormatChanged;
        Update(_context.ViewTime.Current.UtcDateTime);
    }

    /// <summary>The station's name.</summary>
    public string Title => _station.Name;

    /// <inheritdoc />
    /// <remarks>The station's cell, outlined while it's picked (D13).</remarks>
    public IReadOnlyList<GeoPosition>? Outline => _outline ??= TideStationCell.Outline(
        _catalogue,
        _station,
        id => _catalogue.TryCreatePrediction(id, VerticalDatum.MeanLowerLowWater) is not null);

    private IReadOnlyList<GeoPosition>? _outline;

    /// <summary>The station id.</summary>
    public string NoaaId => _station.Id;

    /// <summary>"NOAA tide station 9447130 · harmonic · 47.6026° N, 122.3393° W" or "… · subordinate · from Seattle 9447130".</summary>
    public string Subtitle => _station.Kind == TideStationKind.Harmonic
        ? string.Format(
            CultureInfo.CurrentCulture,
            Strings.Tides_Card_SubtitleHarmonicFormat,
            _station.Id,
            LatLonFormatter.FormatDecimal(_station.Position.Latitude, _station.Position.Longitude))
        : string.Format(
            CultureInfo.CurrentCulture,
            Strings.Tides_Card_SubtitleSubordinateFormat,
            _station.Id,
            ReferenceLabel);

    /// <summary>"PREDICTED HEIGHT AT 14:20".</summary>
    public string NowLabel { get; private set; } = string.Empty;

    /// <summary>"0.4 m".</summary>
    public string NowHeight { get; private set; } = string.Empty;

    /// <summary>"falling 0.6 m/h", or "turning".</summary>
    public string NowTrend { get; private set; } = string.Empty;

    /// <summary>The trend at the view time.</summary>
    public TideTrend Trend { get; private set; }

    /// <summary>"12 HOURS EACH SIDE OF 14:20".</summary>
    public string CurveTitle { get; private set; } = string.Empty;

    /// <summary>"heights above MLLW".</summary>
    public string CurveDatum => string.Format(CultureInfo.CurrentCulture, Strings.Tides_Card_CurveDatumFormat, _predictor.DatumName);

    /// <summary>The predicted curve (dashed), and S-104's when it covers the station.</summary>
    public ISeries[] HeightSeries { get; }

    /// <summary>The height axis.</summary>
    public Axis HeightAxis { get; }

    /// <summary>Single-element wrapper around <see cref="HeightAxis"/>.</summary>
    public ICartesianAxis[] HeightAxisArray { get; }

    /// <summary>The now-marker, the datum line and MHHW.</summary>
    public IReadOnlyList<RectangularSection> CardSections { get; }

    /// <summary>The last high or low before the view time, then the next four.</summary>
    public ObservableCollection<TideExtremeRow> Extremes { get; } = [];

    /// <summary>Whether the station is subordinate, so the card has a Reference station block.</summary>
    public bool HasReference => _station.Kind == TideStationKind.Subordinate && _station.Offsets is not null;

    /// <summary>"Seattle 9447130".</summary>
    public string ReferenceLabel => _reference is { } r
        ? $"{r.Name} {r.Id}"
        : _station.ReferenceStationId ?? string.Empty;

    /// <summary>"Seattle 9447130 · 6.1 km".</summary>
    public string ReferenceStationText => _reference is { } r
        ? string.Format(
            CultureInfo.CurrentCulture,
            Strings.Tides_Card_ReferenceDistanceFormat,
            ReferenceLabel,
            Wgs84Geodesy.GeodesicDistanceMetres(_station.Position, r.Position) / 1000)
        : ReferenceLabel;

    /// <summary>"+0 h 12 m, height ×1.02".</summary>
    public string HighWaterOffset => _station.Offsets is { } o ? OffsetText(o.HighWaterTime, o.HighWaterHeight, o.HeightAdjustment) : string.Empty;

    /// <summary>"+0 h 25 m, height ×0.98".</summary>
    public string LowWaterOffset => _station.Offsets is { } o ? OffsetText(o.LowWaterTime, o.LowWaterHeight, o.HeightAdjustment) : string.Empty;

    /// <summary>The note on how a subordinate station's curve is made (D20).</summary>
    public string ReferenceNote => string.Format(CultureInfo.CurrentCulture, Strings.Tides_Card_ReferenceNoteFormat, _reference?.Name ?? ReferenceLabel);

    /// <summary>"Mean Lower Low Water (MLLW), the station's chart datum".</summary>
    public string DatumText => string.Equals(_predictor.DatumName, TideDatumNames.MeanLowerLowWater, StringComparison.OrdinalIgnoreCase)
        ? Strings.Tides_Card_DatumMllw
        : _predictor.DatumName;

    /// <summary>MHHW above MLLW, in metres, when the station gives both.</summary>
    public double? MhhwMeters =>
        _station.Datums?.Heights is { } h
        && h.TryGetValue(TideDatumNames.MeanHigherHighWater, out var mhhw)
        && h.TryGetValue(TideDatumNames.MeanLowerLowWater, out var mllw)
            ? mhhw - mllw
            : null;

    /// <summary>Whether <see cref="MhhwText"/> is known.</summary>
    public bool HasMhhw => MhhwMeters is not null;

    /// <summary>"3.44 m above MLLW".</summary>
    public string MhhwText => MhhwMeters is { } m
        ? string.Format(CultureInfo.CurrentCulture, Strings.Tides_Card_MhhwFormat, m, _predictor.DatumName)
        : string.Empty;

    /// <summary>Whether S-104 covers the station at the view time (D8).</summary>
    public bool IsCoveredByS104 => _s104Series is { } s && s.Start <= _time && _time <= s.End;

    /// <summary>Whether depths use this prediction here.</summary>
    public bool IsUsedForDepths => _context.UsePredictions() && !IsCoveredByS104;

    /// <summary>Whether the card offers <b>Use tide predictions</b>: predictions are off.</summary>
    public bool CanTurnOn => !_context.UsePredictions();

    /// <summary>"Yes, within 10 km, where there's no S-104", or why not.</summary>
    public string UsedForDepthsText => IsUsedForDepths
        ? Strings.Tides_Card_UsedYes
        : IsCoveredByS104 ? Strings.Tides_Card_UsedNoS104 : Strings.Tides_Card_UsedNoOff;

    /// <summary>"Used for depths here: S-104" box text.</summary>
    public string S104Text => _s104Series is { } s
        ? string.Format(
            CultureInfo.CurrentCulture,
            Strings.Tides_Card_S104CoversFormat,
            s.Source.DatasetId,
            TimeFormatting.FormatClock(_time, _context.Chart.TimeFormat.Current))
        : string.Empty;

    /// <summary>"0.5 m · forecast" at the view time, when S-104 covers.</summary>
    public string S104AtTime { get; private set; } = string.Empty;

    /// <summary>"0.2 m · astronomical only".</summary>
    public string PredictedAtTime { get; private set; } = string.Empty;

    /// <summary>"+0.3 m (wind, pressure, river flow)": S-104 minus the prediction, a check only.</summary>
    public string Difference { get; private set; } = string.Empty;

    /// <summary>"NOAA CO-OPS, 3 Mar 2026".</summary>
    public string StationListText => _catalogue.Retrieved is { } retrieved
        ? string.Format(
            CultureInfo.CurrentCulture,
            Strings.Tides_Card_StationListFormat,
            _catalogue.Provider ?? Strings.Tides_ProviderUnknown,
            TimeFormatting.FormatDateOnly(DateOnly.FromDateTime(retrieved.UtcDateTime)))
        : _catalogue.Provider ?? Strings.Tides_ProviderUnknown;

    /// <summary>Turns tide predictions on as a water-level source.</summary>
    public IRelayCommand UseTidePredictionsCommand { get; }

    /// <summary>Moves the card to a view time.</summary>
    /// <param name="utc">The view time (UTC).</param>
    public void Update(DateTime utc)
    {
        _time = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        UpdateNowMarker(_time);
        var format = _context.Chart.TimeFormat.Current;
        var clock = TimeFormatting.FormatClock(_time, format);
        var height = _predictor.HeightAt(_time);
        var rate = _predictor.RateAt(_time);
        Trend = TideStationSource.TrendAt(_predictor, _time);
        NowLabel = string.Format(CultureInfo.CurrentCulture, Strings.Tides_Card_NowFormat, clock);
        NowHeight = Metres(height);
        NowTrend = Trend == TideTrend.Turning
            ? Strings.Tides_Trend_Turning
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.Tides_Card_TrendRateFormat,
                TideStationSource.TrendText(Trend),
                Math.Abs(rate));
        CurveTitle = string.Format(CultureInfo.CurrentCulture, Strings.Tides_Card_CurveTitleFormat, clock);

        var start = _time - HalfWindow;
        var count = (int)((HalfWindow + HalfWindow) / Step) + 1;
        var heights = new double[count];
        _predictor.Predict(start, Step, heights);
        _predicted.Clear();
        for (var i = 0; i < count; i++)
        {
            _predicted.Add(new DateTimePoint(start + (Step * i), heights[i]));
        }

        _s104.Clear();
        if (_s104Series is { } s104)
        {
            for (var i = 0; i < count; i++)
            {
                var t = start + (Step * i);
                if (t >= s104.Start && t <= s104.End && s104.ValueAt(t) is { } reading)
                {
                    _s104.Add(new DateTimePoint(t, reading.HeightMeters));
                }
            }

            if (IsCoveredByS104 && s104.ValueAt(_time) is { } atTime)
            {
                S104AtTime = string.Format(CultureInfo.CurrentCulture, Strings.Tides_Card_S104AtFormat, Metres(atTime.HeightMeters));
                PredictedAtTime = string.Format(CultureInfo.CurrentCulture, Strings.Tides_Card_PredictedAtFormat, Metres(height));
                Difference = string.Format(CultureInfo.CurrentCulture, Strings.Tides_Card_DifferenceFormat, SignedMetres(atTime.HeightMeters - height));
            }
        }

        Extremes.Clear();
        var now = _context.Now.GetUtcNow().UtcDateTime;
        var found = _predictor.FindExtremes(_time - TimeSpan.FromHours(14), _time + TimeSpan.FromDays(3));
        var before = found.LastOrDefault(e => e.Time <= _time);
        if (before != default)
        {
            Extremes.Add(Row(before, format, now, isPast: true));
        }

        foreach (var extreme in found.Where(e => e.Time > _time).Take(4))
        {
            Extremes.Add(Row(extreme, format, now, isPast: false));
        }

        RaiseAll();
    }

    /// <inheritdoc />
    protected override void OnChartThemeChanged(ChartTheme theme)
    {
        base.OnChartThemeChanged(theme);
        _predictedPaint.Color = theme.SeriesPrimary;
        _s104Paint.Color = theme.AxisName;
        _datumPaint.Color = theme.Separator;
        _axisLabelsPaint.Color = theme.AxisLabel;
        _axisNamePaint.Color = theme.AxisName;
        _separatorsPaint.Color = theme.Separator;
    }

    /// <summary>Stops following the view time.</summary>
    public void Release()
    {
        _context.ViewTime.Changed -= OnViewTimeChanged;
        _context.Chart.TimeFormat.TimeFormatChanged -= OnTimeFormatChanged;
        Dispose();
    }

    private static string Metres(double value) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Tides_MetresFormat, value);

    private static string SignedMetres(double value) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Tides_SignedMetresFormat, value);

    private static TideExtremeRow Row(TideExtreme extreme, TimeFormat format, DateTime now, bool isPast) => new(
        extreme.Kind == TideExtremeKind.High ? Strings.Tides_High : Strings.Tides_Low,
        TideTimeText.DayAndClock(extreme.Time, format, now),
        Metres(extreme.HeightMeters),
        isPast);

    private static string OffsetText(TimeSpan time, double height, SubordinateHeightAdjustment adjustment)
    {
        var sign = time < TimeSpan.Zero ? "−" : "+";
        var abs = time.Duration();
        var heightText = adjustment == SubordinateHeightAdjustment.Ratio
            ? string.Format(CultureInfo.CurrentCulture, Strings.Tides_Card_HeightRatioFormat, height)
            : string.Format(CultureInfo.CurrentCulture, Strings.Tides_Card_HeightDifferenceFormat, height);
        return string.Format(
            CultureInfo.CurrentCulture,
            Strings.Tides_Card_OffsetFormat,
            sign,
            (int)abs.TotalHours,
            abs.Minutes,
            heightText);
    }

    private LineSeries<DateTimePoint> Line(ObservableCollection<DateTimePoint> values, string name, SolidColorPaint stroke) => new()
    {
        Name = name,
        Values = values,
        Stroke = stroke,
        Fill = null,
        GeometrySize = 0,
        GeometryStroke = null,
        GeometryFill = null,
        LineSmoothness = 0,
        XToolTipLabelFormatter = p => FormatTooltipDateTime(new DateTime((long)p.Coordinate.SecondaryValue, DateTimeKind.Utc)),
        YToolTipLabelFormatter = p => Metres(p.Coordinate.PrimaryValue),
    };

    private static StationTimeSeriesSnapshot EmptySnapshot(TideStationEntry station) => new()
    {
        StationId = station?.Id ?? throw new ArgumentNullException(nameof(station)),
        StationName = station.Name,
        Latitude = station.Position.Latitude,
        Longitude = station.Position.Longitude,
        Times = [],
        Channels = [],
    };

    private void OnViewTimeChanged(object? sender, EventArgs e) =>
        _context.Post(() =>
        {
            if (!IsDisposed)
            {
                Update(_context.ViewTime.Current.UtcDateTime);
            }
        });

    private void OnTimeFormatChanged(TimeFormat format) =>
        _context.Post(() =>
        {
            if (!IsDisposed)
            {
                Update(_time);
            }
        });

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(NowLabel));
        OnPropertyChanged(nameof(NowHeight));
        OnPropertyChanged(nameof(NowTrend));
        OnPropertyChanged(nameof(Trend));
        OnPropertyChanged(nameof(CurveTitle));
        OnPropertyChanged(nameof(IsCoveredByS104));
        OnPropertyChanged(nameof(IsUsedForDepths));
        OnPropertyChanged(nameof(CanTurnOn));
        OnPropertyChanged(nameof(UsedForDepthsText));
        OnPropertyChanged(nameof(S104Text));
        OnPropertyChanged(nameof(S104AtTime));
        OnPropertyChanged(nameof(PredictedAtTime));
        OnPropertyChanged(nameof(Difference));
    }
}
