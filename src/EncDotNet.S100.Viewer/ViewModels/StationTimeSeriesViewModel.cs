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
    private readonly RectangularSection _nowSection;
    private bool _disposed;

    /// <summary>
    /// Constructs a view model bound to <paramref name="snapshot"/>. The
    /// <paramref name="globalTime"/> service may be <c>null</c> in unit
    /// tests; when supplied, <see cref="GlobalTimeService.CurrentTimeChanged"/>
    /// is observed for the lifetime of this view model.
    /// </summary>
    protected StationTimeSeriesViewModel(StationTimeSeriesSnapshot snapshot, GlobalTimeService? globalTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        _globalTime = globalTime;

        _nowSection = new RectangularSection
        {
            Xi = double.NaN,
            Xj = double.NaN,
            Fill = null,
            Stroke = new SolidColorPaint(new SKColor(0xE6, 0x49, 0x4B), 1.5f),
        };
        Sections = new[] { _nowSection };

        TimeAxis = new Axis
        {
            Name = Strings.Pick_Chart_Axis_Time,
            Labeler = FormatTimeAxisLabel,
            LabelsRotation = 0,
            UnitWidth = TimeSpan.FromMinutes(1).Ticks,
            MinStep = TimeSpan.FromMinutes(1).Ticks,
        };
        TimeAxisArray = new ICartesianAxis[] { TimeAxis };

        if (_globalTime is not null)
        {
            _globalTime.CurrentTimeChanged += OnGlobalTimeChanged;
            if (_globalTime.CurrentTime is { } current)
                UpdateNowMarker(current);
        }
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

    private static string FormatTimeAxisLabel(double ticks)
    {
        if (!double.IsFinite(ticks)) return string.Empty;
        var t = (long)ticks;
        if (t < DateTime.MinValue.Ticks || t > DateTime.MaxValue.Ticks)
            return string.Empty;
        var dt = new DateTime(t, DateTimeKind.Utc);
        return dt.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
