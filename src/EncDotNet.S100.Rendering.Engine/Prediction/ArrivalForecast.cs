namespace EncDotNet.S100.Rendering.Engine.Prediction;

/// <summary>
/// The cells the current view is still waiting for (map rendering
/// slice&#160;9): the ones the cell demand driver has queued for the view, and
/// the ones it has started loading that haven't reached the chart stack yet.
/// The chart stack holds a compose while arrivals are pending, so cells that
/// land close together reveal in one stack version rather than one each.
/// </summary>
/// <remarks>
/// A load is identified by a token the driver chooses (the viewer uses the
/// load's <see cref="Core.Scheduling.WorkDemand"/>, which flows to the
/// render). Thread-safe.
/// </remarks>
public sealed class ArrivalForecast
{
    private readonly Lock _sync = new();
    private readonly HashSet<object> _inFlight = new(ReferenceEqualityComparer.Instance);
    private int _queued;
    private long _version;

    /// <summary>Cells queued or in flight for the view.</summary>
    public int Pending
    {
        get
        {
            lock (_sync)
            {
                return _queued + _inFlight.Count;
            }
        }
    }

    /// <summary>
    /// Cells the view has started loading that haven't reached the chart
    /// stack: the arrivals that are on their way, as opposed to queued ones
    /// that may not start for seconds.
    /// </summary>
    public int InFlight
    {
        get
        {
            lock (_sync)
            {
                return _inFlight.Count;
            }
        }
    }

    /// <summary>Bumped on every change, so a waiter can tell that something arrived.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Sets how many cells the view has queued but not started.</summary>
    /// <param name="count">The number of cells.</param>
    public void SetQueued(int count)
    {
        lock (_sync)
        {
            var value = Math.Max(0, count);
            if (_queued != value)
            {
                _queued = value;
                Interlocked.Increment(ref _version);
            }
        }
    }

    /// <summary>Notes that the load <paramref name="token"/> has started for the view.</summary>
    /// <param name="token">The load's token.</param>
    public void Expect(object token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_sync)
        {
            if (_inFlight.Add(token))
            {
                Interlocked.Increment(ref _version);
            }
        }
    }

    /// <summary>
    /// Notes that the load <paramref name="token"/> has reached the chart
    /// stack, or stopped: it is no longer pending. Unknown tokens are ignored.
    /// </summary>
    /// <param name="token">The load's token, or <see langword="null"/>.</param>
    public void Resolve(object? token)
    {
        if (token is null)
        {
            return;
        }

        lock (_sync)
        {
            if (_inFlight.Remove(token))
            {
                Interlocked.Increment(ref _version);
            }
        }
    }

    /// <summary>Forgets every pending arrival.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _inFlight.Clear();
            _queued = 0;
            Interlocked.Increment(ref _version);
        }
    }
}
