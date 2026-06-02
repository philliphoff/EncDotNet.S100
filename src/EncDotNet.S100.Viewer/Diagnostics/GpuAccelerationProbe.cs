using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// One-shot diagnostic that asks Avalonia for a Skia API lease the
/// same way Mapsui's <c>MapControl</c> does (via
/// <see cref="ISkiaSharpApiLease"/>) and reports whether the lease
/// exposes a non-null <c>GrContext</c>. A non-null context means the
/// canvas Mapsui is drawing into is GPU-backed; null means the
/// Avalonia compositor handed Skia a software bitmap and every paint
/// is rasterised on the CPU.
/// </summary>
/// <remarks>
/// The control is invisible (transparent, hit-test disabled, 1×1)
/// and removes itself after the first successful probe so it costs
/// nothing in steady state. Result is written to <see cref="LastResult"/>
/// and to <c>stderr</c> for log scraping.
/// </remarks>
public sealed class GpuAccelerationProbe : Control
{
    /// <summary>Latest probe result; null until first paint.</summary>
    public static GpuProbeResult? LastResult { get; private set; }

    /// <summary>Raised on the UI thread when a probe completes.</summary>
    public static event Action<GpuProbeResult>? Probed;

    private bool _reported;

    public GpuAccelerationProbe()
    {
        Width = 1;
        Height = 1;
        IsHitTestVisible = false;
        // Note: do NOT set Opacity = 0; Avalonia's compositor skips
        // Render() on zero-opacity visuals, so the probe op would
        // never run.
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Force at least one paint so the custom draw op gets a
        // chance to lease the Skia API.
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_reported) return;
        context.Custom(new ProbeOp(new Rect(0, 0, 1, 1), this));
    }

    private void Report(GpuProbeResult result)
    {
        if (_reported) return;
        _reported = true;
        LastResult = result;
        Console.Error.WriteLine(
            $"[GPU-PROBE] gpuAccelerated={result.GpuAccelerated} " +
            $"backend={result.BackendName} " +
            $"surfaceWidth={result.SurfaceWidth} " +
            $"surfaceHeight={result.SurfaceHeight}");
        try { Probed?.Invoke(result); } catch { }
    }

    private sealed class ProbeOp : ICustomDrawOperation
    {
        private readonly GpuAccelerationProbe _owner;

        public ProbeOp(Rect bounds, GpuAccelerationProbe owner)
        {
            Bounds = bounds;
            _owner = owner;
        }

        public Rect Bounds { get; }

        public void Dispose() { }

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null)
            {
                _owner.Report(new GpuProbeResult(false, "no-skia-lease-feature", 0, 0));
                return;
            }

            using var lease = leaseFeature.Lease();
            var gr = lease.GrContext;
            var surface = lease.SkSurface;
            int w = 0, h = 0;
            try
            {
                if (surface is not null)
                {
                    w = surface.Canvas.LocalClipBounds is { } b
                        ? (int)b.Width
                        : 0;
                    h = surface.Canvas.LocalClipBounds is { } b2
                        ? (int)b2.Height
                        : 0;
                }
            }
            catch { }

            string backend = gr is null
                ? "software"
                : gr.Backend.ToString();
            _owner.Report(new GpuProbeResult(gr is not null, backend, w, h));
        }
    }
}

/// <summary>Result of a single <see cref="GpuAccelerationProbe"/> paint.</summary>
public sealed record GpuProbeResult(
    bool GpuAccelerated,
    string BackendName,
    int SurfaceWidth,
    int SurfaceHeight);
