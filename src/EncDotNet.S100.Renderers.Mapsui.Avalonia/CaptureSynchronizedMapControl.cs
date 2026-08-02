using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Mapsui.UI.Avalonia;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia;

/// <summary>
/// Mapsui Avalonia control that serializes live painting against offscreen
/// captures that read shared Skia image resources.
/// </summary>
/// <remarks>
/// <para>
/// The control adds no S-100 dataset or presentation behavior. It only brackets
/// Mapsui's live draw operation with the synchronization required by
/// <see cref="AvaloniaMapsuiMapAdapter"/> and
/// <see cref="AvaloniaControlCapture"/>.
/// </para>
/// <para>
/// Derive from this type to add host diagnostics. Override
/// <see cref="OnLivePaintStarted"/> and <see cref="OnLivePaintCompleted"/>
/// rather than replacing the synchronization markers.
/// </para>
/// </remarks>
public class CaptureSynchronizedMapControl : MapControl
{
    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var frame = new LivePaintFrame();
        context.Custom(new StartMarkerOperation(frame, this));
        base.Render(context);
        context.Custom(new EndMarkerOperation(frame, this));
    }

    /// <summary>
    /// Called on the compositor render thread after the capture gate has been
    /// acquired and immediately before Mapsui paints.
    /// </summary>
    /// <param name="startTimestamp">
    /// The <see cref="System.Diagnostics.Stopwatch"/> timestamp for the start
    /// of the live paint.
    /// </param>
    protected virtual void OnLivePaintStarted(long startTimestamp)
    {
    }

    /// <summary>
    /// Called on the compositor render thread immediately after Mapsui paints
    /// and before capture synchronization drains pending GPU work.
    /// </summary>
    /// <param name="startTimestamp">The timestamp supplied to the start hook.</param>
    /// <param name="endTimestamp">The timestamp captured after Mapsui painted.</param>
    protected virtual void OnLivePaintCompleted(long startTimestamp, long endTimestamp)
    {
    }

    private sealed class LivePaintFrame
    {
        public long StartTimestamp;
        public CaptureCoordinator.GateLease? GateLease;
    }

    private sealed class StartMarkerOperation : ICustomDrawOperation
    {
        private readonly LivePaintFrame _frame;
        private readonly CaptureSynchronizedMapControl _owner;

        public StartMarkerOperation(
            LivePaintFrame frame,
            CaptureSynchronizedMapControl owner)
        {
            _frame = frame;
            _owner = owner;
        }

        public Rect Bounds => default;

        public void Dispose()
        {
        }

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var lease = CaptureCoordinator.EnterLivePaint();
            _frame.GateLease = lease;
            try
            {
                _frame.StartTimestamp = Stopwatch.GetTimestamp();
                _owner.OnLivePaintStarted(_frame.StartTimestamp);
            }
            catch
            {
                _frame.GateLease = null;
                CaptureCoordinator.ExitGate(lease);
                throw;
            }
        }
    }

    private sealed class EndMarkerOperation : ICustomDrawOperation
    {
        private readonly LivePaintFrame _frame;
        private readonly CaptureSynchronizedMapControl _owner;

        public EndMarkerOperation(
            LivePaintFrame frame,
            CaptureSynchronizedMapControl owner)
        {
            _frame = frame;
            _owner = owner;
        }

        public Rect Bounds => default;

        public void Dispose()
        {
        }

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            try
            {
                if (_frame.StartTimestamp == 0)
                {
                    return;
                }

                var endTimestamp = Stopwatch.GetTimestamp();
                _owner.OnLivePaintCompleted(_frame.StartTimestamp, endTimestamp);
            }
            finally
            {
                if (_frame.GateLease is { } lease)
                {
                    try
                    {
                        if (CaptureCoordinator.CaptureActive)
                        {
                            try
                            {
                                DrainGpu(context);
                            }
                            finally
                            {
                                CaptureCoordinator.NotifyDrained();
                            }
                        }
                    }
                    finally
                    {
                        _frame.GateLease = null;
                        CaptureCoordinator.ExitGate(lease);
                    }
                }
            }
        }

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
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"CaptureCoordinator: unable to drain the live Skia context: {exception.Message}");
            }
        }
    }
}
