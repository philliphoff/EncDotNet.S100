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
/// Station time-series view model for an S-104 water-level station. Exposes
/// a single height-vs-time line series via <see cref="HeightSeries"/>.
/// </summary>
internal sealed class S104StationTimeSeriesViewModel : StationTimeSeriesViewModel
{
    private readonly SolidColorPaint _heightStrokePaint;
    private readonly SolidColorPaint _heightAxisLabelsPaint;
    private readonly SolidColorPaint _heightAxisNamePaint;
    private readonly SolidColorPaint _heightAxisSeparatorsPaint;

    public S104StationTimeSeriesViewModel(
        StationTimeSeriesSnapshot snapshot,
        GlobalTimeService? globalTime,
        ITimeFormatProvider? timeFormat = null,
        IThemeService? themeService = null)
        : base(snapshot, globalTime, timeFormat, themeService)
    {
        var channel = FindChannel(snapshot, "waterLevelHeight");
        var points = channel is null ? new List<DateTimePoint>() : ProjectChannel(snapshot.Times, channel);

        _heightStrokePaint = new SolidColorPaint(CurrentChartTheme.SeriesPrimary, 2f);
        _heightAxisLabelsPaint = new SolidColorPaint(CurrentChartTheme.AxisLabel);
        _heightAxisNamePaint = new SolidColorPaint(CurrentChartTheme.AxisName);
        _heightAxisSeparatorsPaint = new SolidColorPaint(CurrentChartTheme.Separator);

        HeightSeries = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Name = Strings.Pick_Chart_Title_WaterLevel,
                Values = points,
                Stroke = _heightStrokePaint,
                Fill = null,
                GeometrySize = 0,
                GeometryStroke = null,
                GeometryFill = null,
                // Override LiveCharts2's default tooltip (which prints the
                // raw UTC DateTime regardless of mariner time-zone setting)
                // with one that routes through FormatTooltipDateTime.
                XToolTipLabelFormatter = p => FormatTooltipDateTime(
                    new System.DateTime((long)p.Coordinate.SecondaryValue, System.DateTimeKind.Utc)),
                YToolTipLabelFormatter = p => p.Coordinate.PrimaryValue.ToString("0.##", CultureInfo.InvariantCulture) + " m",
            },
        };

        HeightAxis = new Axis
        {
            Name = Strings.Pick_Chart_Axis_HeightMetres,
            LabelsPaint = _heightAxisLabelsPaint,
            NamePaint = _heightAxisNamePaint,
            SeparatorsPaint = _heightAxisSeparatorsPaint,
        };
        HeightAxisArray = new ICartesianAxis[] { HeightAxis };
    }

    /// <summary>Single height-vs-time series for the S-104 chart.</summary>
    public ISeries[] HeightSeries { get; }

    /// <summary>Y axis for the height chart (metres).</summary>
    public Axis HeightAxis { get; }

    /// <summary>Single-element wrapper around <see cref="HeightAxis"/>.</summary>
    public ICartesianAxis[] HeightAxisArray { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Mutates the height-series stroke and Y-axis paints in place so
    /// LiveCharts2 redraws on the next paint cycle without rebuilding
    /// the series.
    /// </remarks>
    protected override void OnChartThemeChanged(ChartTheme theme)
    {
        base.OnChartThemeChanged(theme);
        _heightStrokePaint.Color = theme.SeriesPrimary;
        _heightAxisLabelsPaint.Color = theme.AxisLabel;
        _heightAxisNamePaint.Color = theme.AxisName;
        _heightAxisSeparatorsPaint.Color = theme.Separator;
    }

    /// <summary>Convenience accessor: title for the chart.</summary>
    public string Title => Strings.Pick_Chart_Title_WaterLevel;

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
