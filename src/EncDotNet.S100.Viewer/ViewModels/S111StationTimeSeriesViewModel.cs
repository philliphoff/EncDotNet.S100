using System.Collections.Generic;
using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Station time-series view model for an S-111 surface-current station.
/// Exposes two independently-bound series collections (<see cref="SpeedSeries"/>
/// and <see cref="DirectionSeries"/>) so the panel can stack two charts that
/// share the inherited time axis but have distinct Y axes.
/// </summary>
internal sealed class S111StationTimeSeriesViewModel : StationTimeSeriesViewModel
{
    private readonly SolidColorPaint _speedStrokePaint;
    private readonly SolidColorPaint _directionStrokePaint;
    private readonly SolidColorPaint _speedAxisLabelsPaint;
    private readonly SolidColorPaint _speedAxisNamePaint;
    private readonly SolidColorPaint _speedAxisSeparatorsPaint;
    private readonly SolidColorPaint _directionAxisLabelsPaint;
    private readonly SolidColorPaint _directionAxisNamePaint;
    private readonly SolidColorPaint _directionAxisSeparatorsPaint;

    public S111StationTimeSeriesViewModel(
        StationTimeSeriesSnapshot snapshot,
        GlobalTimeService? globalTime,
        ITimeFormatProvider? timeFormat = null,
        IThemeService? themeService = null)
        : base(snapshot, globalTime, timeFormat, themeService)
    {
        var speedChannel = FindChannel(snapshot, "surfaceCurrentSpeed");
        var directionChannel = FindChannel(snapshot, "surfaceCurrentDirection");

        var speedPoints = speedChannel is null
            ? new List<DateTimePoint>()
            : ProjectChannel(snapshot.Times, speedChannel);
        var directionPoints = directionChannel is null
            ? new List<DateTimePoint>()
            : ProjectChannel(snapshot.Times, directionChannel);

        _speedStrokePaint = new SolidColorPaint(CurrentChartTheme.SeriesSpeed, 2f);
        _directionStrokePaint = new SolidColorPaint(CurrentChartTheme.SeriesDirection, 2f);
        _speedAxisLabelsPaint = new SolidColorPaint(CurrentChartTheme.AxisLabel);
        _speedAxisNamePaint = new SolidColorPaint(CurrentChartTheme.AxisName);
        _speedAxisSeparatorsPaint = new SolidColorPaint(CurrentChartTheme.Separator);
        _directionAxisLabelsPaint = new SolidColorPaint(CurrentChartTheme.AxisLabel);
        _directionAxisNamePaint = new SolidColorPaint(CurrentChartTheme.AxisName);
        _directionAxisSeparatorsPaint = new SolidColorPaint(CurrentChartTheme.Separator);

        SpeedSeries = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Name = Strings.Pick_Chart_Title_SurfaceCurrentSpeed,
                Values = speedPoints,
                Stroke = _speedStrokePaint,
                Fill = null,
                GeometrySize = 0,
                GeometryStroke = null,
                GeometryFill = null,
                XToolTipLabelFormatter = p => FormatTooltipDateTime(
                    new System.DateTime((long)p.Coordinate.SecondaryValue, System.DateTimeKind.Utc)),
                YToolTipLabelFormatter = p => p.Coordinate.PrimaryValue.ToString("0.##", CultureInfo.InvariantCulture) + " m/s",
            },
        };

        DirectionSeries = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Name = Strings.Pick_Chart_Title_SurfaceCurrentDirection,
                Values = directionPoints,
                Stroke = _directionStrokePaint,
                Fill = null,
                GeometrySize = 0,
                GeometryStroke = null,
                GeometryFill = null,
                XToolTipLabelFormatter = p => FormatTooltipDateTime(
                    new System.DateTime((long)p.Coordinate.SecondaryValue, System.DateTimeKind.Utc)),
                YToolTipLabelFormatter = p => p.Coordinate.PrimaryValue.ToString("0.#", CultureInfo.InvariantCulture) + "°",
            },
        };

        SpeedAxis = new Axis
        {
            Name = Strings.Pick_Chart_Axis_SpeedMetresPerSecond,
            LabelsPaint = _speedAxisLabelsPaint,
            NamePaint = _speedAxisNamePaint,
            SeparatorsPaint = _speedAxisSeparatorsPaint,
        };
        DirectionAxis = new Axis
        {
            Name = Strings.Pick_Chart_Axis_DirectionDegrees,
            MinLimit = 0,
            MaxLimit = 360,
            LabelsPaint = _directionAxisLabelsPaint,
            NamePaint = _directionAxisNamePaint,
            SeparatorsPaint = _directionAxisSeparatorsPaint,
        };
        SpeedAxisArray = new ICartesianAxis[] { SpeedAxis };
        DirectionAxisArray = new ICartesianAxis[] { DirectionAxis };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Mutates the speed/direction series strokes and the two Y-axis
    /// paint sets in place; LiveCharts2 redraws on the next paint cycle.
    /// </remarks>
    protected override void OnChartThemeChanged(ChartTheme theme)
    {
        base.OnChartThemeChanged(theme);
        _speedStrokePaint.Color = theme.SeriesSpeed;
        _directionStrokePaint.Color = theme.SeriesDirection;
        _speedAxisLabelsPaint.Color = theme.AxisLabel;
        _speedAxisNamePaint.Color = theme.AxisName;
        _speedAxisSeparatorsPaint.Color = theme.Separator;
        _directionAxisLabelsPaint.Color = theme.AxisLabel;
        _directionAxisNamePaint.Color = theme.AxisName;
        _directionAxisSeparatorsPaint.Color = theme.Separator;
    }

    /// <summary>Top chart series: surface-current speed.</summary>
    public ISeries[] SpeedSeries { get; }

    /// <summary>Bottom chart series: surface-current direction.</summary>
    public ISeries[] DirectionSeries { get; }

    /// <summary>Y axis for the speed chart (m/s).</summary>
    public Axis SpeedAxis { get; }

    /// <summary>Y axis for the direction chart (degrees, clamped 0–360).</summary>
    public Axis DirectionAxis { get; }

    /// <summary>Single-element wrapper around <see cref="SpeedAxis"/>.</summary>
    public ICartesianAxis[] SpeedAxisArray { get; }

    /// <summary>Single-element wrapper around <see cref="DirectionAxis"/>.</summary>
    public ICartesianAxis[] DirectionAxisArray { get; }

    /// <summary>Title for the speed chart.</summary>
    public string SpeedTitle => Strings.Pick_Chart_Title_SurfaceCurrentSpeed;

    /// <summary>Title for the direction chart.</summary>
    public string DirectionTitle => Strings.Pick_Chart_Title_SurfaceCurrentDirection;

    private static StationTimeSeriesChannel? FindChannel(StationTimeSeriesSnapshot snapshot, string key)
    {
        foreach (var c in snapshot.Channels)
        {
            if (string.Equals(c.Key, key, System.StringComparison.Ordinal))
                return c;
        }
        return null;
    }
}
