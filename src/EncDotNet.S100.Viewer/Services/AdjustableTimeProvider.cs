namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// A <see cref="TimeProvider"/> whose "now" a scripted test can move or freeze
/// (the MCP <c>set_test_clock</c> tool, registered only with
/// <c>--mcp-test-hooks</c>). Every clock-driven view model resolves the
/// viewer's one <see cref="TimeProvider"/>, so moving this clock ages
/// forecasts, expires runs and advances a Live timeline everywhere at once.
/// </summary>
/// <remarks>
/// Monotonic timestamps and timer cadence stay real; only wall-clock time is
/// adjusted. After each adjustment every live periodic timer created through
/// this provider fires once, so minute-tick consumers (the Timeline's Now
/// marker, the Library's expiry check) react at once rather than up to a
/// minute later. One-shot timers (e.g. notification auto-dismiss) are left
/// alone.
/// </remarks>
internal sealed class AdjustableTimeProvider : TimeProvider
{
    private readonly TimeProvider _inner;
    private readonly object _sync = new();
    private readonly List<AdjustableTimer> _timers = [];
    private TimeSpan _offset;
    private DateTimeOffset? _frozenAt;

    /// <summary>Creates a provider over <paramref name="inner"/> (the system clock by default).</summary>
    public AdjustableTimeProvider(TimeProvider? inner = null)
    {
        _inner = inner ?? System;
    }

    /// <summary>Gets the offset added to the real clock while not frozen.</summary>
    public TimeSpan Offset
    {
        get { lock (_sync) return _offset; }
    }

    /// <summary>Gets the time the clock is frozen at, or <see langword="null"/> while it runs.</summary>
    public DateTimeOffset? FrozenAt
    {
        get { lock (_sync) return _frozenAt; }
    }

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    /// <inheritdoc />
    public override long TimestampFrequency => _inner.TimestampFrequency;

    /// <inheritdoc />
    public override long GetTimestamp() => _inner.GetTimestamp();

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
            return _frozenAt ?? _inner.GetUtcNow() + _offset;
    }

    /// <summary>
    /// Sets "now" to <paramref name="now"/>. When <paramref name="freeze"/> is
    /// true the clock stops there; otherwise it runs on from it.
    /// </summary>
    public void SetNow(DateTimeOffset now, bool freeze)
    {
        lock (_sync)
        {
            if (freeze)
            {
                _frozenAt = now;
                _offset = TimeSpan.Zero;
            }
            else
            {
                _frozenAt = null;
                _offset = now - _inner.GetUtcNow();
            }
        }
        FirePeriodicTimers();
    }

    /// <summary>Moves "now" by <paramref name="delta"/>, frozen or running.</summary>
    public void Advance(TimeSpan delta)
    {
        lock (_sync)
        {
            if (_frozenAt is { } frozen)
                _frozenAt = frozen + delta;
            else
                _offset += delta;
        }
        FirePeriodicTimers();
    }

    /// <summary>Returns to the real clock.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _offset = TimeSpan.Zero;
            _frozenAt = null;
        }
        FirePeriodicTimers();
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new AdjustableTimer(this, callback, state, period);
        timer.Inner = _inner.CreateTimer(callback, state, dueTime, period);
        lock (_sync)
            _timers.Add(timer);
        return timer;
    }

    private void FirePeriodicTimers()
    {
        AdjustableTimer[] timers;
        lock (_sync)
            timers = [.. _timers.Where(timer => timer.IsPeriodic)];
        foreach (var timer in timers)
            timer.Fire();
    }

    private void Remove(AdjustableTimer timer)
    {
        lock (_sync)
            _timers.Remove(timer);
    }

    private sealed class AdjustableTimer(
        AdjustableTimeProvider owner,
        TimerCallback callback,
        object? state,
        TimeSpan period) : ITimer
    {
        private TimeSpan _period = period;

        public ITimer Inner { get; set; } = null!;

        public bool IsPeriodic => _period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            return Inner.Change(dueTime, period);
        }

        public void Dispose()
        {
            owner.Remove(this);
            Inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            owner.Remove(this);
            return Inner.DisposeAsync();
        }
    }
}
