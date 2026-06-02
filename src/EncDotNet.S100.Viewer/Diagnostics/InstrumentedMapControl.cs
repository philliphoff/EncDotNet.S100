using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Mapsui.UI.Avalonia;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// <see cref="MapControl"/> subclass that wall-clock-times the
/// Mapsui custom draw operation on the compositor render thread.
/// </summary>
/// <remarks>
/// <para>
/// Mapsui's <c>MapsuiCustomDrawOperation.Render(ImmediateDrawingContext)</c>
/// is sealed inside the Mapsui.UI.Avalonia DLL, so we can't subclass
/// it directly. Instead we sandwich it between two
/// <see cref="ICustomDrawOperation"/>s of our own. Avalonia replays
/// custom draw ops in registration order on the render thread, so
/// the elapsed time between the start and end markers is the actual
/// Skia paint duration — independent of UI-thread / dispatch /
/// invalidation cadence.
/// </para>
/// <para>
/// The approach is non-invasive: Mapsui's draw op is unchanged, the
/// markers do nothing visual, and the rest of <see cref="MapControl"/>
/// behaves identically. Frame interval is computed from end-marker
/// timestamps, with a 500 ms idle-gap filter so a single pause
/// doesn't dominate percentiles.
/// </para>
/// </remarks>
internal sealed class InstrumentedMapControl : MapControl
{
    /// <summary>Idle-gap threshold above which an interval sample is dropped.</summary>
    private const double IdleGapThresholdMs = 500.0;

    private long _lastEndTimestamp;

    public override void Render(DrawingContext context)
    {
        // Shared between the two markers so the END marker can
        // compute duration relative to START's timestamp without
        // touching shared state on the render thread.
        var marker = new PaintMarker();

        context.Custom(new StartMarkerOp(marker));
        base.Render(context);
        context.Custom(new EndMarkerOp(marker, this));
    }

    private void RecordPaint(long startTimestamp, long endTimestamp)
    {
        var durationMs = Stopwatch.GetElapsedTime(startTimestamp, endTimestamp).TotalMilliseconds;
        Telemetry.MapPaintDuration.Record(durationMs);

        var prevEnd = _lastEndTimestamp;
        _lastEndTimestamp = endTimestamp;
        if (prevEnd != 0)
        {
            var intervalMs = Stopwatch.GetElapsedTime(prevEnd, endTimestamp).TotalMilliseconds;
            if (intervalMs <= IdleGapThresholdMs)
            {
                Telemetry.MapPaintInterval.Record(intervalMs);
            }
        }
    }
    private sealed class PaintMarker
    {
        public long StartTimestamp;
    }

    private sealed class StartMarkerOp : ICustomDrawOperation
    {
        private readonly PaintMarker _marker;
        public StartMarkerOp(PaintMarker marker) => _marker = marker;
        public Rect Bounds => default;
        public void Dispose() { }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Render(ImmediateDrawingContext context)
        {
            _marker.StartTimestamp = Stopwatch.GetTimestamp();
            MapPaintInstrumentation.BeginPaint();
        }
    }

    private sealed class EndMarkerOp : ICustomDrawOperation
    {
        private readonly PaintMarker _marker;
        private readonly InstrumentedMapControl _owner;

        public EndMarkerOp(PaintMarker marker, InstrumentedMapControl owner)
        {
            _marker = marker;
            _owner = owner;
        }

        public Rect Bounds => default;
        public void Dispose() { }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var end = Stopwatch.GetTimestamp();
            // Defensive: if the start marker somehow didn't run
            // (e.g. compositor culled it on a clipped frame), the
            // delta would be wildly negative — skip the sample.
            if (_marker.StartTimestamp == 0) return;
            _owner.RecordPaint(_marker.StartTimestamp, end);
            MapPaintInstrumentation.EndPaintAndEmit();
        }
    }
}
