using System;
using System.Diagnostics;
using System.Threading;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Process-wide mutual-exclusion gate that serialises the viewer's two
/// Skia paint paths so they never touch shared GPU resources at the same
/// time:
/// <list type="bullet">
/// <item>the <b>live</b> on-screen paint performed by
/// <see cref="InstrumentedMapControl"/> on Avalonia's compositor render
/// thread (the <c>RenderTimerLoop</c>), and</item>
/// <item>the <b>offscreen</b> framebuffer readback performed by
/// <c>MapsuiMapHost.RenderCurrentViewToPngAsync</c> on the UI thread when
/// an MCP <c>render_to_image</c> request is serviced.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Both paths render the same live <c>Map.Layers</c>, whose symbol bitmaps
/// are cached as shared <c>SKImage</c> instances. On a GPU-accelerated
/// (e.g. Metal) build, the live paint uploads those images to GPU textures
/// (<c>sk_image_make_texture_image</c>) while drawing; if the offscreen
/// capture reads the same <c>SKImage</c> concurrently, Skia's
/// single-threaded <c>GrDirectContext</c> contract is violated and the
/// process crashes with <c>EXC_BAD_ACCESS</c> on the render-timer thread
/// (issue #337).
/// </para>
/// <para>
/// The gate is a plain <see cref="Monitor"/> over a private object. The
/// live paint takes it (blocking) for the duration of the on-screen Skia
/// draw — bracketed by the start/end markers in
/// <see cref="InstrumentedMapControl"/> — and the offscreen capture takes
/// it (with a bounded timeout) for the duration of its render. Monitor's
/// thread affinity is deliberate: should an end marker ever be culled and
/// the live release be skipped, the render thread (which is reentrant on
/// the lock) keeps painting and only offscreen captures degrade, rather
/// than the whole compositor freezing.
/// </para>
/// <para>
/// All members are safe to call from any thread.
/// </para>
/// </remarks>
internal static class RenderGate
{
    private static readonly object Gate = new();

    /// <summary>
    /// Number of offscreen captures currently waiting for, or holding, the
    /// gate. Read by the live paint's end marker so it knows to drain this
    /// frame's GPU work before releasing the gate while a capture is pending.
    /// </summary>
    private static int _captureDepth;

    /// <summary>
    /// Whether an offscreen <c>render_to_image</c> capture is currently
    /// waiting for or running under the gate. While true, the live paint's
    /// end marker flushes and synchronises the Skia GPU context before
    /// releasing the gate so a freshly-uploaded symbol texture cannot still
    /// be in flight when the capture reads the same shared <c>SKImage</c>
    /// (issue #337). When false (the steady state with no capture) the live
    /// paint pays nothing.
    /// </summary>
    public static bool CaptureActive => Volatile.Read(ref _captureDepth) > 0;

    /// <summary>
    /// Signalled by the live paint's end marker each time it drains the GPU
    /// while a capture is pending (see <see cref="NotifyDrained"/>). A capture
    /// uses <see cref="WaitForFreshDrain"/> to force one fully-drained live
    /// frame immediately before it reads the shared layers, closing the
    /// residual #337 window where the gate is free but a prior frame's
    /// present-flush is still in flight.
    /// </summary>
    private static readonly ManualResetEventSlim DrainSignal = new(false);

    /// <summary>
    /// Marks the start of an offscreen capture so the live paint begins
    /// draining the GPU at its end marker. Balance with <see cref="EndCapture"/>.
    /// Depth-counted, so nested captures keep <see cref="CaptureActive"/> set
    /// until the outermost completes.
    /// </summary>
    public static void BeginCapture() => Interlocked.Increment(ref _captureDepth);

    /// <summary>
    /// Marks the end of an offscreen capture started with
    /// <see cref="BeginCapture"/>.
    /// </summary>
    public static void EndCapture() => Interlocked.Decrement(ref _captureDepth);

    /// <summary>
    /// Called by the live paint's end marker after it has flushed and
    /// synchronised the GPU under a pending capture, so a capture waiting in
    /// <see cref="WaitForFreshDrain"/> knows a drained frame has completed.
    /// </summary>
    public static void NotifyDrained() => DrainSignal.Set();

    /// <summary>
    /// Blocks until the next live paint drains the GPU (or <paramref name="timeout"/>
    /// elapses). The caller must have already taken <see cref="BeginCapture"/>
    /// and triggered a repaint; this guarantees the most recent symbol-texture
    /// uploads have completed before the offscreen capture reads the shared
    /// <c>SKImage</c>s. Returns <see langword="true"/> if a drain was observed.
    /// </summary>
    public static bool WaitForFreshDrain(TimeSpan timeout)
    {
        DrainSignal.Reset();
        return DrainSignal.Wait(timeout);
    }

    /// <summary>
    /// Acquires the gate for the live on-screen paint, blocking until any
    /// in-flight offscreen capture has finished. Call from the start
    /// marker on the compositor render thread; pair with
    /// <see cref="ExitLivePaint"/> from the matching end marker.
    /// </summary>
    /// <remarks>
    /// The live paint must not be skipped (Mapsui's own draw operation
    /// will run regardless), so this blocks rather than abandoning the
    /// frame. The wait is bounded only by the offscreen capture, which is
    /// a single, self-contained render.
    /// </remarks>
    public static void EnterLivePaint() => Monitor.Enter(Gate);

    /// <summary>
    /// Releases the gate previously taken by <see cref="EnterLivePaint"/>.
    /// Must be called on the same thread that entered.
    /// </summary>
    public static void ExitLivePaint() => Monitor.Exit(Gate);

    /// <summary>
    /// Runs <paramref name="capture"/> while holding the gate so it cannot
    /// overlap a live on-screen paint, returning the captured bytes.
    /// </summary>
    /// <param name="capture">
    /// The offscreen render to perform under mutual exclusion.
    /// </param>
    /// <param name="timeout">
    /// Maximum time to wait for the live paint to yield the gate. If the
    /// timeout elapses the capture runs anyway (degraded, unsynchronised)
    /// rather than hanging — a perpetual hold would only occur if the
    /// compositor were already wedged.
    /// </param>
    /// <returns>The bytes produced by <paramref name="capture"/>.</returns>
    public static byte[]? RunCapture(Func<byte[]?> capture, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(capture);

        // Mark a capture as pending *before* contending for the gate so the
        // live frame that currently holds it will, at its end marker, drain
        // and synchronise the GPU before releasing (issue #337). Any live
        // frame in flight is exactly the dangerous case — a quiescent view
        // has already presented (and therefore flushed) its last frame.
        BeginCapture();
        var taken = false;
        try
        {
            Monitor.TryEnter(Gate, timeout, ref taken);
            if (!taken)
            {
                Debug.WriteLine(
                    "RenderGate: timed out waiting for the live paint to yield; " +
                    "running render_to_image capture unsynchronised.");
            }

            return capture();
        }
        finally
        {
            if (taken) Monitor.Exit(Gate);
            EndCapture();
        }
    }
}
