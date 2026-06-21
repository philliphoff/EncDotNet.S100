using System;
using System.Threading;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A one-way drain gate used to stop background tile-rasterisation workers and
/// wait for in-flight work to finish before the process tears down native Skia.
///
/// <para>
/// Tile workers call into native Skia (<c>SKBitmap</c>/<c>SKImage</c>, typeface
/// lookup, …). If the managed runtime begins exiting — running the C++
/// <c>__cxa_finalize</c> destructors of <c>libSkiaSharp</c> — while a worker is
/// mid-rasterise, the worker dereferences freed Skia globals and the process
/// dies with a native SIGSEGV. The host calls <see cref="DrainAndWait"/> on its
/// shutdown path; thereafter <see cref="TryRegister"/> refuses new workers and
/// the wait completes once all registered workers have called
/// <see cref="Complete"/>.
/// </para>
///
/// <para>
/// Correctness invariant: <em>no Skia call happens after
/// <see cref="DrainAndWait"/> returns</em>, except for a worker that was already
/// registered (and therefore awaited). A worker that races with the drain either
/// fails <see cref="TryRegister"/> (and never touches Skia) or succeeds but must
/// re-check <see cref="IsDraining"/> at the top of its loop before any Skia call.
/// </para>
/// </summary>
internal sealed class WorkerDrainGate
{
    private volatile bool _draining;
    private int _active;
    private readonly ManualResetEventSlim _idle = new(initialState: true);

    /// <summary>Whether <see cref="DrainAndWait"/> has been invoked.</summary>
    public bool IsDraining => _draining;

    /// <summary>Current count of registered, not-yet-completed workers.</summary>
    public int ActiveWorkers => Volatile.Read(ref _active);

    /// <summary>
    /// Attempts to register a worker before it starts. Returns
    /// <see langword="false"/> if the gate is draining, in which case the caller
    /// MUST NOT start the worker (and the registration is already undone).
    /// </summary>
    public bool TryRegister()
    {
        // Register first, then re-check, so a worker that registers concurrently
        // with DrainAndWait is either properly counted (and awaited) or cleanly
        // undone — the drain wait can never miss a worker it should await.
        Interlocked.Increment(ref _active);
        _idle.Reset();
        if (_draining)
        {
            Complete();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Marks a registered worker as finished. When the last worker completes,
    /// the idle signal fires and a pending <see cref="DrainAndWait"/> returns.
    /// </summary>
    public void Complete()
    {
        if (Interlocked.Decrement(ref _active) == 0)
        {
            _idle.Set();
        }
    }

    /// <summary>
    /// Sets the draining flag (permanent for the gate's lifetime) and blocks
    /// until all registered workers complete or <paramref name="timeout"/>
    /// elapses.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for in-flight workers.</param>
    /// <returns>
    /// <see langword="true"/> if all workers drained within the timeout;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public bool DrainAndWait(TimeSpan timeout)
    {
        _draining = true;
        return _idle.Wait(timeout);
    }
}
