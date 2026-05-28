using System;
using System.Threading;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Coalesces frequent settings writes (PR-M3): each <see cref="RequestSave"/>
/// call (re)starts a <see cref="DelayMilliseconds"/> timer; on fire the
/// supplied save action runs exactly once on the UI thread (via the
/// optional dispatch callback) or synchronously on the timer thread.
/// <see cref="Flush"/> cancels any pending timer and writes immediately —
/// MainWindow calls it during shutdown so the last splitter drag is
/// always captured.
/// </summary>
internal sealed class DebouncedSettingsSaver : IDisposable
{
    public const int DefaultDelayMilliseconds = 500;

    private readonly Action _save;
    private readonly Action<Action>? _dispatch;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private bool _isPending;
    private bool _disposed;

    /// <summary>Debounce window in milliseconds.</summary>
    public int DelayMilliseconds { get; }

    /// <summary>Creates a new saver.</summary>
    /// <param name="save">Action that writes settings to disk.</param>
    /// <param name="delayMilliseconds">Debounce window; defaults to 500 ms.</param>
    /// <param name="dispatch">
    /// Optional dispatcher (typically <c>Dispatcher.UIThread.Post</c>) that marshals
    /// the save call onto the UI thread. When <c>null</c>, the save runs on the
    /// timer thread — fine for tests, but real code should pass the UI dispatcher
    /// so settings stay consistent with view-model reads.
    /// </param>
    public DebouncedSettingsSaver(Action save, int delayMilliseconds = DefaultDelayMilliseconds, Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (delayMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));
        _save = save;
        _dispatch = dispatch;
        DelayMilliseconds = delayMilliseconds;
        _timer = new Timer(OnTimerFired, state: null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Schedules a debounced save. If a save is already pending, the
    /// timer is restarted so multiple rapid calls coalesce into one write.
    /// </summary>
    public void RequestSave()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _isPending = true;
            _timer.Change(DelayMilliseconds, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Cancels any pending timer and writes immediately if a save was queued.
    /// Called on shutdown so the final splitter drag isn't lost.
    /// </summary>
    public void Flush()
    {
        bool shouldSave;
        lock (_gate)
        {
            if (_disposed) return;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            shouldSave = _isPending;
            _isPending = false;
        }
        if (shouldSave)
            RunSave();
    }

    private void OnTimerFired(object? state)
    {
        bool shouldSave;
        lock (_gate)
        {
            if (_disposed) return;
            shouldSave = _isPending;
            _isPending = false;
        }
        if (shouldSave)
            RunSave();
    }

    private void RunSave()
    {
        try
        {
            if (_dispatch is { } d)
                d(_save);
            else
                _save();
        }
        catch
        {
            // Best-effort: settings save failures must not crash the
            // viewer. The next RequestSave (or Flush) will try again.
        }
    }

    /// <summary>
    /// Flushes any pending save and releases the timer. Safe to call
    /// multiple times.
    /// </summary>
    public void Dispose()
    {
        Flush();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
        }
    }
}
