using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
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

        // Feed the render-activity monitor so off-thread MCP callers can
        // wait for idle and read the last paint's cost. Only collect the
        // per-style snapshot when a sink is actually attached, so runs
        // without an MCP consumer pay no per-paint allocation.
        var sink = RenderActivityHub.Sink;
        if (sink is not null)
        {
            var styles = MapPaintInstrumentation.CollectStyleSnapshot();
            sink.NotifyPaint(durationMs, styles);
        }
    }
    private sealed class PaintMarker
    {
        public long StartTimestamp;

        /// <summary>
        /// Whether the start marker successfully took the
        /// <see cref="RenderGate"/>, so the end marker knows to release it.
        /// </summary>
        public bool GateHeld;
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
            // Take the render gate *before* the live Skia paint so the
            // offscreen render_to_image readback cannot touch shared
            // SKImage symbol textures concurrently (issue #337). Released
            // by the matching end marker once the paint has run.
            RenderGate.EnterLivePaint();
            _marker.GateHeld = true;
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
            try
            {
                var end = Stopwatch.GetTimestamp();
                // Defensive: if the start marker somehow didn't run
                // (e.g. compositor culled it on a clipped frame), the
                // delta would be wildly negative — skip the sample.
                if (_marker.StartTimestamp == 0) return;
                _owner.RecordPaint(_marker.StartTimestamp, end);
                MapPaintInstrumentation.EndPaintAndEmit();
            }
            finally
            {
                // Release the render gate taken by the start marker once
                // the live Skia paint has run (issue #337). Guarded so a
                // missing start marker never triggers a spurious release.
                if (_marker.GateHeld)
                {
                    // If an offscreen render_to_image capture is waiting on
                    // (or running under) the gate, drain this frame's GPU
                    // work before releasing it. The live paint records its
                    // symbol-texture uploads (sk_image_make_texture_image)
                    // into the Metal command buffer during this frame; if we
                    // release the gate before that work has completed, the
                    // capture can read the same shared SKImage while the GPU
                    // upload is still in flight — the #337 crash. A
                    // synchronous flush makes the upload finish inside the
                    // bracket. Only paid while a capture is pending; steady
                    // live painting is untouched.
                    if (RenderGate.CaptureActive)
                    {
                        DrainGpu(context);

                        // Let a capture waiting in WaitForFreshDrain know that
                        // a fully-synchronised frame has just completed, so it
                        // can read the shared layers without racing a pending
                        // GPU upload (issue #337).
                        RenderGate.NotifyDrained();
                    }

                    _marker.GateHeld = false;
                    RenderGate.ExitLivePaint();
                }
            }
        }

        /// <summary>
        /// Synchronously flushes and submits the live Skia GPU context so any
        /// symbol-texture uploads recorded during this frame complete before
        /// the render gate is released to a waiting offscreen capture
        /// (issue #337). Best-effort: a flush failure must never crash the
        /// compositor render thread or mask the paint, and on a CPU/software
        /// backend (<see cref="ISkiaSharpApiLease.GrContext"/> is null) there
        /// is no GPU work to drain.
        /// </summary>
        private static void DrainGpu(ImmediateDrawingContext context)
        {
            try
            {
                if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature))
                        is not ISkiaSharpApiLeaseFeature leaseFeature)
                {
                    return;
                }

                using var lease = leaseFeature.Lease();
                lease.GrContext?.Flush(submit: true, synchronous: true);
            }
            catch
            {
                // The gate still serialises the draw calls themselves; a
                // missed flush only narrows, never widens, the race window.
            }
        }
    }
}
