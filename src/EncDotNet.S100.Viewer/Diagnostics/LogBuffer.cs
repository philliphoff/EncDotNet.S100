using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Thread-safe ring buffer of the viewer's most recent log entries (#1080).
/// Fed by <see cref="LogBufferLoggerProvider"/>, which is registered in
/// <see cref="ViewerObservability.AddS100Observability(Microsoft.Extensions.DependencyInjection.IServiceCollection, string?, bool)"/>
/// so it always runs; read by the Logs panel, the feedback report and the
/// <c>get_recent_logs</c> MCP tool. Nothing in it leaves the machine unless
/// the user sends it.
/// </summary>
/// <remarks>
/// <para>
/// Logs arrive from any thread (background loaders, network fetches). Writes
/// take a short lock; <see cref="Changed"/> is raised at most once per
/// notification interval, on a timer thread, however many entries arrived,
/// so a burst of messages during a heavy load costs the UI one refresh.
/// </para>
/// <para>
/// The buffer has its own <see cref="MinimumLevel"/>, separate from the
/// logging pipeline's global minimum (which <c>--verbose</c> sets and the
/// OTLP exporter follows), so the Logs panel's Everything level changes only
/// what the panel captures.
/// </para>
/// </remarks>
internal sealed class LogBuffer : IDisposable
{
    /// <summary>How many entries the buffer keeps by default.</summary>
    public const int DefaultCapacity = 1000;

    /// <summary>How long the buffer waits after a write before raising <see cref="Changed"/>.</summary>
    public static readonly TimeSpan DefaultNotifyInterval = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly LogEntry?[] _ring;
    private readonly TimeProvider _time;
    private readonly TimeSpan _notifyInterval;
    private readonly ITimer _notifyTimer;
    private int _head;
    private int _count;
    private long _nextSequence = 1;
    private int _notifyPending;
    private int _minimumLevel;
    private long _totalAdded;
    private long _problemsAdded;
    private bool _disposed;

    /// <summary>Creates a buffer.</summary>
    /// <param name="capacity">How many entries to keep; older ones are dropped.</param>
    /// <param name="minimumLevel">The lowest level captured.</param>
    /// <param name="timeProvider">Clock for timestamps and the notification timer.</param>
    /// <param name="notifyInterval">How long to batch writes before raising <see cref="Changed"/>.</param>
    public LogBuffer(
        int capacity = DefaultCapacity,
        LogLevel minimumLevel = LogLevel.Information,
        TimeProvider? timeProvider = null,
        TimeSpan? notifyInterval = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _ring = new LogEntry?[capacity];
        _minimumLevel = (int)minimumLevel;
        _time = timeProvider ?? TimeProvider.System;
        _notifyInterval = notifyInterval ?? DefaultNotifyInterval;
        _notifyTimer = _time.CreateTimer(_ => RaiseChanged(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>How many entries the buffer keeps.</summary>
    public int Capacity => _ring.Length;

    /// <summary>
    /// The lowest level the buffer captures. Defaults to Information; the
    /// Logs panel's Everything level lowers it to Debug.
    /// </summary>
    public LogLevel MinimumLevel
    {
        get => (LogLevel)Volatile.Read(ref _minimumLevel);
        set
        {
            if (Interlocked.Exchange(ref _minimumLevel, (int)value) != (int)value)
                MinimumLevelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised, on the setting thread, when <see cref="MinimumLevel"/> changes.</summary>
    public event EventHandler? MinimumLevelChanged;

    /// <summary>
    /// Raised after entries were added, collapsed or cleared, at most once per
    /// notification interval and on a timer thread. Handlers read the new
    /// state with <see cref="Snapshot"/>.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// How many messages were captured since the buffer was created, counting
    /// each collapsed repeat; never reset, not even by <see cref="Clear"/>.
    /// </summary>
    public long TotalAdded => Interlocked.Read(ref _totalAdded);

    /// <summary>
    /// How many Warning, Error and Critical messages were captured since the
    /// buffer was created, counting repeats; never reset. The Logs tab's
    /// unread badge is the growth of this count since the tab was last shown.
    /// </summary>
    public long ProblemsAdded => Interlocked.Read(ref _problemsAdded);

    /// <summary>Whether a message at <paramref name="level"/> would be captured.</summary>
    public bool IsEnabled(LogLevel level) =>
        level != LogLevel.None && (int)level >= Volatile.Read(ref _minimumLevel);

    /// <summary>
    /// Adds a message, or folds it into the newest entry when it repeats that
    /// entry's level, category, message and exception. Ignored below
    /// <see cref="MinimumLevel"/>.
    /// </summary>
    public void Add(LogLevel level, string category, string message, Exception? exception = null) =>
        Add(level, category, message, exception?.ToString());

    /// <inheritdoc cref="Add(LogLevel, string, string, Exception?)"/>
    public void Add(LogLevel level, string category, string message, string? exceptionText)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(message);
        if (!IsEnabled(level))
            return;

        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_disposed)
                return;

            Interlocked.Increment(ref _totalAdded);
            if (level >= LogLevel.Warning)
                Interlocked.Increment(ref _problemsAdded);

            if (_count > 0)
            {
                var newestIndex = (_head + _count - 1) % _ring.Length;
                var newest = _ring[newestIndex]!;
                if (newest.IsRepeatOf(level, category, message, exceptionText))
                {
                    _ring[newestIndex] = newest with { Count = newest.Count + 1, LastTimestamp = now };
                    ScheduleChanged();
                    return;
                }
            }

            var entry = new LogEntry(_nextSequence++, now, now, level, category, message, exceptionText);
            if (_count < _ring.Length)
            {
                _ring[(_head + _count) % _ring.Length] = entry;
                _count++;
            }
            else
            {
                _ring[_head] = entry;
                _head = (_head + 1) % _ring.Length;
            }
        }
        ScheduleChanged();
    }

    /// <summary>The buffered entries, oldest first. A copy: safe to read on any thread.</summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            var copy = new LogEntry[_count];
            for (var i = 0; i < _count; i++)
                copy[i] = _ring[(_head + i) % _ring.Length]!;
            return copy;
        }
    }

    /// <summary>Removes every entry. Sequences keep counting up.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_ring);
            _head = 0;
            _count = 0;
        }
        ScheduleChanged();
    }

    /// <summary>Raises a pending <see cref="Changed"/> now instead of waiting for the interval.</summary>
    public void Flush()
    {
        if (Volatile.Read(ref _notifyPending) != 0)
            RaiseChanged();
    }

    private void ScheduleChanged()
    {
        if (Interlocked.CompareExchange(ref _notifyPending, 1, 0) != 0)
            return;
        try
        {
            _notifyTimer.Change(_notifyInterval, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Disposed while a late write arrived.
        }
    }

    private void RaiseChanged()
    {
        if (Interlocked.Exchange(ref _notifyPending, 0) == 0)
            return;
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A failing subscriber must never break logging.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
        _notifyTimer.Dispose();
    }
}
