using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Projects a <see cref="MapsuiDatasetLayerSession"/>'s aggregate time state into the
/// Viewer's global timeline model.
/// </summary>
internal sealed class GlobalTimeService
{
    private MapsuiDatasetLayerSession? _session;
    private MapsuiMapTimeSnapshot _snapshot = MapsuiMapTimeSnapshot.Empty;

    /// <summary>The earliest sample across all registered datasets.</summary>
    public DateTime? MinTime => _snapshot.Minimum;

    /// <summary>The latest sample across all registered datasets.</summary>
    public DateTime? MaxTime => _snapshot.Maximum;

    /// <summary>The current global map clock.</summary>
    public DateTime? CurrentTime => _snapshot.Current;

    /// <summary>True when at least one registered dataset has time samples.</summary>
    public bool IsActive => _snapshot.IsActive;

    /// <summary>All distinct registered samples in ascending order.</summary>
    public IReadOnlyList<DateTime> AllSamples => _snapshot.Samples;

    /// <summary>
    /// Merged, ascending intervals over which at least one registered dataset
    /// portrays data.
    /// </summary>
    public IReadOnlyList<CoverageSegment> CoverageSegments { get; private set; } = [];

    /// <summary>The registered datasets with time samples, and the span each covers.</summary>
    public IReadOnlyList<MapsuiMapTimedDataset> TimedDatasets => _snapshot.Datasets;

    /// <summary>Raised whenever the aggregate timeline range changes.</summary>
    public event Action? RangeChanged;

    /// <summary>Raised whenever the global map clock changes.</summary>
    public event Action<DateTime>? CurrentTimeChanged;

    /// <summary>Attaches this Viewer projection to the reusable map session.</summary>
    public void AttachTo(MapsuiDatasetLayerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (ReferenceEquals(_session, session))
            return;
        if (_session is not null)
            throw new InvalidOperationException(
                "GlobalTimeService is already attached to a map session.");

        _session = session;
        _session.TimeRangeChanged += OnTimeRangeChanged;
        _session.CurrentTimeChanged += OnCurrentTimeChanged;
        UpdateSnapshot();
        RangeChanged?.Invoke();
        if (_snapshot.Current is { } current)
            CurrentTimeChanged?.Invoke(current);
    }

    /// <summary>
    /// Whether the view time follows now (<see cref="TimeMode.Live"/>, set by
    /// <see cref="GoLive"/>) or stays where it was put
    /// (<see cref="TimeMode.Pinned"/>, set by any <see cref="SetCurrentTime"/>).
    /// Loading or replacing a dataset does not change it (#706, #713).
    /// </summary>
    public TimeMode Mode { get; private set; } = TimeMode.Pinned;

    /// <summary>Raised when <see cref="Mode"/> changes.</summary>
    public event Action<TimeMode>? ModeChanged;

    /// <summary>
    /// Sets the view time. A user's choice of time (scrub, step, a jump to
    /// data, an agent's set_view_time): it pins the time, leaving Live. The
    /// time is not clamped to the loaded range.
    /// </summary>
    public void SetCurrentTime(DateTime time)
    {
        SetMode(TimeMode.Pinned);
        SetClock(time);
    }

    /// <summary>
    /// Sets the view time to <paramref name="now"/> and enters
    /// <see cref="TimeMode.Live"/>, so the timeline moves it on as now
    /// advances, even past every loaded window.
    /// </summary>
    public void GoLive(DateTime now)
    {
        SetMode(TimeMode.Live);
        SetClock(now);
    }

    private void SetMode(TimeMode mode)
    {
        if (Mode == mode)
            return;
        Mode = mode;
        ModeChanged?.Invoke(mode);
    }

    private void SetClock(DateTime time)
    {
        if (_session is not null)
        {
            _session.SetCurrentTime(time);
            return;
        }
        if (!_snapshot.IsActive || _snapshot.Current == time)
            return;

        _snapshot = new MapsuiMapTimeSnapshot
        {
            Minimum = _snapshot.Minimum,
            Maximum = _snapshot.Maximum,
            Current = time,
            Samples = _snapshot.Samples,
            CoverageSegments = _snapshot.CoverageSegments,
            Datasets = _snapshot.Datasets,
        };
        CurrentTimeChanged?.Invoke(time);
    }

    internal void ApplySnapshot(MapsuiMapTimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_session is not null)
            throw new InvalidOperationException(
                "An attached time projection can only be updated by its map session.");

        _snapshot = snapshot;
        CoverageSegments = snapshot.CoverageSegments
            .Select(segment => new CoverageSegment(segment.Start, segment.End))
            .ToArray();
        RangeChanged?.Invoke();
    }

    private void OnTimeRangeChanged(object? sender, EventArgs e)
    {
        UpdateSnapshot();
        RangeChanged?.Invoke();
    }

    private void OnCurrentTimeChanged(object? sender, MapSessionCurrentTimeEventArgs e)
    {
        UpdateSnapshot();
        // Forward the session's clock as it is now, not the event's value: a
        // range change raises its clock event after RangeChanged handlers ran,
        // and one of them may already have moved the clock (the Timeline going
        // Live on load). Re-applying the stale value would undo that (#713).
        if (_snapshot.Current is { } current)
            CurrentTimeChanged?.Invoke(current);
    }

    private void UpdateSnapshot()
    {
        _snapshot = _session?.GetTimeSnapshot() ?? MapsuiMapTimeSnapshot.Empty;
        CoverageSegments = _snapshot.CoverageSegments
            .Select(segment => new CoverageSegment(segment.Start, segment.End))
            .ToArray();
    }
}

/// <summary>
/// A single contiguous time range over which the global timeline has data.
/// </summary>
internal readonly record struct CoverageSegment(DateTime Start, DateTime End);

/// <summary>Whether the view time follows now or stays where it was put (#713).</summary>
internal enum TimeMode
{
    /// <summary>The view time was chosen (scrub, step, jump) and stays there.</summary>
    Pinned,

    /// <summary>The view time is now and moves on with the clock.</summary>
    Live,
}
