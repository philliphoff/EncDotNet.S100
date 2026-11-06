namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>Tunables for <see cref="PrewarmConcurrency"/>.</summary>
internal sealed record PrewarmConcurrencyOptions
{
    /// <summary>The most cells portrayed at once. At least 1.</summary>
    public required int MaxConcurrency { get; init; }

    /// <summary>How many cells run at once when pre-warming starts. Clamped to 1…<see cref="MaxConcurrency"/>.</summary>
    public required int InitialConcurrency { get; init; }

    /// <summary>How long a GC pause share is measured over before the limit changes.</summary>
    public required TimeSpan Window { get; init; }

    /// <summary>A window whose GC pause share is above this halves the limit; 1 never backs off.</summary>
    public required double BackOffPauseShare { get; init; }

    /// <summary>A full window whose GC pause share is below this raises the limit by one.</summary>
    public required double GrowPauseShare { get; init; }

    /// <summary>
    /// The environment variable that pins the limit to a fixed number of
    /// cells, for the perf harness's baselines (1 is the pre-slice-3 warmer).
    /// </summary>
    public const string FixedConcurrencyVariable = "S100_PREWARM_CONCURRENCY";

    /// <summary>
    /// <see cref="ForProcessors"/>, unless <paramref name="fixedConcurrency"/>
    /// (the value of <see cref="FixedConcurrencyVariable"/>) is a positive
    /// whole number: then the limit starts and stays there, whatever the GC
    /// pause share.
    /// </summary>
    /// <param name="processorCount">Logical processors.</param>
    /// <param name="fixedConcurrency">The variable's value, or null.</param>
    /// <returns>The options.</returns>
    public static PrewarmConcurrencyOptions Resolve(int processorCount, string? fixedConcurrency)
    {
        var options = ForProcessors(processorCount);
        return int.TryParse(fixedConcurrency, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0
            ? options with { MaxConcurrency = n, InitialConcurrency = n, BackOffPauseShare = 1, GrowPauseShare = 0 }
            : options;
    }

    /// <summary>
    /// The viewer's defaults: up to all cores but two (one for the UI thread,
    /// one for the renderer and the GC's own threads), starting at half of
    /// that; over two-second windows, back off above 15% of wall time in GC
    /// pauses and grow below 5%. A single Server GC pause can be 100–200 ms,
    /// so a shorter window or a lower threshold halves on one collection.
    /// </summary>
    /// <param name="processorCount">Logical processors.</param>
    /// <returns>The options.</returns>
    public static PrewarmConcurrencyOptions ForProcessors(int processorCount)
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
}

/// <summary>
/// Decides how many cells <see cref="PortrayalCacheWarmer"/> portrays at once
/// (map rendering slice 3): additive increase while the share of wall time the
/// process spends in GC pauses stays low, multiplicative decrease when it
/// rises. GC pauses stop every thread, the map's included, so they are the cost
/// the background work must keep down; once they climb, more workers add
/// garbage faster than they add throughput.
/// </summary>
/// <remarks>
/// <para>
/// The warmer calls <see cref="Observe"/> on every pass while it portrays.
/// Each <see cref="PrewarmConcurrencyOptions.Window"/> the share of the GC
/// pause time in that window is compared with the two thresholds. The limit
/// only grows after a window in which every slot was busy, so a queue that is
/// running dry doesn't inflate it.
/// </para>
/// <para>
/// <see cref="Restart"/> drops the open window when the warmer pauses for the
/// map: pauses while the map loads are the map's, not the warmer's. The limit
/// itself is kept, so the next idle period starts where the last one ended.
/// </para>
/// <para>
/// Not thread-safe; the warmer's loop owns it. It has no other state, so a
/// work scheduler can host it as an admission policy (slice 5).
/// </para>
/// </remarks>
internal sealed class PrewarmConcurrency
{
    private readonly PrewarmConcurrencyOptions _options;
    private readonly Func<TimeSpan> _gcPauseTotal;
    private readonly TimeProvider _time;
    private long? _windowStart;
    private TimeSpan _windowPauseStart;
    private bool _windowSaturated;

    /// <summary>Creates the governor.</summary>
    /// <param name="options">Bounds and thresholds.</param>
    /// <param name="gcPauseTotal">Total GC pause time since the process started (<see cref="GC.GetTotalPauseDuration"/>).</param>
    /// <param name="time">Clock for the windows.</param>
    public PrewarmConcurrency(PrewarmConcurrencyOptions options, Func<TimeSpan> gcPauseTotal, TimeProvider time)
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

    /// <summary>How many cells may be portrayed at once now.</summary>
    public int Limit { get; private set; }

    /// <summary>The GC pause share of the last full window, or null before one closes.</summary>
    public double? LastPauseShare { get; private set; }

    /// <summary>
    /// Notes a pass of the warmer while it portrays, and closes the window when
    /// it is due.
    /// </summary>
    /// <param name="running">Cells being portrayed now.</param>
    /// <returns>Whether <see cref="Limit"/> changed.</returns>
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

        // The next window is judged by the passes inside it: after a change
        // the warmer fills the new slots before it observes again.
        _windowStart = now;
        _windowPauseStart = _gcPauseTotal();
        _windowSaturated = true;
        return Limit != previous;
    }

    /// <summary>Drops the open window; the next <see cref="Observe"/> starts a new one. The limit is kept.</summary>
    public void Restart() => _windowStart = null;
}
