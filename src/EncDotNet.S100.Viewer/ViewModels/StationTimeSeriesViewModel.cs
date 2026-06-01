using System;
using System.Collections.Generic;
using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Base view model for the LiveCharts2 time-series chart shown in the
/// Object Information (pick) panel when the selected hit represents a
/// fixed-station observation. Holds the station's identity, the projected
/// chart series + axes, and a read-only "now" marker section that tracks
/// <see cref="GlobalTimeService.CurrentTime"/>.
/// </summary>
/// <remarks>
/// Derived classes shape the per-product chart (S-104 single chart, S-111
/// two stacked charts) by populating the appropriate <see cref="ISeries"/>
/// + axis collections; this base class owns the time axis, the now-marker
/// section, and the global-time subscription.
///
/// The chart is strictly read-only with respect to the global clock — it
/// does not call <see cref="GlobalTimeService.SetCurrentTime"/>; the
/// existing timeline row remains the sole scrubber.
/// </remarks>
internal abstract class StationTimeSeriesViewModel : ViewModelBase, IDisposable
{
    private readonly GlobalTimeService? _globalTime;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly IThemeService? _themeService;
    private readonly RectangularSection _nowSection;
    private readonly SolidColorPaint _nowMarkerPaint;
    private readonly SolidColorPaint _timeAxisLabelsPaint;
    private readonly SolidColorPaint _timeAxisNamePaint;
    private readonly SolidColorPaint _timeAxisSeparatorsPaint;
    private bool _disposed;

    /// <summary>
    /// Constructs a view model bound to <paramref name="snapshot"/>. The
    /// <paramref name="globalTime"/> service may be <c>null</c> in unit
    /// tests; when supplied, <see cref="GlobalTimeService.CurrentTimeChanged"/>
    /// is observed for the lifetime of this view model. Likewise
    /// <paramref name="themeService"/> may be <c>null</c>; when supplied,
    /// <see cref="IThemeService.ThemeChanged"/> drives in-place updates of
    /// the chart's <see cref="SolidColorPaint"/> colours so the chart
    /// follows the chrome <see cref="Avalonia.Styling.ThemeVariant"/>.
    /// </summary>
    protected StationTimeSeriesViewModel(
        StationTimeSeriesSnapshot snapshot,
        GlobalTimeService? globalTime,
        ITimeFormatProvider? timeFormat = null,
        IThemeService? themeService = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        _globalTime = globalTime;
        _timeFormat = timeFormat;
        _themeService = themeService;

        CurrentChartTheme = ChartTheme.Resolve(themeService?.IsDarkTheme ?? false);

        _nowMarkerPaint = new SolidColorPaint(CurrentChartTheme.NowMarker, 1.5f);
        _nowSection = new RectangularSection
        {
            Xi = double.NaN,
            Xj = double.NaN,
            Fill = null,
            Stroke = _nowMarkerPaint,
        };
        Sections = new[] { _nowSection };

        _timeAxisLabelsPaint = new SolidColorPaint(CurrentChartTheme.AxisLabel);
        _timeAxisNamePaint = new SolidColorPaint(CurrentChartTheme.AxisName);
        _timeAxisSeparatorsPaint = new SolidColorPaint(CurrentChartTheme.Separator);

        TimeAxis = new Axis
        {
            Name = Strings.Pick_Chart_Axis_Time,
            Labeler = FormatTimeAxisLabel,
            LabelsRotation = 0,
            UnitWidth = TimeSpan.FromMinutes(1).Ticks,
            MinStep = TimeSpan.FromMinutes(1).Ticks,
            LabelsPaint = _timeAxisLabelsPaint,
            NamePaint = _timeAxisNamePaint,
            SeparatorsPaint = _timeAxisSeparatorsPaint,
        };
        TimeAxisArray = new ICartesianAxis[] { TimeAxis };

        if (_globalTime is not null)
        {
            _globalTime.CurrentTimeChanged += OnGlobalTimeChanged;
            if (_globalTime.CurrentTime is { } current)
                UpdateNowMarker(current);
        }

        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged += OnTimeFormatChanged;

        if (_themeService is not null)
            _themeService.ThemeChanged += OnThemeChanged;
    }

    /// <summary>The raw snapshot that backs this view model.</summary>
    public StationTimeSeriesSnapshot Snapshot { get; }

    /// <summary>Station identifier — typically shown above the chart.</summary>
    public string StationId => Snapshot.StationId;

    /// <summary>Station latitude in degrees (WGS-84).</summary>
    public double Latitude => Snapshot.Latitude;

    /// <summary>Station longitude in degrees (WGS-84).</summary>
    public double Longitude => Snapshot.Longitude;

    /// <summary>
    /// Shared X axis (UTC time) used by every chart in the panel. Subclasses
    /// expose this same instance so stacked S-111 charts align horizontally.
    /// </summary>
    public Axis TimeAxis { get; }

    /// <summary>
    /// Single-element array wrapper for <see cref="TimeAxis"/>. LiveCharts2's
    /// <c>CartesianChart.XAxes</c> expects an axis collection; XAML-binding
    /// to a property instead of an inline array keeps the binding compiled
    /// and re-uses the same axis instance across stacked charts.
    /// </summary>
    public ICartesianAxis[] TimeAxisArray { get; }

    /// <summary>
    /// Read-only "now" marker section pinned to
    /// <see cref="GlobalTimeService.CurrentTime"/>. Width is zero (vertical
    /// rule); subclasses attach it to each chart via the <c>Sections</c>
    /// binding.
    /// </summary>
    public IReadOnlyList<RectangularSection> Sections { get; }

    /// <summary>
    /// Re-positions the now-marker to the supplied UTC time. Public so unit
    /// tests can drive it without standing up a <see cref="GlobalTimeService"/>.
    /// </summary>
    public void UpdateNowMarker(DateTime time)
    {
        var x = time.Ticks;
        _nowSection.Xi = x;
        _nowSection.Xj = x;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_globalTime is not null)
            _globalTime.CurrentTimeChanged -= OnGlobalTimeChanged;
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged -= OnTimeFormatChanged;
        if (_themeService is not null)
            _themeService.ThemeChanged -= OnThemeChanged;
    }

    /// <summary>
    /// Current chart palette derived from the chrome
    /// <see cref="Avalonia.Styling.ThemeVariant"/>. Refreshed in place
    /// when <see cref="IThemeService.ThemeChanged"/> fires; subclasses
    /// read this from <see cref="OnChartThemeChanged"/> to update their
    /// per-product paints.
    /// </summary>
    /// <remarks>
    /// This responds to <b>chrome theme</b> (Light vs Dark), not the
    /// S-100 map palette (Day / Dusk / Night). The map palette is owned
    /// by <see cref="SettingsViewModel.SelectedPalette"/> and is not
    /// consumed by these charts.
    /// </remarks>
    protected internal ChartTheme CurrentChartTheme { get; private set; }

    /// <summary>
    /// Override to update product-specific <see cref="SolidColorPaint"/>
    /// instances when <see cref="CurrentChartTheme"/> changes. Mutating
    /// existing paints in place (via <see cref="SolidColorPaint.Color"/>)
    /// avoids the cost of rebuilding series; LiveCharts2 redraws on the
    /// next paint cycle. Base implementation updates the shared time
    /// axis paints and the "now" marker stroke.
    /// </summary>
    /// <param name="theme">The new chart palette.</param>
    protected virtual void OnChartThemeChanged(ChartTheme theme)
    {
        _nowMarkerPaint.Color = theme.NowMarker;
        _timeAxisLabelsPaint.Color = theme.AxisLabel;
        _timeAxisNamePaint.Color = theme.AxisName;
        _timeAxisSeparatorsPaint.Color = theme.Separator;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        CurrentChartTheme = ChartTheme.Resolve(_themeService?.IsDarkTheme ?? false);
        OnChartThemeChanged(CurrentChartTheme);
    }

    /// <summary>
    /// Projects <paramref name="channel"/> into a list of
    /// <see cref="DateTimePoint"/>s suitable for a
    /// <see cref="LineSeries{DateTimePoint}"/>. Samples whose value equals
    /// the channel's <see cref="StationTimeSeriesChannel.FillValue"/> (or
    /// is non-finite) are dropped so the chart does not show large
    /// downward spikes for missing data.
    /// </summary>
    protected static List<DateTimePoint> ProjectChannel(
        IReadOnlyList<DateTime> times,
        StationTimeSeriesChannel channel)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(channel);
        var values = channel.Values;
        var n = Math.Min(times.Count, values.Count);
        var points = new List<DateTimePoint>(n);
        var fill = channel.FillValue;
        for (var i = 0; i < n; i++)
        {
            var v = values[i];
            if (!float.IsFinite(v)) continue;
            if (fill.HasValue && v == fill.Value) continue;
            points.Add(new DateTimePoint(times[i], v));
        }
        return points;
    }

    private void OnGlobalTimeChanged(DateTime time) => UpdateNowMarker(time);

    private void OnTimeFormatChanged(TimeFormat format)
    {
        // Force LiveCharts2 to re-evaluate the axis labels — the labeler
        // closes over _timeFormat.Current so simply notifying is enough.
        TimeAxis.Labeler = FormatTimeAxisLabel;
    }

    /// <summary>
    /// Formats a chart point's underlying <see cref="DateTime"/> (always
    /// stored as UTC ticks via <see cref="DateTimePoint"/>) for display
    /// in the per-point tooltip, honouring the mariner's selected
    /// time-zone display (<see cref="TimeFormat"/>). Used by subclasses
    /// to override the default LiveCharts2 tooltip, which otherwise
    /// renders the raw UTC <c>DateTime.ToString()</c> regardless of the
    /// chosen format (LiveCharts2 v2 has no awareness of local zones).
    /// </summary>
    protected internal string FormatTooltipDateTime(DateTime utc)
    {
        var fmt = _timeFormat?.Current ?? TimeFormat.Local;
        if (fmt == TimeFormat.Utc)
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc)
                .ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString("g", CultureInfo.CurrentCulture);
    }

    private string FormatTimeAxisLabel(double ticks)
    {
        if (!double.IsFinite(ticks)) return string.Empty;
        var t = (long)ticks;
        if (t < DateTime.MinValue.Ticks || t > DateTime.MaxValue.Ticks)
            return string.Empty;
        var dt = new DateTime(t, DateTimeKind.Utc);
        var fmt = _timeFormat?.Current ?? TimeFormat.Local;
        if (fmt == TimeFormat.Utc)
            return dt.ToString("HH:mm", CultureInfo.InvariantCulture);
        return dt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
    }
}
