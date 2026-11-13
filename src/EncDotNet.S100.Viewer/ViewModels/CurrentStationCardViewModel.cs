using System.Collections.ObjectModel;
using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Tides;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>One slack or maximum on a current station card.</summary>
/// <param name="Kind">"Slack", "Max flood" or "Max ebb".</param>
/// <param name="When">"Today 14:35".</param>
/// <param name="Speed">"6.2 kn", empty for a slack.</param>
/// <param name="Direction">"100°", empty for a slack.</param>
/// <param name="IsPast">Whether it's the last one before the view time, drawn greyed.</param>
internal sealed record CurrentEventRow(string Kind, string When, string Speed, string Direction, bool IsPast)
{
    /// <summary>The row's opacity: the past one greyed.</summary>
    public double Opacity => IsPast ? 0.45 : 1.0;
}

/// <summary>What a current station card needs besides the station.</summary>
/// <param name="Chart">The station chart context: time format, theme, now-marker.</param>
/// <param name="ViewTime">The view time.</param>
/// <param name="Now">The clock, which "Today" is relative to.</param>
/// <param name="Post">Runs an action on the UI thread.</param>
internal sealed record CurrentCardContext(
    StationChartContext Chart,
    IViewTime ViewTime,
    TimeProvider Now,
    Action<Action> Post);

/// <summary>
/// The Object information card of a predicted current station (tide
/// predictions design Q8b, D16, D22): the current now, the speed 12 hours
/// each side of the view time (flood above the zero line, ebb below), the
/// slacks and maxima, the flood and ebb directions, the current's depth, and
/// that it's for display only.
/// </summary>
internal sealed class CurrentStationCardViewModel : StationTimeSeriesViewModel, Services.DynamicSources.IDynamicPickCard
{
    private readonly CurrentStationEntry _station;
    private readonly CurrentStationEntry? _reference;
    private readonly ICurrentPredictor _predictor;
    private readonly CurrentCardContext _context;
    private readonly SolidColorPaint _curvePaint;
    private readonly SolidColorPaint _zeroPaint;
    private readonly SolidColorPaint _axisLabelsPaint;
    private readonly SolidColorPaint _separatorsPaint;
    private readonly ObservableCollection<DateTimePoint> _points = [];
    private DateTime _time;

    /// <summary>Creates the card.</summary>
    /// <param name="catalogue">The station list.</param>
    /// <param name="station">The station.</param>
    /// <param name="predictor">The station's prediction.</param>
    /// <param name="context">The card's surroundings.</param>
    public CurrentStationCardViewModel(
        TideStationCatalogue catalogue,
        CurrentStationEntry station,
        ICurrentPredictor predictor,
        CurrentCardContext context)
        : base(EmptySnapshot(station), context?.Chart ?? throw new ArgumentNullException(nameof(context)))
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(predictor);
        _station = station;
        _predictor = predictor;
        _context = context;
        _reference = station.ReferenceStationId is { } id ? catalogue.FindCurrentStation(id) : null;

        var theme = CurrentChartTheme;
        _curvePaint = new SolidColorPaint(theme.SeriesSpeed, 2f) { PathEffect = new DashEffect([6, 4]) };
        _zeroPaint = new SolidColorPaint(theme.Separator, 1.2f);
        _axisLabelsPaint = new SolidColorPaint(theme.AxisLabel);
        _separatorsPaint = new SolidColorPaint(theme.Separator);
        SpeedSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Name = Strings.Tides_Card_PredictedSeries,
                Values = _points,
                Stroke = _curvePaint,
                Fill = null,
                GeometrySize = 0,
                GeometryStroke = null,
                GeometryFill = null,
                LineSmoothness = 0,
                XToolTipLabelFormatter = p => FormatTooltipDateTime(new DateTime((long)p.Coordinate.SecondaryValue, DateTimeKind.Utc)),
                YToolTipLabelFormatter = p => Knots(p.Coordinate.PrimaryValue),
            },
        ];
        SpeedAxis = new Axis { LabelsPaint = _axisLabelsPaint, SeparatorsPaint = _separatorsPaint };
        SpeedAxisArray = [SpeedAxis];
        CardSections = [.. Sections, new RectangularSection { Yi = 0, Yj = 0, Xi = double.NaN, Xj = double.NaN, Fill = null, Stroke = _zeroPaint }];

        _context.ViewTime.Changed += OnViewTimeChanged;
        _context.Chart.TimeFormat.TimeFormatChanged += OnTimeFormatChanged;
        Update(_context.ViewTime.Current.UtcDateTime);
    }

    /// <summary>The station's name.</summary>
    public string Title => _station.Name;

    /// <summary>"NOAA current station PUG1701_33 · harmonic · 4.6 m below the surface".</summary>
    public string Subtitle
    {
        get
        {
            var c = CultureInfo.CurrentCulture;
            var kind = _station.Kind == TideStationKind.Harmonic
                ? Strings.Tides_Kind_Harmonic
                : string.Format(c, Strings.Tides_Currents_FromFormat, _reference is { } r ? $"{r.Name} {r.Id}" : _station.ReferenceStationId);
            var depth = _station.DepthMeters is { } d
                ? string.Format(c, Strings.Tides_Currents_DepthFormat, d)
                : string.Empty;
            return string.Format(c, Strings.Tides_Currents_SubtitleFormat, _station.Id, kind, depth).TrimEnd(' ', '·');
        }
    }

    /// <summary>"PREDICTED AT 14:20".</summary>
    public string NowLabel { get; private set; } = string.Empty;

    /// <summary>"Slack", "2.6 kn".</summary>
    public string NowValue { get; private set; } = string.Empty;

    /// <summary>"0.1 kn, turning to flood at 14:35", or "flood · 100°".</summary>
    public string NowDetail { get; private set; } = string.Empty;

    /// <summary>"12 HOURS EACH SIDE OF 14:20".</summary>
    public string CurveTitle { get; private set; } = string.Empty;

    /// <summary>The speed curve: flood above zero, ebb below, in knots, dashed.</summary>
    public ISeries[] SpeedSeries { get; }

    /// <summary>The speed axis.</summary>
    public Axis SpeedAxis { get; }

    /// <summary>Single-element wrapper around <see cref="SpeedAxis"/>.</summary>
    public ICartesianAxis[] SpeedAxisArray { get; }

    /// <summary>The now-marker and the zero line.</summary>
    public IReadOnlyList<RectangularSection> CardSections { get; }

    /// <summary>The last slack or maximum before the view time, then the next five.</summary>
    public ObservableCollection<CurrentEventRow> Events { get; } = [];

    /// <summary>Whether the maxima carry a direction: a rotary station (D22).</summary>
    public bool IsRotary => _station.IsRotary;

    /// <summary>"100°".</summary>
    public string FloodDirection => Degrees(_predictor.FloodDirectionDegrees);

    /// <summary>"280°".</summary>
    public string EbbDirection => Degrees(_predictor.EbbDirectionDegrees);

    /// <summary>"4.6 m below the surface", or "Not given".</summary>
    public string DepthText => _station.DepthMeters is { } d
        ? string.Format(CultureInfo.CurrentCulture, Strings.Tides_Currents_DepthFormat, d)
        : Strings.Tides_Currents_DepthUnknown;

    /// <inheritdoc />
    public IReadOnlyList<GeoPosition>? Outline => null;

    /// <summary>Moves the card to a view time.</summary>
    /// <param name="utc">The view time (UTC).</param>
    public void Update(DateTime utc)
    {
        _time = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        UpdateNowMarker(_time);
        var format = _context.Chart.TimeFormat.Current;
        var c = CultureInfo.CurrentCulture;
        var clock = TimeFormatting.FormatClock(_time, format);
        NowLabel = string.Format(c, Strings.Tides_Card_NowAtFormat, clock);
        CurveTitle = string.Format(c, Strings.Tides_Card_CurveTitleFormat, clock);

        var current = _predictor.CurrentAt(_time);
        var events = _predictor.FindEvents(_time - TimeSpan.FromHours(14), _time + TimeSpan.FromDays(2));
        var slack = events.Where(e => e.IsSlack && Math.Abs((e.Time - _time).TotalMinutes) <= TideStationSource.TurningWindow.TotalMinutes)
            .Select(e => (CurrentEvent?)e)
            .FirstOrDefault();
        if (slack is { } s)
        {
            NowValue = Strings.Tides_Currents_Slack;
            NowDetail = string.Format(
                c,
                Strings.Tides_Currents_TurningFormat,
                current.SpeedKnots,
                s.Kind == CurrentEventKind.SlackBeforeFlood ? Strings.Tides_Currents_Flood : Strings.Tides_Currents_Ebb,
                TimeFormatting.FormatClock(s.Time, format));
        }
        else
        {
            NowValue = Knots(current.SpeedKnots);
            NowDetail = string.Format(
                c,
                Strings.Tides_Currents_FlowFormat,
                current.IsFlood ? Strings.Tides_Currents_Flood : Strings.Tides_Currents_Ebb,
                Degrees(current.DirectionDegrees));
        }

        var start = _time - TideStationCardViewModel.HalfWindow;
        var count = (int)((TideStationCardViewModel.HalfWindow * 2) / TideStationCardViewModel.Step) + 1;
        var velocities = new double[count];
        _predictor.Predict(start, TideStationCardViewModel.Step, velocities);
        _points.Clear();
        for (var i = 0; i < count; i++)
        {
            _points.Add(new DateTimePoint(start + (TideStationCardViewModel.Step * i), velocities[i] / TidalCurrent.MetersPerSecondPerKnot));
        }

        Events.Clear();
        var now = _context.Now.GetUtcNow().UtcDateTime;
        var shown = events.Where(e => e.IsSlack || e.Kind is CurrentEventKind.MaximumFlood or CurrentEventKind.MaximumEbb).ToList();
        var before = shown.LastOrDefault(e => e.Time <= _time);
        if (before != default)
        {
            Events.Add(Row(before, format, now, isPast: true));
        }

        foreach (var e in shown.Where(e => e.Time > _time).Take(5))
        {
            Events.Add(Row(e, format, now, isPast: false));
        }

        OnPropertyChanged(nameof(NowLabel));
        OnPropertyChanged(nameof(NowValue));
        OnPropertyChanged(nameof(NowDetail));
        OnPropertyChanged(nameof(CurveTitle));
    }

    /// <inheritdoc />
    public void Release()
    {
        _context.ViewTime.Changed -= OnViewTimeChanged;
        _context.Chart.TimeFormat.TimeFormatChanged -= OnTimeFormatChanged;
        Dispose();
    }

    /// <inheritdoc />
    protected override void OnChartThemeChanged(ChartTheme theme)
    {
        base.OnChartThemeChanged(theme);
        _curvePaint.Color = theme.SeriesSpeed;
        _zeroPaint.Color = theme.Separator;
        _axisLabelsPaint.Color = theme.AxisLabel;
        _separatorsPaint.Color = theme.Separator;
    }

    private CurrentEventRow Row(CurrentEvent e, TimeFormat format, DateTime now, bool isPast)
    {
        var kind = e.Kind switch
        {
            CurrentEventKind.MaximumFlood => Strings.Tides_Currents_MaxFlood,
            CurrentEventKind.MaximumEbb => Strings.Tides_Currents_MaxEbb,
            _ => Strings.Tides_Currents_Slack,
        };
        var when = TideTimeText.DayAndClock(e.Time, format, now);
        if (e.IsSlack)
        {
            return new CurrentEventRow(kind, when, string.Empty, string.Empty, isPast);
        }

        // A rotary station's maxima point where it's predicted to flow (D22);
        // a reversing one's along its flood or ebb direction.
        var direction = _station.IsRotary
            ? _predictor.CurrentAt(e.Time).DirectionDegrees
            : e.IsFlood ? _predictor.FloodDirectionDegrees : _predictor.EbbDirectionDegrees;
        return new CurrentEventRow(kind, when, Knots(e.SpeedMetersPerSecond / TidalCurrent.MetersPerSecondPerKnot), Degrees(direction), isPast);
    }

    private static string Knots(double knots) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Tides_Currents_KnotsFormat, knots);

    private static string Degrees(double degrees) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Tides_Currents_DegreesFormat, ((degrees % 360) + 360) % 360);

    private static StationTimeSeriesSnapshot EmptySnapshot(CurrentStationEntry station) => new()
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
}
