using System.Diagnostics;
using EncDotNet.S100.Core.Scheduling;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Measures how long the UI thread goes without picking up new work (map
/// rendering slice 6): a probe thread posts a no-op to the UI dispatcher at the
/// highest priority, records how long it took to run, waits about a
/// millisecond and posts the next. The longest delay is, to within the probe
/// interval, the longest single task the UI thread ran.
/// </summary>
/// <remarks>
/// Nothing runs until <see cref="Start"/>; the perf harness starts it with a
/// metrics window, so a viewer without the harness pays nothing. One probe is
/// in flight at a time, so a blocked UI thread doesn't build up a queue.
/// </remarks>
internal sealed class UiThreadStallMonitor : IDisposable
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(1);

    private readonly Action<Action> _postToUi;
    private readonly LatencyHistogram _delays = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _stop;

    /// <summary>Creates a monitor.</summary>
    /// <param name="postToUi">Posts an action to the UI thread at the highest priority.</param>
    public UiThreadStallMonitor(Action<Action> postToUi)
    {
        ArgumentNullException.ThrowIfNull(postToUi);
        _postToUi = postToUi;
    }

    /// <summary>The delays measured so far, cumulative; take differences with <see cref="LatencySnapshot.Since"/>.</summary>
    public LatencySnapshot Snapshot() => _delays.Snapshot();

    /// <summary>Records one measured delay (the probe thread calls this).</summary>
    /// <param name="ticks">The delay in <see cref="Stopwatch"/> ticks.</param>
    internal void Record(long ticks) =>
        _delays.Record(ticks * 1000.0 / Stopwatch.Frequency);

    /// <summary>Starts probing, if it isn't already.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_stop is not null)
                return;
            _stop = new CancellationTokenSource();
            var token = _stop.Token;
            var thread = new Thread(() => Probe(token))
            {
                IsBackground = true,
                Name = "UI stall probe",
                Priority = ThreadPriority.AboveNormal,
            };
            thread.Start();
        }
    }

    /// <summary>Stops probing.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _stop?.Cancel();
            _stop?.Dispose();
            _stop = null;
        }
    }

    private void Probe(CancellationToken stop)
    {
        using var ran = new SemaphoreSlim(0);
        while (!stop.IsCancellationRequested)
        {
            var posted = Stopwatch.GetTimestamp();
            long delay = 0;
            try
            {
                _postToUi(() =>
                {
                    delay = Stopwatch.GetTimestamp() - posted;
                    ran.Release();
                });
            }
            catch (InvalidOperationException)
            {
                return; // The dispatcher has shut down.
            }

            // Waits without a timeout so a long UI task is measured in full;
            // the probe is a background thread, so it never holds up exit.
            ran.Wait();
            Record(delay);
            Thread.Sleep(ProbeInterval);
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();
}
