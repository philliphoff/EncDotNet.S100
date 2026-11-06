namespace EncDotNet.S100.Core.Scheduling;

/// <summary>Settings for a <see cref="GcPauseGovernor"/>.</summary>
public sealed record GcPauseGovernorOptions
{
    /// <summary>The most jobs it ever admits at once.</summary>
    public required int MaxConcurrency { get; init; }

    /// <summary>The limit it starts at.</summary>
    public required int InitialConcurrency { get; init; }

    /// <summary>How long each judged window lasts.</summary>
    public required TimeSpan Window { get; init; }

    /// <summary>The GC pause share of a window above which the limit halves.</summary>
    public required double BackOffPauseShare { get; init; }

    /// <summary>
    /// The GC pause share of a window below which the limit grows by one, if
    /// the jobs it admitted filled the limit through the whole window.
    /// </summary>
    public required double GrowPauseShare { get; init; }

    /// <summary>
    /// The defaults for a machine with <paramref name="processorCount"/>
    /// logical processors: up to two fewer than the processors, starting at
    /// half that, judged every 2 s, halving above a 15% pause share and
    /// growing below 5%. These are the pre-warm governor's figures from map
    /// rendering slice 3.
    /// </summary>
    /// <param name="processorCount">Logical processors.</param>
    /// <returns>The options.</returns>
    public static GcPauseGovernorOptions ForProcessors(int processorCount)
    {
        var max = Math.Max(1, processorCount - 2);
        return new()
        {
            MaxConcurrency = max,
            InitialConcurrency = Math.Max(1, max / 2),
            Window = TimeSpan.FromSeconds(2),
            BackOffPauseShare = 0.15,
            GrowPauseShare = 0.05,
        };
    }

    /// <summary>Options that hold the limit at <paramref name="concurrency"/>.</summary>
    /// <param name="concurrency">The fixed limit, at least 1.</param>
    /// <returns>The options.</returns>
    public static GcPauseGovernorOptions Fixed(int concurrency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        return new()
        {
            MaxConcurrency = concurrency,
            InitialConcurrency = concurrency,
            Window = TimeSpan.FromSeconds(2),
            BackOffPauseShare = 1,
            GrowPauseShare = 0,
        };
    }
}

/// <summary>
/// Caps how many allocation-heavy jobs run at once by the share of wall time
/// the GC pauses the process: additive increase while pauses stay low and the
/// cap is used, multiplicative decrease when they climb. GC pauses stop every
/// thread, so past some point more allocating jobs make everything slower.
/// </summary>
/// <remarks>Not thread-safe: the caller serialises <see cref="Observe"/>.</remarks>
public sealed class GcPauseGovernor
{
    private readonly GcPauseGovernorOptions _options;
    private readonly Func<TimeSpan> _gcPauseTotal;
    private readonly TimeProvider _time;
    private long? _windowStart;
    private TimeSpan _windowPauseStart;
    private bool _windowSaturated;

    /// <summary>Creates a governor.</summary>
    /// <param name="options">The settings.</param>
    /// <param name="gcPauseTotal">The process's total GC pause time so far (<see cref="GC.GetTotalPauseDuration"/>).</param>
    /// <param name="time">The clock.</param>
    public GcPauseGovernor(GcPauseGovernorOptions options, Func<TimeSpan> gcPauseTotal, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(gcPauseTotal);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConcurrency, 1, nameof(options));
        _options = options;
        _gcPauseTotal = gcPauseTotal;
        _time = time;
        Limit = Math.Clamp(options.InitialConcurrency, 1, options.MaxConcurrency);
    }

    /// <summary>How many allocation-heavy jobs may run now.</summary>
    public int Limit { get; private set; }

    /// <summary>The pause share of the last judged window, or null before the first.</summary>
    public double? LastPauseShare { get; private set; }

    /// <summary>
    /// Notes how many admitted jobs are running and, once a window has
    /// passed, judges it and moves the limit.
    /// </summary>
    /// <param name="running">Admitted jobs running now.</param>
    /// <returns>Whether the limit changed.</returns>
    public bool Observe(int running)
    {
        var now = _time.GetTimestamp();
        if (_windowStart is not { } start)
        {
            _windowStart = now;
            _windowPauseStart = _gcPauseTotal();
            _windowSaturated = running >= Limit;
            return false;
        }

        _windowSaturated &= running >= Limit;
        var elapsed = _time.GetElapsedTime(start, now);
        if (elapsed < _options.Window)
            return false;

        var pause = _gcPauseTotal() - _windowPauseStart;
        var share = elapsed > TimeSpan.Zero ? Math.Clamp(pause / elapsed, 0, 1) : 0;
        LastPauseShare = share;
        var previous = Limit;
        if (share > _options.BackOffPauseShare)
            Limit = Math.Max(1, Limit / 2);
        else if (share < _options.GrowPauseShare && _windowSaturated)
            Limit = Math.Min(_options.MaxConcurrency, Limit + 1);

        _windowStart = now;
        _windowPauseStart = _gcPauseTotal();
        _windowSaturated = running >= Limit;
        return Limit != previous;
    }

    /// <summary>Forgets the current window, for when the admitted work stops and starts again.</summary>
    public void Restart() => _windowStart = null;
}
