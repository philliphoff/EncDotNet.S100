using System.Diagnostics;
using EncDotNet.S100.Renderers.Mapsui.Avalonia;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Capture-synchronized Mapsui control that records live paint timing for
/// Viewer diagnostics.
/// </summary>
internal sealed class InstrumentedMapControl : CaptureSynchronizedMapControl
{
    private const double IdleGapThresholdMs = 500.0;

    private long _lastEndTimestamp;
    private volatile IRenderActivitySink? _activitySink;

    /// <summary>
    /// Hears this control's paints: its map's render-activity monitors (split
    /// map, #1735), or <see langword="null"/> before the map view attaches or
    /// after it tears down. Read on the render thread.
    /// </summary>
    public IRenderActivitySink? ActivitySink
    {
        get => _activitySink;
        set => _activitySink = value;
    }

    protected override void OnLivePaintStarted(long startTimestamp)
    {
        MapPaintInstrumentation.BeginPaint();
    }

    protected override void OnLivePaintCompleted(
        long startTimestamp,
        long endTimestamp)
    {
        RecordPaint(startTimestamp, endTimestamp);
        MapPaintInstrumentation.EndPaintAndEmit();
    }

    private void RecordPaint(long startTimestamp, long endTimestamp)
    {
        var durationMs = Stopwatch.GetElapsedTime(
            startTimestamp,
            endTimestamp).TotalMilliseconds;
        Telemetry.MapPaintDuration.Record(durationMs);

        var previousEnd = _lastEndTimestamp;
        _lastEndTimestamp = endTimestamp;
        if (previousEnd != 0)
        {
            var intervalMs = Stopwatch.GetElapsedTime(
                previousEnd,
                endTimestamp).TotalMilliseconds;
            if (intervalMs <= IdleGapThresholdMs)
            {
                Telemetry.MapPaintInterval.Record(intervalMs);
            }
        }

        var sink = _activitySink;
        if (sink is not null)
        {
            var styles = MapPaintInstrumentation.CollectStyleSnapshot();
            sink.NotifyPaint(durationMs, styles);
        }
    }
}
