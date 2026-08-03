using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Projects a <see cref="MapsuiMapSession"/>'s aggregate time state into the
/// Viewer's global timeline model.
/// </summary>
internal sealed class GlobalTimeService
{
    private MapsuiMapSession? _session;
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

    /// <summary>Raised whenever the aggregate timeline range changes.</summary>
    public event Action? RangeChanged;

    /// <summary>Raised whenever the global map clock changes.</summary>
    public event Action<DateTime>? CurrentTimeChanged;

    /// <summary>Attaches this Viewer projection to the reusable map session.</summary>
    public void AttachTo(MapsuiMapSession session)
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
    }

    /// <summary>Sets the session clock, clamped to the aggregate range.</summary>
    public void SetCurrentTime(DateTime time)
    {
        if (_session is not null)
        {
            _session.SetCurrentTime(time);
            return;
        }
        if (_snapshot.Minimum is not { } minimum
            || _snapshot.Maximum is not { } maximum)
        {
            return;
        }

        var clamped = time < minimum
            ? minimum
            : time > maximum
                ? maximum
                : time;
        if (_snapshot.Current == clamped)
            return;

        _snapshot = new MapsuiMapTimeSnapshot
        {
            Minimum = _snapshot.Minimum,
            Maximum = _snapshot.Maximum,
            Current = clamped,
            Samples = _snapshot.Samples,
            CoverageSegments = _snapshot.CoverageSegments,
        };
        CurrentTimeChanged?.Invoke(clamped);
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

    private void OnTimeRangeChanged()
    {
        UpdateSnapshot();
        RangeChanged?.Invoke();
    }

    private void OnCurrentTimeChanged(DateTime time)
    {
        UpdateSnapshot();
        CurrentTimeChanged?.Invoke(time);
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
