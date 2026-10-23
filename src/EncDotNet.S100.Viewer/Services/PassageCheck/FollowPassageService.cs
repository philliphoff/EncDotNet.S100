namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// Follow passage (#904, handoff E3): while it's on, the view time is pinned
/// to the ETA at the profile cursor (or at the minimum UKC when there's no
/// cursor), so the S-104 and S-111 layers show what the ship meets there.
/// </summary>
/// <remarks>
/// <para>
/// Turning it on pins the Timeline. Turning it off leaves the view time
/// where it is. Choosing another time on the Timeline (a scrub, a step, Go
/// live) turns it off, as does turning Check passage off.
/// </para>
/// <para>
/// A route without a departure is checked from the view time, so following
/// would move its own departure. Turning Follow passage on therefore sets
/// the route's departure to the one being checked first, as one undoable
/// edit.
/// </para>
/// </remarks>
internal sealed class FollowPassageService
{
    private readonly PassageCheckService _check;
    private readonly GlobalTimeService _time;
    private DateTime? _applied;
    private bool _applying;

    public FollowPassageService(PassageCheckService check, GlobalTimeService time)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(time);
        _check = check;
        _time = time;
        _check.Changed += (_, _) => OnCheckChanged();
        _time.CurrentTimeChanged += OnViewTimeChanged;
        _time.ModeChanged += mode =>
        {
            if (mode == TimeMode.Live)
                SetOn(false);
        };
    }

    /// <summary>Whether Follow passage is on.</summary>
    public bool IsOn { get; private set; }

    /// <summary>
    /// Whether Follow passage can be turned on: Check passage has a result
    /// with ETAs and some time-aware data is loaded.
    /// </summary>
    public bool CanFollow =>
        _time.IsActive && _check.IsActive && _check.Report is { Clearance.Arrival: not null } && _check.Current is not null;

    /// <summary>The view time Follow passage last set, or <c>null</c>.</summary>
    public DateTime? FollowedTime => IsOn ? _applied : null;

    /// <summary>Raised when <see cref="IsOn"/> or <see cref="CanFollow"/> may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Turns Follow passage on or off.</summary>
    /// <param name="on">The new state.</param>
    /// <returns><c>false</c> when it can't be turned on (<see cref="CanFollow"/> is false).</returns>
    public bool SetOn(bool on)
    {
        if (on == IsOn)
            return !on || CanFollow;
        if (on && !CanFollow)
            return false;

        if (on && !_check.HasRouteDeparture && _check.Departure is { } departure)
            _check.SetDeparture(departure);

        IsOn = on;
        if (!on)
            _applied = null;
        Changed?.Invoke(this, EventArgs.Empty);
        if (on)
            Apply();
        return true;
    }

    private void OnCheckChanged()
    {
        if (IsOn && !_check.IsActive)
        {
            SetOn(false);
            return;
        }

        if (IsOn)
            Apply();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Someone else chose a time: the user scrubbed or stepped, or an agent set it.
    private void OnViewTimeChanged(DateTime time)
    {
        if (IsOn && !_applying && _applied is { } applied && time != applied)
            SetOn(false);
    }

    /// <summary>The time the view should follow: the ETA at the cursor, else at the minimum.</summary>
    internal DateTimeOffset? Target()
    {
        if (_check.Current is not { } current)
            return null;
        if (_check.Cursor is { Eta: { } eta })
            return eta;
        return current.Result.Minimum is { HasEta: true } min ? min.Time : null;
    }

    private void Apply()
    {
        if (Target() is not { } target)
            return;
        var utc = target.UtcDateTime;
        if (_applied == utc && _time.CurrentTime == utc)
            return;
        _applied = utc;
        _applying = true;
        try
        {
            _time.SetCurrentTime(utc);
        }
        finally
        {
            _applying = false;
        }
    }
}
