using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Depth;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Time-series view model for the location depth-assimilation card. Plots the
/// total available water depth over time (base depth + S-104 tide) at a picked
/// location, overlays the mariner safety-depth reference line and, for an S-102
/// base, a ± uncertainty band, and exposes a compact now-readout that tracks
/// the global clock.
/// </summary>
/// <remarks>
/// Orientation follows the design decision: more water is plotted upward
/// (mirroring tide), so the Y axis is a conventional (non-inverted) depth axis.
/// Depth values are rendered in the mariner's selected <see cref="DepthUnit"/>.
/// </remarks>
internal sealed class DepthOverTimeViewModel : StationTimeSeriesViewModel
{
    private const string DepthChannelKey = "depthOverTime";

    private readonly DepthUnit _depthUnit;
    private readonly double _baseDepthMetres;
    private readonly IReadOnlyList<DateTime> _curveTimes;
    private readonly IReadOnlyList<double?> _curveDepthsMetres;

    private readonly SolidColorPaint _depthStrokePaint;
    private readonly SolidColorPaint _uncertaintyStrokePaint;
    private readonly SolidColorPaint _safetyStrokePaint;
    private readonly SolidColorPaint _depthAxisLabelsPaint;
    private readonly SolidColorPaint _depthAxisNamePaint;
    private readonly SolidColorPaint _depthAxisSeparatorsPaint;
    private readonly LiveChartsCore.SkiaSharpView.RectangularSection? _safetySection;

    private string _depthNowText;
    private string _tideNowText;
    private bool _isExpanded = true;

    /// <summary>
    /// Constructs a depth-over-time view model for a picked location.
    /// </summary>
    /// <param name="result">The assimilated depth result for the location.</param>
    /// <param name="locationLabel">Formatted location label shown on the card.</param>
    /// <param name="latitude">Pick latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Pick longitude in decimal degrees (WGS-84).</param>
    /// <param name="depthUnit">The mariner's selected depth display unit.</param>
    /// <param name="safetyDepthMetres">
    /// The mariner safety depth in metres to draw as a reference line, or
    /// <c>null</c> when unset.
    /// </param>
    /// <param name="globalTime">Global time service driving the now-marker.</param>
    /// <param name="timeFormat">Optional time-format provider.</param>
    /// <param name="themeService">Optional chrome-theme service.</param>
    public DepthOverTimeViewModel(
        LocationDepthResult result,
        string locationLabel,
        double latitude,
        double longitude,
        DepthUnit depthUnit,
        double? safetyDepthMetres,
        GlobalTimeService? globalTime,
        ITimeFormatProvider? timeFormat = null,
        IThemeService? themeService = null)
        : base(BuildSnapshot(result, locationLabel, latitude, longitude, depthUnit), globalTime, timeFormat, themeService)
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
        LocationLabel = locationLabel;
        _depthUnit = depthUnit;
        _baseDepthMetres = result.Base.DepthMeters;
        _curveTimes = result.DepthOverTime.Select(p => p.Time).ToList();
        _curveDepthsMetres = result.DepthOverTime.Select(p => p.DepthMeters).ToList();
        _depthNowText = Strings.Pick_Depth_Value_Unavailable;
        _tideNowText = Strings.Pick_Depth_Value_Unavailable;

        _depthStrokePaint = new SolidColorPaint(CurrentChartTheme.SeriesPrimary, 2f);
        _uncertaintyStrokePaint = new SolidColorPaint(CurrentChartTheme.SeriesPrimary.WithAlpha(0x55), 1f);
        _safetyStrokePaint = new SolidColorPaint(CurrentChartTheme.NowMarker, 1.5f);
        _depthAxisLabelsPaint = new SolidColorPaint(CurrentChartTheme.AxisLabel);
        _depthAxisNamePaint = new SolidColorPaint(CurrentChartTheme.AxisName);
        _depthAxisSeparatorsPaint = new SolidColorPaint(CurrentChartTheme.Separator);

        DepthSeries = BuildSeries(result, depthUnit);

        DepthAxis = new Axis
        {
            Name = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Pick_Depth_Axis_Depth,
                DepthFormatting.UnitAbbreviation(depthUnit)),
            LabelsPaint = _depthAxisLabelsPaint,
            NamePaint = _depthAxisNamePaint,
            SeparatorsPaint = _depthAxisSeparatorsPaint,
        };
        DepthAxisArray = new ICartesianAxis[] { DepthAxis };

        var sections = new List<LiveChartsCore.SkiaSharpView.RectangularSection>(Sections);
        if (safetyDepthMetres is { } safety)
        {
            var safetyDisplay = DepthFormatting.ToDisplay(safety, depthUnit);
            _safetySection = new LiveChartsCore.SkiaSharpView.RectangularSection
            {
                Yi = safetyDisplay,
                Yj = safetyDisplay,
                Xi = double.NaN,
                Xj = double.NaN,
                Fill = null,
                Stroke = _safetyStrokePaint,
            };
            sections.Add(_safetySection);
            HasSafetyDepth = true;
        }

        DepthSections = sections;

        RefreshNow(globalTime?.CurrentTime);
    }

    /// <summary>The assimilated depth result backing this view model.</summary>
    public LocationDepthResult Result { get; }

    /// <summary>Formatted location label shown on the card.</summary>
    public string LocationLabel { get; }

    /// <summary>
    /// Series for the chart: the tide-adjusted depth curve, preceded (when an
    /// S-102 uncertainty band applies) by the faint ± boundary lines.
    /// </summary>
    public ISeries[] DepthSeries { get; }

    /// <summary>Y axis for the depth chart (mariner depth unit).</summary>
    public Axis DepthAxis { get; }

    /// <summary>Single-element wrapper around <see cref="DepthAxis"/>.</summary>
    public ICartesianAxis[] DepthAxisArray { get; }

    /// <summary>
    /// Chart sections: the inherited vertical now-marker plus the horizontal
    /// safety-depth reference line when a safety depth is configured.
    /// </summary>
    public IReadOnlyList<LiveChartsCore.SkiaSharpView.RectangularSection> DepthSections { get; }

    /// <summary>Card title.</summary>
    public string Title => Strings.Pick_Depth_Title;

    /// <summary>Human-readable label for the base-depth source.</summary>
    public string BaseSourceLabel => Result.Base.Source switch
    {
        BaseDepthSource.Bathymetry => Strings.Pick_Depth_Source_Bathymetry,
        BaseDepthSource.DredgedArea => Strings.Pick_Depth_Source_DredgedArea,
        BaseDepthSource.DepthArea => Strings.Pick_Depth_Source_DepthArea,
        BaseDepthSource.Sounding => Strings.Pick_Depth_Source_Sounding,
        _ => Strings.Pick_Depth_Source_Sounding,
    };

    /// <summary>
    /// Hover tooltip for the base label: "Source data: &lt;file&gt;" naming the
    /// exact dataset the base depth came from, or the source label when the
    /// file name is unknown.
    /// </summary>
    public string BaseSourceTooltip => string.IsNullOrEmpty(Result.Base.SourceDatasetId)
        ? BaseSourceLabel
        : string.Format(CultureInfo.CurrentCulture, Strings.Pick_Depth_SourceData, Result.Base.SourceDatasetId);

    /// <summary>
    /// Hover tooltip for the tide label: "Source data: &lt;file&gt;" naming the
    /// selected S-104 dataset, or empty when no tide overlaps.
    /// </summary>
    public string TideSourceTooltip => string.IsNullOrEmpty(TideDatasetId)
        ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, Strings.Pick_Depth_SourceData, TideDatasetId);

    /// <summary>The formatted base depth in the mariner's depth unit.</summary>
    public string BaseDepthText => DepthFormatting.Format(_baseDepthMetres, _depthUnit);

    /// <summary>
    /// Whether the card's collapsible detail region (base/tide breakdown, chart
    /// and datum caveat) is expanded. The headline depth readout and source
    /// badge remain visible regardless. Defaults to expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// Headline label for the prominent depth readout: "DEPTH NOW" when a live
    /// tide series drives it, or "DEPTH (STATIC)" when only the tide-independent
    /// base depth is available.
    /// </summary>
    public string DepthNowLabel => HasTide
        ? Strings.Pick_Depth_Label_DepthNow
        : Strings.Pick_Depth_Label_DepthStatic;

    /// <summary>
    /// The value shown in the prominent readout: the tide-adjusted depth at the
    /// current global time when tide data overlaps, otherwise the static base
    /// depth.
    /// </summary>
    public string DisplayDepthText => HasTide ? DepthNowText : BaseDepthText;

    /// <summary><c>true</c> when the base depth came from an S-102 bathymetric surface.</summary>
    public bool IsBathymetrySource => Result.Base.Source == BaseDepthSource.Bathymetry;

    /// <summary><c>true</c> when the base depth came from the nearest S-101 sounding.</summary>
    public bool IsSoundingSource => Result.Base.Source == BaseDepthSource.Sounding;

    /// <summary>
    /// <c>true</c> when the base depth came from a charted S-101 area (a dredged
    /// area or a depth area).
    /// </summary>
    public bool IsChartedAreaSource =>
        Result.Base.Source is BaseDepthSource.DredgedArea or BaseDepthSource.DepthArea;

    /// <summary><c>true</c> when an S-102 uncertainty band is available.</summary>
    public bool HasUncertainty => Result.UncertaintyMeters is not null;

    /// <summary>The formatted ± uncertainty caption, or empty when none.</summary>
    public string UncertaintyText => Result.UncertaintyMeters is { } unc
        ? string.Format(
            CultureInfo.CurrentCulture,
            Strings.Pick_Depth_Uncertainty,
            DepthFormatting.Format(unc, _depthUnit))
        : string.Empty;

    /// <summary><c>true</c> when the base/tide vertical datums are not reconciled.</summary>
    public bool HasDatumNote => Result.DatumsNotReconciled;

    /// <summary>The datum-not-reconciled caption.</summary>
    public string DatumNoteText => Strings.Pick_Depth_DatumNote;

    /// <summary><c>true</c> when an S-104 tide series overlaps the location.</summary>
    public bool HasTide => Result.Tide is not null;

    /// <summary>Identifier of the selected S-104 dataset, or empty when none.</summary>
    public string TideDatasetId => Result.Tide?.DatasetId ?? string.Empty;

    /// <summary>Empty-state caption shown when no tide data overlaps.</summary>
    public string NoTideText => Strings.Pick_Depth_NoTide;

    /// <summary>
    /// The tide-adjusted depth at the current global time, formatted, or a
    /// placeholder when unavailable.
    /// </summary>
    public string DepthNowText
    {
        get => _depthNowText;
        private set
        {
            if (SetProperty(ref _depthNowText, value) && HasTide)
            {
                // The prominent readout mirrors DepthNowText while tide data drives it.
                OnPropertyChanged(nameof(DisplayDepthText));
            }
        }
    }

    /// <summary>
    /// The S-104 tide height at the current global time, formatted, or a
    /// placeholder when unavailable.
    /// </summary>
    public string TideNowText
    {
        get => _tideNowText;
        private set => SetProperty(ref _tideNowText, value);
    }

    /// <summary><c>true</c> when a safety-depth reference line is drawn.</summary>
    public bool HasSafetyDepth { get; }

    /// <inheritdoc />
    protected override void OnChartThemeChanged(ChartTheme theme)
    {
        base.OnChartThemeChanged(theme);
        _depthStrokePaint.Color = theme.SeriesPrimary;
        _uncertaintyStrokePaint.Color = theme.SeriesPrimary.WithAlpha(0x55);
        _safetyStrokePaint.Color = theme.NowMarker;
        _depthAxisLabelsPaint.Color = theme.AxisLabel;
        _depthAxisNamePaint.Color = theme.AxisName;
        _depthAxisSeparatorsPaint.Color = theme.Separator;
    }

    /// <inheritdoc />
    protected override void OnNowMarkerChanged(DateTime time) => RefreshNow(time);

    private void RefreshNow(DateTime? time)
    {
        // Guarded against the base constructor's early invocation, before the
        // subclass curve fields are initialised.
        if (_curveTimes is null || _curveDepthsMetres is null)
        {
            return;
        }

        var depthMetres = time is { } t ? NearestDepth(t) : null;
        if (depthMetres is { } depth)
        {
            DepthNowText = DepthFormatting.Format(depth, _depthUnit);
            TideNowText = DepthFormatting.Format(depth - _baseDepthMetres, _depthUnit);
        }
        else
        {
            DepthNowText = Strings.Pick_Depth_Value_Unavailable;
            TideNowText = Strings.Pick_Depth_Value_Unavailable;
        }
    }

    private double? NearestDepth(DateTime time)
    {
        if (_curveTimes.Count == 0)
        {
            return null;
        }

        var bestIndex = 0;
        var bestDelta = (_curveTimes[0] - time).Duration();
        for (var i = 1; i < _curveTimes.Count; i++)
        {
            var delta = (_curveTimes[i] - time).Duration();
            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestIndex = i;
            }
        }

        return _curveDepthsMetres[bestIndex];
    }

    private ISeries[] BuildSeries(LocationDepthResult result, DepthUnit depthUnit)
    {
        var series = new List<ISeries>(3);

        if (result.UncertaintyMeters is { } unc)
        {
            series.Add(BoundarySeries(result, depthUnit, unc));
            series.Add(BoundarySeries(result, depthUnit, -unc));
        }

        var points = new List<DateTimePoint>(result.DepthOverTime.Count);
        foreach (var point in result.DepthOverTime)
        {
            if (point.DepthMeters is { } depth)
            {
                points.Add(new DateTimePoint(point.Time, DepthFormatting.ToDisplay(depth, depthUnit)));
            }
        }

        series.Add(new LineSeries<DateTimePoint>
        {
            Name = Strings.Pick_Depth_Series_TideAdjusted,
            Values = points,
            Stroke = _depthStrokePaint,
            Fill = null,
            GeometrySize = 0,
            GeometryStroke = null,
            GeometryFill = null,
            XToolTipLabelFormatter = p => FormatTooltipDateTime(
                new DateTime((long)p.Coordinate.SecondaryValue, DateTimeKind.Utc)),
            YToolTipLabelFormatter = p => p.Coordinate.PrimaryValue.ToString("0.##", CultureInfo.InvariantCulture)
                + " " + DepthFormatting.UnitAbbreviation(depthUnit),
        });

        return series.ToArray();
    }

    private LineSeries<DateTimePoint> BoundarySeries(LocationDepthResult result, DepthUnit depthUnit, double offsetMetres)
    {
        var points = new List<DateTimePoint>(result.DepthOverTime.Count);
        foreach (var point in result.DepthOverTime)
        {
            if (point.DepthMeters is { } depth)
            {
                points.Add(new DateTimePoint(point.Time, DepthFormatting.ToDisplay(depth + offsetMetres, depthUnit)));
            }
        }

        return new LineSeries<DateTimePoint>
        {
            Values = points,
            Stroke = _uncertaintyStrokePaint,
            Fill = null,
            GeometrySize = 0,
            GeometryStroke = null,
            GeometryFill = null,
            IsHoverable = false,
        };
    }

    private static StationTimeSeriesSnapshot BuildSnapshot(
        LocationDepthResult result,
        string locationLabel,
        double latitude,
        double longitude,
        DepthUnit depthUnit)
    {
        ArgumentNullException.ThrowIfNull(result);

        var times = result.DepthOverTime.Select(p => p.Time).ToList();
        var values = result.DepthOverTime
            .Select(p => p.DepthMeters is { } d ? (float)DepthFormatting.ToDisplay(d, depthUnit) : float.NaN)
            .ToList();

        return new StationTimeSeriesSnapshot
        {
            StationId = locationLabel,
            Latitude = latitude,
            Longitude = longitude,
            Times = times,
            Channels =
            [
                new StationTimeSeriesChannel
                {
                    Key = DepthChannelKey,
                    DisplayName = Strings.Pick_Depth_Series_TideAdjusted,
                    Unit = DepthFormatting.UnitAbbreviation(depthUnit),
                    Values = values,
                },
            ],
        };
    }
}
