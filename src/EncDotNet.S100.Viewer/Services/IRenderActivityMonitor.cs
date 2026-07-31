namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Per-style aggregate for a single completed map paint: how many
/// style-renderer <c>Draw</c> calls of a given style type ran and how
/// long they took in total. Aggregated across layers and geometry-size
/// buckets so the list stays compact.
/// </summary>
/// <param name="Style">Style-renderer type name (e.g. <c>VectorStyle</c>, <c>SymbolStyle</c>, <c>LabelStyle</c>).</param>
/// <param name="Calls">Number of <c>Draw</c> calls of this style in the paint.</param>
/// <param name="DurationMs">Cumulative wall-clock duration of those calls, in milliseconds.</param>
internal sealed record RenderStyleStat(string Style, long Calls, double DurationMs);

/// <summary>
/// Read-only snapshot of the most recently completed map paint on the
/// live compositor render thread.
/// </summary>
/// <remarks>
/// These figures describe the on-screen <c>InstrumentedMapControl</c>
/// paint — <b>not</b> the offscreen PNG produced by
/// <c>render_to_image</c>, which clones the map and renders on a
/// separate path. Pair with <c>await_render_idle</c> to ensure the
/// snapshot reflects a settled view.
/// </remarks>
/// <param name="FrameDurationMs">Wall-clock duration of the paint, in milliseconds.</param>
/// <param name="IntervalMs">Interval since the previous paint completed, in milliseconds, or <see langword="null"/> for the first observed paint.</param>
/// <param name="TotalDrawCalls">Total style-renderer <c>Draw</c> calls across all styles in the paint.</param>
/// <param name="Styles">Per-style breakdown, ordered by descending duration.</param>
/// <param name="PaintSequence">Monotonic 1-based index of this paint since the monitor started observing.</param>
/// <param name="CapturedAtUtc">UTC wall-clock time the snapshot was published.</param>
internal sealed record RenderStatsSnapshot(
    double FrameDurationMs,
    double? IntervalMs,
    long TotalDrawCalls,
    IReadOnlyList<RenderStyleStat> Styles,
    long PaintSequence,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Aggregate cost statistics over a rolling window of recently completed
/// paints. Where <see cref="RenderStatsSnapshot"/> reports only the
/// single most recent paint — which, after a view settles, is a cheap
/// cached repaint — these aggregates retain the worst frames seen during
/// a burst of activity (e.g. a continuous pan/zoom stress run), so a
/// transient expensive paint is never missed between polls.
/// </summary>
/// <param name="Count">Number of paints currently retained in the window.</param>
/// <param name="FirstSequence">Paint sequence of the oldest retained paint, or 0 when empty.</param>
/// <param name="LastSequence">Paint sequence of the newest retained paint, or 0 when empty.</param>
/// <param name="FrameMaxMs">Maximum whole-frame paint duration over the window, in milliseconds.</param>
/// <param name="FrameMeanMs">Mean whole-frame paint duration over the window, in milliseconds.</param>
/// <param name="FrameP95Ms">95th-percentile whole-frame paint duration over the window, in milliseconds.</param>
/// <param name="VectorMaxMs">Maximum cumulative <c>VectorStyle</c> draw duration in a single paint over the window, in milliseconds.</param>
/// <param name="VectorMeanMs">Mean per-paint cumulative <c>VectorStyle</c> draw duration over the window, in milliseconds.</param>
/// <param name="VectorP95Ms">95th-percentile per-paint cumulative <c>VectorStyle</c> draw duration over the window, in milliseconds.</param>
/// <param name="MaxTotalDrawCalls">Maximum total style draw calls in a single paint over the window.</param>
internal sealed record RenderWindowStats(
    long Count,
    long FirstSequence,
    long LastSequence,
    double FrameMaxMs,
    double FrameMeanMs,
    double FrameP95Ms,
    double VectorMaxMs,
    double VectorMeanMs,
    double VectorP95Ms,
    long MaxTotalDrawCalls)
{
    /// <summary>An empty window (no paints retained).</summary>
    public static RenderWindowStats Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>Outcome of <see cref="IRenderActivityMonitor.WaitForIdleAsync"/>.</summary>
/// <param name="WentIdle">
/// <see langword="true"/> when the map quiesced (no paint or refresh
/// request for the requested quiet period, and no layer reporting busy)
/// before the timeout elapsed.
/// </param>
/// <param name="TimedOut">
/// <see langword="true"/> when the timeout elapsed before the map
/// quiesced. Mutually exclusive with <see cref="WentIdle"/>.
/// </param>
/// <param name="WaitedMs">Total wall-clock time spent waiting, in milliseconds.</param>
/// <param name="PaintsObserved">Number of paints that completed while the call was waiting.</param>
/// <param name="QuietForMs">
/// Milliseconds since the last observed render activity at the moment
/// the call returned.
/// </param>
internal readonly record struct RenderIdleResult(
    bool WentIdle,
    bool TimedOut,
    double WaitedMs,
    long PaintsObserved,
    double QuietForMs);

/// <summary>
/// Observes the viewer's live map render activity so scripted / agent
/// callers can (a) block until the map settles before screenshotting
/// and (b) read back the cost of the last paint. Registered as a DI
/// singleton; fed from the render thread via <see cref="IRenderActivitySink"/>.
/// </summary>
/// <remarks>
/// All members are safe to call from any thread. The monitor exists from
/// app startup and simply reports "no activity" until the map control
/// begins painting.
/// </remarks>
internal interface IRenderActivityMonitor
{
    /// <summary>
    /// Total number of paints observed since startup. Monotonic.
    /// </summary>
    long PaintCount { get; }

    /// <summary>
    /// The most recently completed paint's statistics, or
    /// <see langword="null"/> when no paint has been observed yet.
    /// </summary>
    RenderStatsSnapshot? LatestStats { get; }

    /// <summary>
    /// A predicate reporting whether the map currently has a render in
    /// flight (e.g. a tile or feature fetch that has not yet produced a
    /// paint). Set by the host once the map control exists; when
    /// unset, the monitor assumes the map is never busy. Used to keep
    /// <see cref="WaitForIdleAsync"/> from reporting idle while an
    /// asynchronous fetch is still pending.
    /// </summary>
    Func<bool>? BusyProbe { get; set; }

    /// <summary>
    /// Waits until the live map has been quiet — no completed paint, no
    /// graphics-refresh request, and no busy layer — for a continuous
    /// <paramref name="quietPeriod"/>, or until <paramref name="timeout"/>
    /// elapses, whichever comes first.
    /// </summary>
    /// <param name="quietPeriod">
    /// Continuous span of inactivity that qualifies as idle. The call
    /// always waits at least this long from invocation, which gives a
    /// just-requested paint (e.g. from a preceding viewport change) time
    /// to begin before idle can be declared.
    /// </param>
    /// <param name="timeout">Maximum total time to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The wait outcome.</returns>
    Task<RenderIdleResult> WaitForIdleAsync(
        TimeSpan quietPeriod,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns aggregate cost statistics over the rolling window of
    /// recently observed paints. Implementations that do not track a
    /// window return <see cref="RenderWindowStats.Empty"/>.
    /// </summary>
    RenderWindowStats GetWindowStats() => RenderWindowStats.Empty;

    /// <summary>
    /// Clears the rolling paint window so a subsequent
    /// <see cref="GetWindowStats"/> reflects only paints observed after
    /// this call. Lets a caller isolate a measurement phase. No-op for
    /// implementations that do not track a window.
    /// </summary>
    void ResetWindow() { }
}

/// <summary>
/// Render-thread-facing write surface of the render activity monitor.
/// Implemented by the same singleton that implements
/// <see cref="IRenderActivityMonitor"/>; kept separate so the
/// instrumentation plumbing depends only on the narrow write contract.
/// </summary>
internal interface IRenderActivitySink
{
    /// <summary>
    /// Records a completed paint. Called once per paint from the
    /// compositor render thread.
    /// </summary>
    /// <param name="frameDurationMs">Wall-clock duration of the paint, in milliseconds.</param>
    /// <param name="styles">
    /// Read-only per-style breakdown for the paint. The caller must pass
    /// a fresh list the monitor can retain; the monitor does not copy it.
    /// </param>
    void NotifyPaint(double frameDurationMs, IReadOnlyList<RenderStyleStat> styles);

    /// <summary>
    /// Records non-paint render activity (e.g. a graphics-refresh
    /// request raised after an asynchronous data fetch). Resets the
    /// quiet timer without producing a stats snapshot. Safe to call
    /// from any thread.
    /// </summary>
    void NotifyActivity();
}
