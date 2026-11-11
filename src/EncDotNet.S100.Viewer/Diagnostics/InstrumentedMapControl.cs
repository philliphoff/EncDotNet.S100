using System.Diagnostics;
using EncDotNet.S100.Renderers.Mapsui.Avalonia;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Capture-synchronized Mapsui control that records live paint timing for
/// Viewer diagnostics.
/// </summary>
internal sealed class InstrumentedMapControl : CaptureSynchronizedMapControl
{
    private const double IdleGapThresholdMs = 500.0;

    private long _lastEndTimestamp;

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

        var sink = RenderActivityHub.Sink;
        if (sink is not null)
        {
            var styles = MapPaintInstrumentation.CollectStyleSnapshot();
            sink.NotifyPaint(durationMs, styles);
        }
    }
}
