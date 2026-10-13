namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The time a route's ETAs run from when the route has no departure
/// (handoff decision 2): the Timeline's view time.
/// </summary>
internal interface IRouteViewTime
{
    /// <summary>The current view time.</summary>
    DateTimeOffset Current { get; }

    /// <summary>Raised when <see cref="Current"/> moves.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// An <see cref="IRouteViewTime"/> that is always now. Used where there is no
/// Timeline, such as in tests and headless hosts.
/// </summary>
internal sealed class ClockRouteViewTime(TimeProvider clock) : IRouteViewTime
{
    /// <inheritdoc />
    public DateTimeOffset Current => clock.GetUtcNow();

    /// <inheritdoc />
    public event EventHandler? Changed { add { } remove { } }
}

/// <summary>
/// The Timeline's view time, from <see cref="GlobalTimeService"/>. With no
/// time-aware data loaded the Timeline has no view time, so this is now.
/// </summary>
internal sealed class TimelineRouteViewTime : IRouteViewTime
{
    private readonly GlobalTimeService _time;
    private readonly TimeProvider _clock;

    public TimelineRouteViewTime(GlobalTimeService time, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(clock);
        _time = time;
        _clock = clock;
        _time.CurrentTimeChanged += _ => Changed?.Invoke(this, EventArgs.Empty);
        _time.RangeChanged += () => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public DateTimeOffset Current => _time is { IsActive: true, CurrentTime: { } t }
        ? new DateTimeOffset(t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc))
        : _clock.GetUtcNow();

    /// <inheritdoc />
    public event EventHandler? Changed;
}
